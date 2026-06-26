#include <inttypes.h>
#include <limits.h>
#include <string.h>

#include "driver/gpio.h"
// [ESP-IDF] Esta macro silencia la advertencia de API TWAI clasica/deprecated
// sin cambiar la implementacion ni migrar de driver.
#define CONFIG_TWAI_SUPPRESS_DEPRECATE_WARN 1
#include "driver/twai.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"

#include "app_rtos_types.h"
#include "acuratex_usb_bridge.h"
#include "reply_dispatcher.h"
#include "tcp_server.h"
#include "can_driver_twai.h"

// [ESP-IDF] TAG identifica en los logs que el mensaje pertenece al driver CAN.
static const char *TAG = "can_twai";

// [ACURATEX] Pines fisicos del controlador TWAI/transceiver CAN. No se cambian
// en este estudio: TX=GPIO4, RX=GPIO5, STBY=GPIO6.
static const gpio_num_t CAN_TX_GPIO = GPIO_NUM_4;
static const gpio_num_t CAN_RX_GPIO = GPIO_NUM_5;
static const gpio_num_t CAN_STBY_GPIO = GPIO_NUM_6;

// [ACURATEX] Banderas internas del ciclo de vida TWAI.
static bool s_driver_installed = false;
static bool s_driver_started = false;
// [ACURATEX] Bus logico seleccionado por `can1`/`can2`. El ESP32-S3 usa un solo
// controlador TWAI; esta variable conserva la semantica de protocolo.
static int s_selected_bus = 1;
uint32_t app_usb_get_active_session_id(void);
uint32_t app_tcp_server_get_active_session_id(void);

#define APP_CAN_TX_QUEUE_LENGTH 16

typedef struct
{
    int bus;
    uint32_t id;
    uint8_t data[8];
    uint8_t length;
    uint32_t request_id;
    uint64_t enqueue_us;
    TaskHandle_t requester;
} app_can_tx_request_t;

static QueueHandle_t s_can_tx_queue = NULL;
static TaskHandle_t s_can_tx_task_handle = NULL;
static uint32_t s_can_tx_request_id = 0;
static uint32_t s_can_tx_queue_drops = 0;
static uint32_t s_can_tx_queue_high_water = 0;

#define APP_CAN_RX_QUEUE_LENGTH 32
static QueueHandle_t s_can_rx_queue = NULL;
static TaskHandle_t s_can_rx_task_handle = NULL;
static uint32_t s_can_rx_queue_drops = 0;

/**
 * [POR QUE EXISTE]
 * Valida que el bus logico pedido sea CAN1 o CAN2.
 *
 * [QUIEN LA LLAMA]
 * app_can_select_bus() y app_can_send_standard().
 *
 * [CUANDO SE EJECUTA]
 * Antes de guardar un bus o transmitir una trama.
 *
 * [ENTRADAS]
 * Recibe el entero de bus.
 *
 * [SALIDAS]
 * Devuelve true solo para 1 o 2.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No usa estado compartido mutable.
 *
 * [FLUJO ACURATEX]
 * can1/can2/send -> validar bus -> continuar o error.
 *
 * [EQUIVALENCIA MCU]
 * Es validar un selector antes de usar un periferico.
 *
 * [SI NO EXISTIERA]
 * Un valor fuera de rango podria llegar a logs o callbacks CAN.
 */
static bool app_can_is_valid_bus(int bus)
{
    return bus == 1 || bus == 2;
}

/**
 * [POR QUE EXISTE]
 * Formatea los bytes CAN como texto hexadecimal para logs.
 *
 * [QUIEN LA LLAMA]
 * app_can_send_standard() antes de transmitir.
 *
 * [CUANDO SE EJECUTA]
 * En cada envio CAN aceptado por validacion.
 *
 * [ENTRADAS]
 * Puntero a datos, DLC/len, buffer de salida y tamano.
 *
 * [SALIDAS]
 * No devuelve valor; escribe una cadena tipo `01 02 FF`.
 *
 * [ESTADO QUE MODIFICA]
 * Solo modifica el buffer `out`.
 *
 * [CONCURRENCIA]
 * Usa variables locales; no bloquea.
 *
 * [FLUJO ACURATEX]
 * send -> data[] -> log CAN_TX_BEGIN con DATA=...
 *
 * [EQUIVALENCIA MCU]
 * Es imprimir el contenido del mailbox de datos antes de transmitir.
 *
 * [SI NO EXISTIERA]
 * Los logs indicarian ID/DLC pero no bytes enviados.
 */
static void app_can_format_data_hex(const uint8_t *data, size_t len, char *out, size_t out_len)
{
    size_t offset = 0;

    if (out == NULL || out_len == 0) {
        return;
    }

    out[0] = '\0';
    if (data == NULL || len == 0) {
        return;
    }

    for (size_t i = 0; i < len && offset + 3 < out_len; ++i) {
        // [C/C++] `%02X` fuerza dos digitos por byte; el primer byte no lleva
        // espacio inicial y los siguientes se separan con un espacio.
        int written = snprintf(out + offset, out_len - offset, i == 0 ? "%02X" : " %02X", data[i]);
        if (written < 0 || (size_t)written >= out_len - offset) {
            break;
        }
        offset += (size_t)written;
    }
}

/**
 * [POR QUE EXISTE]
 * Pone el transceiver CAN en standby cuando no se pudo iniciar TWAI.
 *
 * [QUIEN LA LLAMA]
 * app_can_init() en rutas de error de install/start.
 *
 * [CUANDO SE EJECUTA]
 * Si twai_driver_install() o twai_start() fallan.
 *
 * [ENTRADAS]
 * No recibe parametros; usa CAN_STBY_GPIO.
 *
 * [SALIDAS]
 * No devuelve valor; registra warning si GPIO falla.
 *
 * [ESTADO QUE MODIFICA]
 * Sube GPIO6 a nivel alto.
 *
 * [CONCURRENCIA]
 * Corre durante inicializacion/error, sin mutex propio.
 *
 * [FLUJO ACURATEX]
 * Falla TWAI -> STBY alto -> transceiver fuera de operacion.
 *
 * [EQUIVALENCIA MCU]
 * Es deshabilitar el transceiver externo con un pin de control.
 *
 * [SI NO EXISTIERA]
 * El transceiver podria quedar habilitado aunque el driver interno no funcione.
 */
static void app_can_enter_standby(void)
{
    // [ACURATEX] En esta placa STBY alto deja el transceiver en espera.
    esp_err_t err = gpio_set_level(CAN_STBY_GPIO, 1);
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "No se pudo poner CAN STBY en alto: %s", esp_err_to_name(err));
    }
}

static void app_can_tx_update_high_water(void)
{
    if (s_can_tx_queue == NULL)
    {
        return;
    }

    UBaseType_t waiting = uxQueueMessagesWaiting(s_can_tx_queue);
    if ((uint32_t)waiting > s_can_tx_queue_high_water)
    {
        s_can_tx_queue_high_water = (uint32_t)waiting;
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion contiene la escritura real al driver TWAI.
 *
 * [QUIEN LA LLAMA]
 * Solo la llama app_can_tx_task(), para que una unica tarea toque
 * twai_transmit().
 *
 * [CUANDO SE EJECUTA]
 * Cuando la cola app_can_tx_queue entrega una solicitud CAN copiada.
 *
 * [ENTRADAS]
 * Recibe una solicitud con bus logico, ID estandar, DLC y datos ya copiados.
 *
 * [SALIDAS]
 * Devuelve ESP_OK o el esp_err_t de twai_transmit().
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado propio; entrega la trama a la cola interna TWAI.
 *
 * [CONCURRENCIA]
 * Corre solo en can_tx. Puede bloquear hasta 50 ms dentro de twai_transmit().
 *
 * [FLUJO ACURATEX]
 * solicitud CAN -> can_tx -> twai_transmit -> bus fisico.
 *
 * [EQUIVALENCIA MCU]
 * Es el unico punto que carga el mailbox TX del periferico CAN.
 *
 * [SI NO EXISTIERA]
 * La cola CAN no tendria consumidor que lleve solicitudes a TWAI.
 */
static esp_err_t app_can_transmit_now(const app_can_tx_request_t *request)
{
    if (request == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    // [ESP-IDF] twai_message_t es la estructura que el driver TWAI consume.
    // Se inicializa en cero para que flags no usados queden apagados.
    twai_message_t message = {};
    // [ESP-IDF] identifier contiene el ID CAN. Como extd=0, se interpreta como
    // identificador estandar de 11 bits.
    message.identifier = request->id;
    // [ESP-IDF] extd=0 selecciona trama estandar; rtr=0 selecciona trama de
    // datos, no remote frame.
    message.extd = 0;
    message.rtr = 0;
    // [ESP-IDF] data_length_code es el DLC que TWAI enviara en la trama.
    message.data_length_code = request->length;
    if (request->length > 0)
    {
        // [C/C++] Copia exactamente `length` bytes al buffer fijo de 8 bytes.
        memcpy(message.data, request->data, request->length);
    }

    // [ACURATEX] Timeout corto para no retener el worker; el comando ya fue
    // aceptado en cola y no debe bloquear la recepcion de nuevas ordenes.
    esp_err_t err = twai_transmit(&message, pdMS_TO_TICKS(5));
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "CAN TX ERROR id=0x%03" PRIX32 " err=%s",
                 request->id,
                 esp_err_to_name(err));
        ESP_LOGI(TAG, "CAN_TX_RESULT|RESULT=%s|ID=0x%03" PRIX32,
                 esp_err_to_name(err),
                 request->id);
        return err;
    }

    ESP_LOGI(TAG, "CAN TX OK id=0x%03" PRIX32 " dlc=%u",
             request->id,
             static_cast<unsigned>(request->length));
    ESP_LOGI(TAG, "CAN_TX_RESULT|RESULT=OK|ID=0x%03" PRIX32 "|DLC=%u",
             request->id,
             static_cast<unsigned>(request->length));
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Serializa todas las transmisiones CAN de la aplicacion en una sola tarea.
 *
 * [CORE]
 * Core 1, junto al control fisico de Cabezal.
 *
 * [PRIORIDAD]
 * 8, por encima del scheduler J prioridad 7 para vaciar CAN con rapidez.
 *
 * [STACK]
 * 4096, suficiente para una solicitud, twai_message_t y logs.
 *
 * [QUIEN LA DESPIERTA]
 * FreeRTOS la despierta cuando app_can_send_standard() encola una solicitud.
 *
 * [QUE COLA CONSUME]
 * s_can_tx_queue.
 *
 * [QUE COLA PRODUCE]
 * No produce colas. Devuelve resultado al solicitante con task notification.
 *
 * [QUE ESTADO MODIFICA]
 * Actualiza diagnosticos de cola; el driver TWAI modifica su estado interno.
 *
 * [QUE MUTEX UTILIZA]
 * No usa mutex propio porque es la unica tarea que llama twai_transmit().
 *
 * [QUE PASA SI SE BLOQUEA]
 * Si se bloquea, los solicitantes esperan resultado CAN; comunicaciones Core 0
 * no escriben TWAI directamente.
 */
static void app_can_tx_task(void *arg)
{
    (void)arg;

    ESP_LOGI(TAG,
             "[RTOS] can_tx core=%d priority=%u stack=4096 queue=%d",
             xPortGetCoreID(),
             8U,
             APP_CAN_TX_QUEUE_LENGTH);

    while (true)
    {
        app_can_tx_request_t request = {};
        if (xQueueReceive(s_can_tx_queue, &request, portMAX_DELAY) != pdTRUE)
        {
            continue;
        }

        app_can_tx_update_high_water();
        (void)app_can_transmit_now(&request);
    }
}

static bool app_can_rx_route_to_active_transport(app_reply_route_t *route)
{
    if (route == NULL)
    {
        return false;
    }

    if (acuratex_usb_bridge_is_mounted())
    {
        uint32_t session_id = app_usb_get_active_session_id();
        if (session_id == 0U)
        {
            return false;
        }

        route->transport = APP_TRANSPORT_USB;
        route->session_id = session_id;
        return true;
    }

    if (app_tcp_server_is_running())
    {
        uint32_t session_id = app_tcp_server_get_active_session_id();
        if (session_id == 0U)
        {
            return false;
        }

        route->transport = APP_TRANSPORT_TCP;
        route->session_id = session_id;
        return true;
    }

    return false;
}

static void app_can_format_rx_line(const app_can_rx_event_t *event, char *out, size_t out_len)
{
    char data_hex[32];
    const char *bus_name = "CAN1";

    if (out == NULL || out_len == 0)
    {
        return;
    }

    out[0] = '\0';
    if (event == NULL)
    {
        return;
    }

    bus_name = (event->bus == 2) ? "CAN2" : "CAN1";
    app_can_format_data_hex(event->data, event->dlc, data_hex, sizeof(data_hex));
    snprintf(out,
             out_len,
             "CAN_RX|BUS=%s|ID=0x%03" PRIX32 "|DLC=%u|DATA=%s|TIME=%u",
             bus_name,
             event->id,
             (unsigned)event->dlc,
             event->dlc > 0 ? data_hex : "",
             (unsigned)event->time_ms);
}

static esp_err_t app_can_publish_rx_event(const app_can_rx_event_t *event)
{
    app_reply_route_t route = {};
    char line[160];
    char data_hex[32];

    if (event == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    if (!app_can_rx_route_to_active_transport(&route))
    {
        s_can_rx_queue_drops++;
        ESP_LOGW(TAG, "CAN_RX_EVENT_DROP|COUNT=%u|REASON=NO_ROUTE", (unsigned)s_can_rx_queue_drops);
        return ESP_ERR_INVALID_STATE;
    }

    app_can_format_data_hex(event->data, event->dlc, data_hex, sizeof(data_hex));
    ESP_LOGI(TAG,
             "CAN_RX_EVENT|BUS=%s|ID=0x%03" PRIX32 "|DLC=%u|DATA=%s",
             (event->bus == 2) ? "CAN2" : "CAN1",
             event->id,
             (unsigned)event->dlc,
             event->dlc > 0 ? data_hex : "");

    app_can_format_rx_line(event, line, sizeof(line));
    esp_err_t err = app_reply_dispatcher_enqueue(route.transport, route.session_id, line, 0);
    if (err != ESP_OK)
    {
        s_can_rx_queue_drops++;
        ESP_LOGW(TAG,
                 "CAN_RX_EVENT_DROP|COUNT=%u|REASON=%s",
                 (unsigned)s_can_rx_queue_drops,
                 esp_err_to_name(err));
        return err;
    }

    ESP_LOGI(TAG, "CAN_RX_EVENT_PUBLISHED|TRANSPORT=%d|SESSION=%u", (int)route.transport, (unsigned)route.session_id);
    return ESP_OK;
}

static void app_can_rx_task(void *arg)
{
    (void)arg;

    ESP_LOGI(TAG,
             "[RTOS] can_rx core=%d priority=%u stack=4096 queue=%d",
             xPortGetCoreID(),
             7U,
             APP_CAN_RX_QUEUE_LENGTH);

    while (true)
    {
        twai_message_t rx = {};
        esp_err_t err = twai_receive(&rx, portMAX_DELAY);
        if (err != ESP_OK)
        {
            if (err != ESP_ERR_TIMEOUT)
            {
                ESP_LOGW(TAG, "CAN_RX_ERROR|ERR=%s", esp_err_to_name(err));
            }
            vTaskDelay(pdMS_TO_TICKS(10));
            continue;
        }

        if (rx.extd || rx.rtr)
        {
            continue;
        }

        app_can_rx_event_t event = {};
        event.bus = app_can_get_selected_bus();
        event.id = rx.identifier;
        event.dlc = rx.data_length_code;
        if (event.dlc > 0)
        {
            memcpy(event.data, rx.data, event.dlc);
        }
        event.time_ms = (uint32_t)((uint64_t)esp_timer_get_time() / 1000ULL);

        if (s_can_rx_queue != NULL && xQueueSend(s_can_rx_queue, &event, 0) != pdTRUE)
        {
            s_can_rx_queue_drops++;
            ESP_LOGW(TAG, "CAN_RX_EVENT_DROP|COUNT=%u|REASON=QUEUE_FULL", (unsigned)s_can_rx_queue_drops);
        }

        (void)app_can_publish_rx_event(&event);
    }
}

static esp_err_t app_can_rx_worker_start(void)
{
    if (s_can_rx_queue == NULL)
    {
        s_can_rx_queue = xQueueCreate(APP_CAN_RX_QUEUE_LENGTH, sizeof(app_can_rx_event_t));
        if (s_can_rx_queue == NULL)
        {
            ESP_LOGE(TAG, "No se pudo crear cola CAN RX");
            return ESP_ERR_NO_MEM;
        }
    }

    if (s_can_rx_task_handle != NULL)
    {
        return ESP_OK;
    }

    BaseType_t ok = xTaskCreatePinnedToCore(app_can_rx_task,
                                            "can_rx",
                                            4096,
                                            NULL,
                                            7,
                                            &s_can_rx_task_handle,
                                            1);
    if (ok != pdPASS)
    {
        s_can_rx_task_handle = NULL;
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG,
             "[RTOS] can_rx_queue length=%d item=%u bytes",
             APP_CAN_RX_QUEUE_LENGTH,
             (unsigned)sizeof(app_can_rx_event_t));
    return ESP_OK;
}

static esp_err_t app_can_tx_worker_start(void)
{
    if (s_can_tx_queue == NULL)
    {
        s_can_tx_queue = xQueueCreate(APP_CAN_TX_QUEUE_LENGTH,
                                      sizeof(app_can_tx_request_t));
        if (s_can_tx_queue == NULL)
        {
            ESP_LOGE(TAG, "No se pudo crear cola CAN TX");
            return ESP_ERR_NO_MEM;
        }
    }

    if (s_can_tx_task_handle != NULL)
    {
        return ESP_OK;
    }

    BaseType_t ok = xTaskCreatePinnedToCore(app_can_tx_task,
                                            "can_tx",
                                            4096,
                                            NULL,
                                            8,
                                            &s_can_tx_task_handle,
                                            1);
    if (ok != pdPASS)
    {
        s_can_tx_task_handle = NULL;
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG,
             "[RTOS] can_tx_queue length=%d item=%u bytes",
             APP_CAN_TX_QUEUE_LENGTH,
             (unsigned)sizeof(app_can_tx_request_t));
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Configura pines, transceiver, bitrate, colas, filtro y arranque TWAI.
 *
 * [QUIEN LA LLAMA]
 * app_main() al inicio del firmware.
 *
 * [CUANDO SE EJECUTA]
 * Una vez durante el arranque; si ya estaba iniciado devuelve ESP_OK.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * ESP_OK si el driver queda corriendo; error de GPIO, install o start si falla.
 *
 * [ESTADO QUE MODIFICA]
 * s_driver_installed, s_driver_started y s_selected_bus.
 *
 * [CONCURRENCIA]
 * Corre antes del uso normal concurrente. No crea tarea propia.
 *
 * [FLUJO ACURATEX]
 * app_main -> app_can_init -> TWAI 1 Mbps listo -> comandos CAN.
 *
 * [EQUIVALENCIA MCU]
 * Es inicializar el periferico CAN: pines, bitrate, filtros y habilitacion.
 *
 * [SI NO EXISTIERA]
 * `send`, TXT y RUN no tendrian salida CAN fisica.
 */
esp_err_t app_can_init(void)
{
    esp_err_t install_err = ESP_OK;
    esp_err_t start_err = ESP_OK;
    int stby_level = -1;

    if (s_driver_started) {
        // [ACURATEX] Inicializacion idempotente: repetir no reinstala TWAI.
        return ESP_OK;
    }

    // [ESP-IDF] GPIO6 controla STBY del transceiver, por eso debe ser salida.
    esp_err_t err = gpio_set_direction(CAN_STBY_GPIO, GPIO_MODE_OUTPUT);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "CAN/TWAI STBY GPIO ERROR: %s", esp_err_to_name(err));
        return err;
    }

    // [ACURATEX] STBY bajo habilita el transceiver para operar en el bus CAN.
    err = gpio_set_level(CAN_STBY_GPIO, 0);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "CAN/TWAI STBY LOW ERROR: %s", esp_err_to_name(err));
        return err;
    }
    stby_level = gpio_get_level(CAN_STBY_GPIO);

    // [ESP-IDF] TWAI_GENERAL_CONFIG_DEFAULT carga una configuracion base en modo
    // normal, usando GPIO4 como TX y GPIO5 como RX.
    twai_general_config_t general =
        TWAI_GENERAL_CONFIG_DEFAULT(CAN_TX_GPIO, CAN_RX_GPIO, TWAI_MODE_NORMAL);
    // [ACURATEX] Longitudes de colas TWAI conservadas: TX=20, RX=50.
    general.tx_queue_len = 20;
    general.rx_queue_len = 50;

    // [ESP-IDF] Bitrate CAN configurado a 1 Mbit/s. No se modifica en esta fase.
    twai_timing_config_t timing = TWAI_TIMING_CONFIG_1MBITS();
    // [ESP-IDF] Filtro accept-all: el controlador acepta todas las tramas
    // recibidas, aunque este firmware se enfoca en transmitir.
    twai_filter_config_t filter = TWAI_FILTER_CONFIG_ACCEPT_ALL();

    // [ESP-IDF] Instala el driver TWAI con configuracion general/timing/filtro.
    err = twai_driver_install(&general, &timing, &filter);
    if (err != ESP_OK) {
        install_err = err;
        ESP_LOGE(TAG, "CAN/TWAI DRIVER INSTALL ERROR: %s", esp_err_to_name(err));
        app_can_enter_standby();
        ESP_LOGI(TAG, "CAN_INIT_RESULT|INSTALL=%s|START=%s|STBY=%d",
                 esp_err_to_name(install_err),
                 esp_err_to_name(start_err),
                 stby_level);
        return err;
    }
    s_driver_installed = true;

    // [ESP-IDF] twai_start() pasa el controlador a estado operativo.
    err = twai_start();
    if (err != ESP_OK) {
        start_err = err;
        ESP_LOGE(TAG, "CAN/TWAI START ERROR: %s", esp_err_to_name(err));
        // [ESP-IDF] Si start falla, se desinstala para no dejar un driver a
        // medias marcado como disponible.
        twai_driver_uninstall();
        s_driver_installed = false;
        app_can_enter_standby();
        ESP_LOGI(TAG, "CAN_INIT_RESULT|INSTALL=%s|START=%s|STBY=%d",
                 esp_err_to_name(install_err),
                 esp_err_to_name(start_err),
                 stby_level);
        return err;
    }

    s_driver_started = true;
    // [ACURATEX] Bus logico por defecto al arrancar.
    s_selected_bus = 1;

    err = app_can_tx_worker_start();
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "CAN/TWAI TX TASK ERROR: %s", esp_err_to_name(err));
        twai_stop();
        twai_driver_uninstall();
        s_driver_started = false;
        s_driver_installed = false;
        app_can_enter_standby();
        return err;
    }

    err = app_can_rx_worker_start();
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "CAN/TWAI RX TASK ERROR: %s", esp_err_to_name(err));
        twai_stop();
        twai_driver_uninstall();
        s_driver_started = false;
        s_driver_installed = false;
        s_can_tx_task_handle = NULL;
        app_can_enter_standby();
        return err;
    }

    ESP_LOGI(TAG, "CAN/TWAI INIT OK 1Mbps TX=4 RX=5 STBY=6");
    ESP_LOGI(TAG, "CAN_INIT_RESULT|INSTALL=%s|START=%s|STBY=%d",
             esp_err_to_name(install_err),
             esp_err_to_name(start_err),
             stby_level);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Comprueba que TWAI esta instalado, iniciado y en estado RUNNING.
 *
 * [QUIEN LA LLAMA]
 * app_can_select_bus(), app_can_send_standard() y app_can_get_status_name().
 *
 * [CUANDO SE EJECUTA]
 * Antes de usar el periferico o publicar estado.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * true si TWAI reporta TWAI_STATE_RUNNING.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Consulta el driver TWAI; no usa mutex local.
 *
 * [FLUJO ACURATEX]
 * send/status -> verificar driver -> OK o ERROR.
 *
 * [EQUIVALENCIA MCU]
 * Es revisar el bit de estado del periferico antes de escribir.
 *
 * [SI NO EXISTIERA]
 * El codigo podria intentar transmitir con el driver detenido.
 */
bool app_can_is_started(void)
{
    if (!s_driver_installed || !s_driver_started || s_can_tx_queue == NULL || s_can_tx_task_handle == NULL || s_can_rx_queue == NULL || s_can_rx_task_handle == NULL) {
        return false;
    }

    // [ESP-IDF] twai_status_info_t recibe contadores y estado interno TWAI.
    twai_status_info_t status = {};
    esp_err_t err = twai_get_status_info(&status);
    return err == ESP_OK && status.state == TWAI_STATE_RUNNING;
}

/**
 * [POR QUE EXISTE]
 * Cambia el bus logico activo entre CAN1 y CAN2.
 *
 * [QUIEN LA LLAMA]
 * command_processor para comandos `can1`/`can2` y el interprete TXT.
 *
 * [CUANDO SE EJECUTA]
 * Antes de futuras lineas `send`.
 *
 * [ENTRADAS]
 * Recibe 1 o 2.
 *
 * [SALIDAS]
 * ESP_OK si guarda la seleccion; error si el bus es invalido o TWAI no corre.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_selected_bus.
 *
 * [CONCURRENCIA]
 * No protege con mutex. El valor es un entero usado como seleccion logica.
 *
 * [FLUJO ACURATEX]
 * `can2` -> s_selected_bus=2 -> TX_OK bus=CAN2.
 *
 * [EQUIVALENCIA MCU]
 * Es cambiar un selector software antes de enviar.
 *
 * [SI NO EXISTIERA]
 * El protocolo no podria conservar la eleccion CAN1/CAN2.
 */
esp_err_t app_can_select_bus(int bus)
{
    if (!app_can_is_valid_bus(bus)) {
        return ESP_ERR_INVALID_ARG;
    }

    if (!app_can_is_started()) {
        return ESP_ERR_INVALID_STATE;
    }

    // [ACURATEX] Esta seleccion no instala otro controlador fisico; se conserva
    // como bus logico del protocolo sobre un unico TWAI.
    s_selected_bus = bus;
    ESP_LOGI(TAG, "CAN bus logico seleccionado: CAN%d (controlador TWAI unico)", bus);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Valida y encola una trama CAN estandar para que la transmita can_tx.
 *
 * [QUIEN LA LLAMA]
 * Comandos `send`, lineas TXT `send` y tick RUN del estado J.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que el firmware debe poner una trama en el bus CAN.
 *
 * [ENTRADAS]
 * `bus` logico 1/2, `id` estandar 0..0x7FF, `data` y `len` como DLC 0..8.
 *
 * [SALIDAS]
 * ESP_OK si can_tx/twai_transmit acepta la trama; error si parametros, estado,
 * cola o timeout TWAI fallan.
 *
 * [ESTADO QUE MODIFICA]
 * Encola una solicitud copiada y espera el resultado de la tarea CAN.
 *
 * [CONCURRENCIA]
 * Solo puede bloquear brevemente mientras la cola CAN acepta la solicitud.
 * La ejecucion fisica ocurre en la tarea can_tx y no se espera su resultado.
 *
 * [FLUJO ACURATEX]
 * App/TXT/RUN -> ID/DLC/DATA -> app_can_tx_queue -> can_tx -> twai_transmit.
 *
 * [EQUIVALENCIA MCU]
 * Es preparar una solicitud para el unico escritor del periferico CAN.
 *
 * [SI NO EXISTIERA]
 * No habria camino desde los comandos hacia el bus fisico CAN.
 */
esp_err_t app_can_send_standard(int bus, uint32_t id, const uint8_t *data, size_t len)
{
    char data_hex[48];
    app_can_tx_request_t request = {};

    // [ACURATEX] El firmware acepta solo tramas CAN estandar: ID de 11 bits
    // hasta 0x7FF y DLC clasico hasta 8 bytes.
    if (!app_can_is_valid_bus(bus) || id > 0x7FF || len > 8 || (len > 0 && data == nullptr)) {
        ESP_LOGE(TAG, "CAN TX ERROR id=0x%03" PRIX32 " err=%s",
                 id,
                 esp_err_to_name(ESP_ERR_INVALID_ARG));
        return ESP_ERR_INVALID_ARG;
    }

    if (!app_can_is_started()) {
        ESP_LOGE(TAG, "CAN TX ERROR id=0x%03" PRIX32 " err=%s",
                 id,
                 esp_err_to_name(ESP_ERR_INVALID_STATE));
        return ESP_ERR_INVALID_STATE;
    }

    if (s_can_tx_queue == NULL)
    {
        ESP_LOGE(TAG, "CAN TX ERROR id=0x%03" PRIX32 " err=%s",
                 id,
                 esp_err_to_name(ESP_ERR_INVALID_STATE));
        return ESP_ERR_INVALID_STATE;
    }

    app_can_format_data_hex(data, len, data_hex, sizeof(data_hex));
    ESP_LOGI(TAG, "CAN_TX_BEGIN|ID=0x%03" PRIX32 "|DLC=%u|DATA=%s",
             id,
             static_cast<unsigned>(len),
             len > 0 ? data_hex : "");

    request.bus = bus;
    request.id = id;
    request.length = static_cast<uint8_t>(len);
    request.enqueue_us = (uint64_t)esp_timer_get_time();
    request.request_id = ++s_can_tx_request_id;
    if (len > 0)
    {
        memcpy(request.data, data, len);
    }

    if (xQueueSend(s_can_tx_queue, &request, 0) != pdTRUE)
    {
        s_can_tx_queue_drops++;
        ESP_LOGE(TAG, "CAN TX ERROR id=0x%03" PRIX32 " err=%s",
                 id,
                 esp_err_to_name(ESP_ERR_TIMEOUT));
        ESP_LOGI(TAG,
                 "CAN_TX_QUEUE_FULL|ID=0x%03" PRIX32 "|DROPS=%u",
                 id,
                 (unsigned)s_can_tx_queue_drops);
        return ESP_ERR_TIMEOUT;
    }

    app_can_tx_update_high_water();
    if (FAST_PERF_LOG)
    {
        ESP_LOGI(TAG,
                 "[FAST] CAN id=0x%03" PRIX32 " enqueue_us=%llu",
                 id,
                 (unsigned long long)request.enqueue_us);
    }
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Expone el bus logico seleccionado al entorno de comandos.
 *
 * [QUIEN LA LLAMA]
 * app_fill_command_env().
 *
 * [CUANDO SE EJECUTA]
 * Antes de procesar una linea recibida.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve 1 o 2.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lectura simple sin mutex.
 *
 * [FLUJO ACURATEX]
 * env.active_bus -> send usa el bus seleccionado.
 *
 * [EQUIVALENCIA MCU]
 * Es consultar el selector actual.
 *
 * [SI NO EXISTIERA]
 * El command_processor no podria aplicar la seleccion previa.
 */
esp_err_t app_can_rx_receive(app_can_rx_event_t *event, TickType_t timeout)
{
    if (event == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    if (s_can_rx_queue == NULL)
    {
        return ESP_ERR_INVALID_STATE;
    }

    if (xQueueReceive(s_can_rx_queue, event, timeout) != pdTRUE)
    {
        return ESP_ERR_TIMEOUT;
    }

    return ESP_OK;
}

void app_can_rx_clear(void)
{
    if (s_can_rx_queue == NULL)
    {
        return;
    }

    app_can_rx_event_t discarded = {};
    while (xQueueReceive(s_can_rx_queue, &discarded, 0) == pdTRUE)
    {
    }
}
int app_can_get_selected_bus(void)
{
    return s_selected_bus;
}

/**
 * [POR QUE EXISTE]
 * Convierte el bus seleccionado en texto estable.
 *
 * [QUIEN LA LLAMA]
 * app_fill_command_env() y respuestas `TX_OK`.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que se arma el entorno o una respuesta.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve `CAN1` o `CAN2`.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lectura simple.
 *
 * [FLUJO ACURATEX]
 * s_selected_bus -> active_bus_name -> TX_OK bus=...
 *
 * [EQUIVALENCIA MCU]
 * Es formatear un enum para diagnostico.
 *
 * [SI NO EXISTIERA]
 * Las respuestas no indicarian el bus de forma legible.
 */
const char *app_can_get_selected_bus_name(void)
{
    return s_selected_bus == 2 ? "CAN2" : "CAN1";
}

/**
 * [POR QUE EXISTE]
 * Resume el estado del driver CAN para comandos de status.
 *
 * [QUIEN LA LLAMA]
 * app_fill_command_env().
 *
 * [CUANDO SE EJECUTA]
 * Al preparar el entorno de una linea recibida.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * `TWAI` si esta corriendo; `ERROR` en otro caso.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Llama app_can_is_started(), que consulta el driver TWAI.
 *
 * [FLUJO ACURATEX]
 * status -> CAN=TWAI/ERROR.
 *
 * [EQUIVALENCIA MCU]
 * Es reportar si el periferico CAN esta disponible.
 *
 * [SI NO EXISTIERA]
 * La app no tendria diagnostico textual de CAN.
 */
const char *app_can_get_status_name(void)
{
    return app_can_is_started() ? "TWAI" : "ERROR";
}
