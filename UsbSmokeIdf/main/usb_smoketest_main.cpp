#include <string.h>
#include <stdio.h>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "driver/uart.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "esp_system.h"

#include "sdkconfig.h"

#include "acuratex_usb_driver.h"
#include "acuratex_usb_bridge.h"
#include "command_processor.h"
#include "can_driver_twai.h"
#include "line_codec.h"
#include "file_transfer.h"
#include "command_head_program_runner.h"
#include "head_state_manager.h"
#include "wifi_manager.h"
#include "tcp_server.h"
#include "network_discovery.h"
#include "command_ingress_queue.h"
#include "app_rtos_types.h"
#include "reply_dispatcher.h"
#include "head_runtime.h"

// [ACURATEX] Este archivo es el punto de arranque del firmware.
// Conecta los modulos propios (USB, CAN/TWAI, archivos, cabezal, WiFi y TCP)
// y mantiene el bucle principal que decide si manda el control por USB o por red.
//
// [EQUIV ARDUINO] En Arduino se suele escribir setup() y loop().
// En ESP-IDF el punto de entrada de la aplicacion es app_main(); desde ahi se
// inicializan servicios y se deja corriendo un bucle o se crean tareas FreeRTOS.

// [C/C++] static limita este simbolo al archivo actual.
// [ESP-IDF] TAG se usa en ESP_LOGI/ESP_LOGW/ESP_LOGE para identificar de donde
// sale cada mensaje en el log serie.
static const char *TAG = "usb_smoketest";

// [ACURATEX] Estos tiempos gobiernan el arranque y reintento de red cuando USB
// no esta montado. Son constantes de comportamiento: no se modifican en estudio.
#define APP_WIFI_BOOT_GRACE_MS       10000
#define APP_WIFI_CONFIG_RECHECK_MS   30000
#define APP_WIFI_START_RETRY_MS      5000
#define APP_TCP_START_RETRY_MS       5000
#define APP_UART_RX_BUFFER_SIZE      512
#define APP_UART_LINE_BUFFER_SIZE    192

// [FREERTOS] SemaphoreHandle_t es un manejador opaco de FreeRTOS.
// Aqui se usa como mutex para serializar respuestas por USB y evitar que dos
// productores mezclen bytes en una misma salida hacia la aplicacion.
static SemaphoreHandle_t s_usb_reply_mutex = NULL;
static volatile uint32_t s_usb_session_id = 1;
static const uint32_t APP_UART_SESSION_ID = 1;
#if CONFIG_ESP_CONSOLE_UART
// [ESP-IDF] CONFIG_ESP_CONSOLE_UART viene de sdkconfig. Si la consola UART esta
// habilitada, el firmware compila tambien un canal de rescate por UART.
static SemaphoreHandle_t s_uart_reply_mutex = NULL;
#endif

#if CONFIG_ESP_CONSOLE_UART
// [ACURATEX] Estado minimo del canal UART de rescate. Se mantiene en variables
// static porque el acumulador de linea debe conservarse entre iteraciones del
// bucle principal.
static bool s_uart_rescue_ready = false;
static char s_uart_line[APP_UART_LINE_BUFFER_SIZE];
static size_t s_uart_line_len = 0;
#endif

static uint32_t app_usb_session_advance(void)
{
    uint32_t next_session_id = s_usb_session_id + 1U;
    if (next_session_id == 0U)
    {
        next_session_id = 1U;
    }
    s_usb_session_id = next_session_id;
    return next_session_id;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion existe para comparar tiempos de FreeRTOS aun cuando el contador
 * de ticks de TickType_t de la vuelta por overflow.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_poll_network_without_usb().
 *
 * [CUANDO SE EJECUTA]
 * Se ejecuta dentro del bucle principal de app_main cuando se decide si ya toca
 * reintentar WiFi o TCP.
 *
 * [ENTRADAS]
 * Recibe el tick actual y el tick objetivo.
 *
 * [SALIDAS]
 * Devuelve true cuando now ya alcanzo o paso a due.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado; solo calcula.
 *
 * [CONCURRENCIA]
 * No bloquea, no usa mutex y no comparte memoria.
 *
 * [FLUJO ACURATEX]
 * Bucle principal -> temporizador logico -> reintento WiFi/TCP.
 *
 * [EQUIVALENCIA MCU]
 * Puede compararse con revisar millis() contra una marca de tiempo en Arduino,
 * pero usando ticks de FreeRTOS.
 *
 * [SI NO EXISTIERA]
 * El codigo tendria comparaciones de tiempo repetidas y mas propensas a fallar
 * cuando el contador de ticks hiciera overflow.
 */
static bool app_tick_due(TickType_t now, TickType_t due)
{
    // [FREERTOS] TickType_t es el tipo de contador de ticks del scheduler.
    // Convertir la resta a int32_t permite saber si now esta "despues" de due
    // sin depender de una comparacion directa que puede romperse con overflow.
    return (int32_t)(now - due) >= 0;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion entrega el tiempo de ejecucion en milisegundos para modulos que
 * necesitan una base temporal simple, como la telemetria del cabezal.
 *
 * [QUIEN LA LLAMA]
 * Es llamada desde app_head_scheduler_task() para temporizar el scheduler J.
 *
 * [CUANDO SE EJECUTA]
 * En cada ciclo periodico del scheduler fisico de Cabezal.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve milisegundos desde el arranque del chip, truncados a uint32_t.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea; consulta el temporizador de ESP-IDF.
 *
 * [FLUJO ACURATEX]
 * head_scheduler -> app_now_ms -> HeadStateManager -> posible avance RUN J.
 *
 * [EQUIVALENCIA MCU]
 * Se parece a millis() en Arduino.
 *
 * [SI NO EXISTIERA]
 * Cada modulo tendria que convertir esp_timer_get_time() por su cuenta.
 */
static uint32_t app_now_ms(void)
{
    // [ESP-IDF] esp_timer_get_time() devuelve microsegundos como entero de
    // 64 bits. Dividir por 1000 lo convierte a milisegundos.
    return (uint32_t)(esp_timer_get_time() / 1000ULL);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion convierte el enum de reset de ESP-IDF en texto estable para el
 * log de arranque.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main inmediatamente al iniciar.
 *
 * [CUANDO SE EJECUTA]
 * Una vez por arranque o reinicio del firmware.
 *
 * [ENTRADAS]
 * Recibe un esp_reset_reason_t devuelto por esp_reset_reason().
 *
 * [SALIDAS]
 * Devuelve un literal const char* con el nombre resumido del reset.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea y no comparte datos modificables.
 *
 * [FLUJO ACURATEX]
 * Reset ESP32 -> app_main -> log LAST_RESET.
 *
 * [EQUIVALENCIA MCU]
 * Equivale a leer una bandera de causa de reset y mostrarla por puerto serie.
 *
 * [SI NO EXISTIERA]
 * El log mostraria un numero crudo y seria mas dificil distinguir power-on,
 * watchdog, panic o brownout durante soporte.
 */
static const char *app_reset_reason_name(esp_reset_reason_t reason)
{
    // [ESP-IDF] esp_reset_reason_t es un enum con causas definidas por ESP-IDF.
    // switch mantiene una traduccion explicita y evita depender de textos
    // internos de la biblioteca.
    switch (reason)
    {
    case ESP_RST_POWERON:
        return "POWERON";
    case ESP_RST_SW:
        return "SOFTWARE";
    case ESP_RST_PANIC:
        return "PANIC";
    case ESP_RST_INT_WDT:
        return "INT_WDT";
    case ESP_RST_TASK_WDT:
        return "TASK_WDT";
    case ESP_RST_WDT:
        return "WDT";
    case ESP_RST_BROWNOUT:
        return "BROWNOUT";
    default:
        return "OTHER";
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion crea los mutex usados para proteger salidas compartidas.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main despues de inicializar USB, archivos y runner de
 * cabezal.
 *
 * [CUANDO SE EJECUTA]
 * Una vez durante el arranque.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Inicializa s_usb_reply_mutex y, si aplica, s_uart_reply_mutex.
 *
 * [CONCURRENCIA]
 * Crea mutex FreeRTOS. Despues, app_reply_usb() y app_reply_uart() los toman
 * con xSemaphoreTake() antes de escribir respuestas.
 *
 * [FLUJO ACURATEX]
 * app_main -> mutex de respuestas -> comandos pueden responder sin mezclar
 * lineas.
 *
 * [EQUIVALENCIA MCU]
 * En un firmware sin RTOS seria similar a deshabilitar una seccion critica
 * alrededor de una transmision compartida, pero aqui se usa un mutex.
 *
 * [SI NO EXISTIERA]
 * Dos contextos podrian intentar responder al mismo canal y mezclar mensajes.
 */
static void app_reply_mutexes_init(void)
{
    if (s_usb_reply_mutex == NULL)
    {
        // [FREERTOS] xSemaphoreCreateMutex() reserva un mutex. El handle queda
        // en NULL si la reserva falla, por eso las funciones de respuesta
        // verifican si el mutex existe antes de tomarlo.
        s_usb_reply_mutex = xSemaphoreCreateMutex();
    }

#if CONFIG_ESP_CONSOLE_UART
    if (s_uart_reply_mutex == NULL)
    {
        // [ACURATEX] El canal UART de rescate usa un mutex separado del USB
        // porque son perifericos y rutas de salida distintas.
        s_uart_reply_mutex = xSemaphoreCreateMutex();
    }
#endif
}

/**
 * [POR QUE EXISTE]
 * Esta funcion adapta el callback generico app_reply_fn_t al transporte USB
 * vendor de Acuratex.
 *
 * [QUIEN LA LLAMA]
 * Es pasada a app_command_process_line() desde app_main cuando una linea llega
 * por USB.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que un comando necesita responder hacia la aplicacion por USB.
 *
 * [ENTRADAS]
 * Recibe la linea de respuesta sin salto final y un ctx que aqui no se usa.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si la linea se escribio por USB; devuelve errores ESP-IDF si
 * el argumento es invalido, si el mutex vence o si falla la escritura.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado funcional, pero usa el mutex USB y emite logs.
 *
 * [CONCURRENCIA]
 * Puede bloquear hasta 200 ms esperando s_usb_reply_mutex. Esto protege la
 * salida compartida entre el bucle principal y posibles callbacks/tareas que
 * tambien respondan.
 *
 * [FLUJO ACURATEX]
 * Handler de comando -> app_reply_usb -> acuratex_usb_bridge_write_line ->
 * aplicacion Windows.
 *
 * [EQUIVALENCIA MCU]
 * Es equivalente a una funcion Serial.println() protegida por mutex.
 *
 * [SI NO EXISTIERA]
 * El procesador de comandos no tendria una forma uniforme de enviar respuestas
 * por USB.
 */
static esp_err_t app_reply_usb(const char *line, void *ctx)
{
    // [C/C++] El parametro ctx forma parte de la firma generica de callback.
    // Se marca como usado con (void)ctx para evitar warning sin cambiar la API.
    (void)ctx;
    bool locked = false;
    // [FREERTOS] pdMS_TO_TICKS convierte milisegundos al numero de ticks que
    // entiende xSemaphoreTake().
    const TickType_t mutex_timeout = pdMS_TO_TICKS(200);

    if (line == NULL)
    {
        // [ESP-IDF] esp_err_t usa codigos como ESP_OK, ESP_FAIL o
        // ESP_ERR_INVALID_ARG para estandarizar errores entre modulos.
        return ESP_ERR_INVALID_ARG;
    }

    char tx[APP_REPLY_TX_BUFFER_SIZE];
    // [ACURATEX] El protocolo de transporte envia lineas terminadas en '\n'.
    // La aplicacion recibe una respuesta textual por linea.
    int len = snprintf(tx, sizeof(tx), "%s\n", line);
    if (len <= 0)
    {
        return ESP_FAIL;
    }
    if (len >= (int)sizeof(tx))
    {
        ESP_LOGW(TAG,
                 "HEAD_OUTPUT_TRUNCATED|CHANNEL=USB|REQUESTED=%d|CAPACITY=%u",
                 len,
                 (unsigned)sizeof(tx));
        return ESP_ERR_INVALID_SIZE;
    }

    if (s_usb_reply_mutex != NULL && xSemaphoreTake(s_usb_reply_mutex, mutex_timeout) == pdTRUE)
    {
        // [FREERTOS] xSemaphoreTake(pdTRUE) significa que este contexto posee el
        // mutex y debe liberarlo con xSemaphoreGive().
        locked = true;
    }
    else if (s_usb_reply_mutex != NULL)
    {
        // [ESP-IDF] ESP_LOGW registra una advertencia sin detener el firmware.
        // [ACURATEX] Se incluye la etapa del runner de cabezal para diagnosticar
        // en que parte del flujo se bloqueo la salida.
        ESP_LOGW(TAG, "HEAD_OUTPUT_MUTEX_TIMEOUT|CHANNEL=USB|STAGE=%s",
                 app_head_program_runner_last_stage());
        return ESP_ERR_TIMEOUT;
    }

    // [ACURATEX] acuratex_usb_bridge_write_line pertenece al componente externo
    // AcuratexUsb. Aqui solo se entrega el buffer ya codificado como linea.
    if (!acuratex_usb_bridge_write_line((const uint8_t *)tx, len))
    {
        if (locked)
        {
            // [FREERTOS] El mutex se libera incluso en error para no bloquear
            // futuras respuestas.
            xSemaphoreGive(s_usb_reply_mutex);
        }
        ESP_LOGW(TAG, "TX USB fallo: %s", line);
        return ESP_FAIL;
    }

    if (locked)
    {
        // [FREERTOS] Liberar el mutex permite que otro contexto envie su linea.
        xSemaphoreGive(s_usb_reply_mutex);
    }

    ESP_LOGI(TAG, "TX USB: %s", line);
    return ESP_OK;
}

#if CONFIG_ESP_CONSOLE_UART
/**
 * [POR QUE EXISTE]
 * Esta funcion adapta el callback generico app_reply_fn_t al canal UART de
 * rescate, cuando la consola UART esta habilitada en sdkconfig.
 *
 * [QUIEN LA LLAMA]
 * Es pasada a app_command_process_line() desde app_handle_uart_line().
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que un comando recibido por UART necesita una respuesta.
 *
 * [ENTRADAS]
 * Recibe una linea de respuesta sin salto final y un ctx no usado.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si escribe todos los bytes; devuelve error si UART no esta
 * lista, si el mutex vence o si la escritura es parcial.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado de protocolo; usa s_uart_reply_mutex y escribe al driver
 * UART configurado por ESP-IDF.
 *
 * [CONCURRENCIA]
 * Puede bloquear hasta 200 ms esperando el mutex UART.
 *
 * [FLUJO ACURATEX]
 * UART rescate -> command_processor -> app_reply_uart -> driver UART.
 *
 * [EQUIVALENCIA MCU]
 * Es comparable a Serial.println() en Arduino, con proteccion de mutex.
 *
 * [SI NO EXISTIERA]
 * El canal UART podria recibir comandos pero no responder de forma uniforme.
 */
static esp_err_t app_reply_uart(const char *line, void *ctx)
{
    (void)ctx;
    bool locked = false;
    const TickType_t mutex_timeout = pdMS_TO_TICKS(200);

    if (line == NULL || !s_uart_rescue_ready)
    {
        return ESP_ERR_INVALID_ARG;
    }

    char tx[APP_REPLY_TX_BUFFER_SIZE];
    // [ACURATEX] Igual que USB, la respuesta UART se normaliza como linea con
    // '\n' final para que el receptor lea mensajes completos.
    int len = snprintf(tx, sizeof(tx), "%s\n", line);
    if (len <= 0)
    {
        return ESP_FAIL;
    }
    if (len >= (int)sizeof(tx))
    {
        ESP_LOGW(TAG,
                 "HEAD_OUTPUT_TRUNCATED|CHANNEL=UART|REQUESTED=%d|CAPACITY=%u",
                 len,
                 (unsigned)sizeof(tx));
        return ESP_ERR_INVALID_SIZE;
    }

    if (s_uart_reply_mutex != NULL && xSemaphoreTake(s_uart_reply_mutex, mutex_timeout) == pdTRUE)
    {
        locked = true;
    }
    else if (s_uart_reply_mutex != NULL)
    {
        ESP_LOGW(TAG, "HEAD_OUTPUT_MUTEX_TIMEOUT|CHANNEL=UART|STAGE=%s",
                 app_head_program_runner_last_stage());
        return ESP_ERR_TIMEOUT;
    }

    // [ESP-IDF] uart_write_bytes() escribe al driver UART instalado. Devuelve
    // cuantos bytes acepto; por eso se compara contra len.
    int written = uart_write_bytes((uart_port_t)CONFIG_ESP_CONSOLE_UART_NUM, tx, len);
    if (written != len)
    {
        if (locked)
        {
            xSemaphoreGive(s_uart_reply_mutex);
        }
        ESP_LOGW(TAG, "TX UART rescate parcial: %s", line);
        return ESP_FAIL;
    }

    if (locked)
    {
        xSemaphoreGive(s_uart_reply_mutex);
    }

    ESP_LOGI(TAG, "TX UART rescate: %s", line);
    return ESP_OK;
}
#endif

static esp_err_t app_reply_writer(const app_reply_route_t *route, const char *line, void *user_ctx)
{
    (void)user_ctx;

    if (route == NULL || line == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    switch (route->transport)
    {
    case APP_TRANSPORT_USB:
        if (!acuratex_usb_bridge_is_mounted() || route->session_id != s_usb_session_id)
        {
            ESP_LOGW(TAG,
                     "USB_REPLY_STALE|SESSION=%u|ACTIVE=%u",
                     (unsigned)route->session_id,
                     (unsigned)s_usb_session_id);
            return ESP_ERR_INVALID_STATE;
        }
        return app_reply_usb(line, NULL);

    case APP_TRANSPORT_TCP:
        return app_tcp_server_send_to_session(route->session_id, line);

    case APP_TRANSPORT_UART:
#if CONFIG_ESP_CONSOLE_UART
        if (route->session_id != APP_UART_SESSION_ID)
        {
            return ESP_ERR_INVALID_STATE;
        }
        return app_reply_uart(line, NULL);
#else
        return ESP_ERR_INVALID_STATE;
#endif

    default:
        return ESP_ERR_INVALID_ARG;
    }
}

static esp_err_t app_enqueue_command_from_transport(app_transport_type_t transport,
                                                   uint32_t session_id,
                                                   const char *line)
{
    if (line == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    return app_command_ingress_enqueue(transport,
                                       session_id,
                                       line,
                                       pdMS_TO_TICKS(20));
}

static void app_fast_log_rx_line(const char *line)
{
    if (FAST_PERF_LOG && line != NULL)
    {
        ESP_LOGI(TAG, "[FAST] RX command=%s", line);
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion conserva el contrato text_input de app_command_env_t aunque el
 * flujo TXT real se maneje por modulos especificos.
 *
 * [QUIEN LA LLAMA]
 * Puede ser llamada por command_processor cuando usa env->text_input.
 *
 * [CUANDO SE EJECUTA]
 * Solo si un comando de texto cae en este callback de prueba.
 *
 * [ENTRADAS]
 * Recibe texto de entrada, callback de respuesta y contexto del transporte.
 *
 * [SALIDAS]
 * Devuelve el resultado de enviar `ACK TXT ...` por el callback recibido.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado persistente.
 *
 * [CONCURRENCIA]
 * No bloquea por si misma; puede bloquear si el callback reply toma un mutex.
 *
 * [FLUJO ACURATEX]
 * command_processor -> reply del transporte.
 *
 * [EQUIVALENCIA MCU]
 * Equivale a una funcion de eco usada para mantener una interfaz conectada.
 *
 * [SI NO EXISTIERA]
 * La estructura de entorno no podria llenar el puntero text_input con una
 * funcion valida desde app_main.
 */
/**
 * [POR QUE EXISTE]
 * Esta funcion envia el banner inicial de USB para avisar a la aplicacion que
 * el firmware esta listo por ese transporte.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main una sola vez por conexion USB montada.
 *
 * [CUANDO SE EJECUTA]
 * Cuando TinyUSB reporta el bridge montado y banner_sent aun es false.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor; internamente intenta enviar `READY`.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado propio; app_main controla la bandera banner_sent.
 *
 * [CONCURRENCIA]
 * Usa app_reply_usb(), por lo que puede tomar el mutex USB.
 *
 * [FLUJO ACURATEX]
 * USB montado -> READY -> aplicacion detecta firmware disponible.
 *
 * [EQUIVALENCIA MCU]
 * Similar a imprimir "READY" por Serial al conectar una herramienta.
 *
 * [SI NO EXISTIERA]
 * La aplicacion no tendria una senal inmediata de disponibilidad por USB.
 */
static void app_send_banner_once(void)
{
    app_reply_usb("READY", NULL);
}

#if CONFIG_ESP_CONSOLE_UART
/**
 * [POR QUE EXISTE]
 * Esta funcion envia el banner del canal UART de rescate.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main despues de app_uart_rescue_init().
 *
 * [CUANDO SE EJECUTA]
 * Una vez durante el arranque si CONFIG_ESP_CONSOLE_UART esta habilitado.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor; intenta enviar `READY UART`.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado propio.
 *
 * [CONCURRENCIA]
 * Usa app_reply_uart(), que protege la salida con mutex.
 *
 * [FLUJO ACURATEX]
 * app_main -> UART rescate listo -> READY UART.
 *
 * [EQUIVALENCIA MCU]
 * Similar a imprimir un mensaje inicial por Serial de depuracion.
 *
 * [SI NO EXISTIERA]
 * Un operador conectado por UART no tendria confirmacion textual de arranque.
 */
static void app_send_uart_banner_once(void)
{
    app_reply_uart("READY UART", NULL);
}
#endif

/**
 * [POR QUE EXISTE]
 * Esta funcion arma la estructura app_command_env_t con el estado actual de
 * USB, WiFi, TCP y CAN para que el procesador de comandos no dependa de
 * variables globales de cada modulo.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main al procesar USB, por app_handle_uart_line() al
 * procesar UART y por app_handle_tcp_line() al procesar TCP.
 *
 * [CUANDO SE EJECUTA]
 * Antes de cada llamada a app_command_process_line().
 *
 * [ENTRADAS]
 * Recibe un puntero a la estructura que debe llenar y una bandera que indica
 * si USB esta montado para el comando actual.
 *
 * [SALIDAS]
 * No devuelve valor; llena `*env` si el puntero es valido.
 *
 * [ESTADO QUE MODIFICA]
 * Modifica solo la estructura apuntada por env.
 *
 * [CONCURRENCIA]
 * No toma mutex. Lee estados de otros modulos mediante sus APIs publicas.
 *
 * [FLUJO ACURATEX]
 * Transporte -> app_fill_command_env -> command_processor -> CAN/archivo/cabezal.
 *
 * [EQUIVALENCIA MCU]
 * Es como pasar una tabla de funciones y estado a un parser en vez de usar
 * variables globales dispersas.
 *
 * [SI NO EXISTIERA]
 * El procesador de comandos tendria que conocer directamente USB, WiFi, TCP y
 * CAN, aumentando acoplamiento.
 */
static void app_fill_command_env(app_command_env_t *env, bool usb_mounted)
{
    if (env == NULL)
    {
        return;
    }

    // [C/C++] Esta asignacion usa inicializacion designada de C++ soportada por
    // el toolchain del proyecto. Cada campo queda documentado por su nombre.
    *env = {
        .usb_mounted = usb_mounted,
        .wifi_connected = app_wifi_manager_is_connected(),
        .wifi_ip = app_wifi_manager_get_ip(),
        .tcp_port = app_wifi_manager_get_port() > 0 ? app_wifi_manager_get_port() : 3333,
        .wifi_ssid = app_wifi_manager_get_ssid(),
        .active_bus = app_can_get_selected_bus(),
        .active_bus_name = app_can_get_selected_bus_name(),
        .can_status = app_can_get_status_name(),
        // [C/C++] Estos campos son punteros a funcion. El command_processor los
        // llama sin saber que implementacion concreta hay detras.
        .can_select_bus = app_can_select_bus,
        .can_send_standard = app_can_send_standard,
        .text_input = nullptr,
        .reply_ctx_clone = app_reply_dispatcher_ctx_clone,
        .reply_ctx_release = app_reply_dispatcher_ctx_release,
        // [ACURATEX] El protocolo aqui se limita a tramas CAN estandar con DLC
        // maximo de 8 bytes e identificador estandar de 11 bits.
        .can_max_frame_len = 8,
        .can_std_id_mask = 0x7FF,
    };
}

#if CONFIG_ESP_CONSOLE_UART
/**
 * [POR QUE EXISTE]
 * Esta funcion prepara el canal UART de rescate para recibir comandos aunque
 * USB o WiFi no esten disponibles.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main durante el arranque, dentro del bloque condicionado
 * por CONFIG_ESP_CONSOLE_UART.
 *
 * [CUANDO SE EJECUTA]
 * Una vez durante la inicializacion general.
 *
 * [ENTRADAS]
 * No recibe parametros; usa CONFIG_ESP_CONSOLE_UART_NUM y
 * CONFIG_ESP_CONSOLE_UART_BAUDRATE desde sdkconfig.
 *
 * [SALIDAS]
 * No devuelve valor. Si falla la instalacion del driver UART, registra el error
 * y deja s_uart_rescue_ready en false.
 *
 * [ESTADO QUE MODIFICA]
 * Inicializa s_uart_line_len, limpia s_uart_line y activa
 * s_uart_rescue_ready.
 *
 * [CONCURRENCIA]
 * Instala/configura un driver ESP-IDF. No crea tarea; la lectura se hace por
 * polling desde el bucle principal.
 *
 * [FLUJO ACURATEX]
 * app_main -> UART rescate -> comandos manuales -> command_processor.
 *
 * [EQUIVALENCIA MCU]
 * Se parece a Serial.begin(baudrate), mas la instalacion explicita del driver
 * UART que Arduino oculta.
 *
 * [SI NO EXISTIERA]
 * No habria canal de comandos de rescate por UART.
 */
static void app_uart_rescue_init(void)
{
    // [ESP-IDF] uart_port_t identifica que UART fisica/logica usa el driver.
    uart_port_t port = (uart_port_t)CONFIG_ESP_CONSOLE_UART_NUM;

    if (!uart_is_driver_installed(port))
    {
        // [ESP-IDF] uart_driver_install() reserva buffers internos y registra
        // el driver UART. Aqui solo se usa buffer RX; TX se escribe directo.
        esp_err_t err = uart_driver_install(port, APP_UART_RX_BUFFER_SIZE, 0, 0, NULL, 0);
        if (err != ESP_OK)
        {
            ESP_LOGE(TAG, "No se pudo iniciar UART rescate: %s", esp_err_to_name(err));
            return;
        }
    }

    // [ESP-IDF] La velocidad viene de sdkconfig, no de una constante local.
    uart_set_baudrate(port, CONFIG_ESP_CONSOLE_UART_BAUDRATE);
    // [ESP-IDF] Se descarta cualquier byte viejo para que el primer comando
    // procesado despues del arranque empiece limpio.
    uart_flush_input(port);

    s_uart_line_len = 0;
    s_uart_line[0] = '\0';
    s_uart_rescue_ready = true;

    ESP_LOGI(TAG,
             "UART rescate activo en UART%d a %d baudios",
             CONFIG_ESP_CONSOLE_UART_NUM,
             CONFIG_ESP_CONSOLE_UART_BAUDRATE);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion toma una linea completa recibida por UART, la normaliza y la
 * entrega al procesador central de comandos.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_poll_uart_rescue() cuando detecta fin de linea.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que llega una linea no vacia por UART de rescate.
 *
 * [ENTRADAS]
 * Recibe un puntero a un buffer modificable terminado en cero.
 *
 * [SALIDAS]
 * No devuelve valor; responde por UART mediante app_reply_uart().
 *
 * [ESTADO QUE MODIFICA]
 * Puede modificar el contenido de line al recortarlo con app_trim_line().
 *
 * [CONCURRENCIA]
 * Corre dentro del bucle principal. app_command_process_line() puede terminar
 * llamando callbacks que bloqueen brevemente con mutex.
 *
 * [FLUJO ACURATEX]
 * UART -> buffer de linea -> app_handle_uart_line -> command_processor ->
 * respuesta UART.
 *
 * [EQUIVALENCIA MCU]
 * Es similar a leer una linea de Serial, quitar espacios/saltos y ejecutar un
 * comando.
 *
 * [SI NO EXISTIERA]
 * Los bytes UART se acumularian, pero no se convertirian en comandos.
 */
static void app_handle_uart_line(char *line)
{
    if (line == NULL)
    {
        return;
    }

    // [ACURATEX] app_trim_line quita espacios y terminadores para que el parser
    // reciba exactamente el comando textual.
    app_trim_line(line);
    if (line[0] == '\0')
    {
        return;
    }

    ESP_LOGI(TAG, "RX UART rescate: %s", line);
    app_fast_log_rx_line(line);

    esp_err_t err = app_enqueue_command_from_transport(APP_TRANSPORT_UART,
                                                       APP_UART_SESSION_ID,
                                                       line);
    if (err != ESP_OK)
    {
        ESP_LOGW(TAG, "No se pudo encolar comando UART: %s", esp_err_to_name(err));
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion revisa el driver UART sin bloquear y arma lineas completas para
 * el canal de rescate.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main en cada iteracion del bucle principal cuando la
 * consola UART esta habilitada.
 *
 * [CUANDO SE EJECUTA]
 * Periodicamente, cada vuelta del loop principal de 10 ms aproximadamente.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_uart_line y s_uart_line_len mientras acumula caracteres.
 *
 * [CONCURRENCIA]
 * No bloquea porque uart_read_bytes() usa timeout 0. Corre en el mismo contexto
 * que app_main.
 *
 * [FLUJO ACURATEX]
 * Driver UART -> bytes -> s_uart_line -> app_handle_uart_line -> comandos.
 *
 * [EQUIVALENCIA MCU]
 * Se parece a revisar Serial.available() y juntar caracteres hasta '\n'.
 *
 * [SI NO EXISTIERA]
 * El firmware no leeria comandos UART despues de inicializar el driver.
 */
static void app_poll_uart_rescue(void)
{
    if (!s_uart_rescue_ready)
    {
        return;
    }

    uint8_t rx[64];
    // [ESP-IDF] timeout 0 significa polling no bloqueante: si no hay bytes,
    // app_main sigue atendiendo USB, red y estado.
    int len = uart_read_bytes((uart_port_t)CONFIG_ESP_CONSOLE_UART_NUM,
                              rx,
                              sizeof(rx),
                              0);
    if (len <= 0)
    {
        return;
    }

    for (int i = 0; i < len; i++)
    {
        char c = (char)rx[i];

        // [ACURATEX] Tanto CR como LF cierran una linea. Esto permite usar
        // terminales que envian '\r', '\n' o ambos.
        if (c == '\r' || c == '\n')
        {
            if (s_uart_line_len > 0)
            {
                // [C/C++] Los parsers esperan string C terminado en '\0'.
                s_uart_line[s_uart_line_len] = '\0';
                app_handle_uart_line(s_uart_line);
                s_uart_line_len = 0;
                s_uart_line[0] = '\0';
            }
            continue;
        }

        if (s_uart_line_len < (sizeof(s_uart_line) - 1))
        {
            // [C/C++] Se reserva un byte para el terminador '\0'.
            s_uart_line[s_uart_line_len++] = c;
        }
        else
        {
            // [ACURATEX] Si la linea excede el buffer, se descarta completa
            // para evitar procesar un comando truncado.
            s_uart_line_len = 0;
            s_uart_line[0] = '\0';
            ESP_LOGW(TAG, "Linea UART rescate descartada por longitud");
        }
    }
}
#endif

/**
 * [POR QUE EXISTE]
 * Esta funcion es el adaptador entre el servidor TCP y el procesador central de
 * comandos.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por tcp_server.cpp mediante el callback registrado en
 * app_tcp_server_start().
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que llega una linea completa desde un cliente TCP.
 *
 * [ENTRADAS]
 * Recibe la linea, un callback de respuesta TCP, el contexto de respuesta y un
 * user_ctx que este arranque no usa.
 *
 * [SALIDAS]
 * Devuelve el esp_err_t del procesamiento o de la respuesta `ERR USB_ACTIVE`.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado directamente.
 *
 * [CONCURRENCIA]
 * Corre desde la tarea del servidor TCP, no desde app_main. Por eso las
 * respuestas usan callbacks/mutex del modulo TCP.
 *
 * [FLUJO ACURATEX]
 * TCP -> app_handle_tcp_line -> command_processor -> respuesta TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es como una ISR o callback de comunicacion que delega el comando a un parser
 * comun, aunque aqui corre en una tarea FreeRTOS.
 *
 * [SI NO EXISTIERA]
 * El servidor TCP podria recibir bytes, pero no sabria ejecutar comandos
 * Acuratex.
 */
static esp_err_t app_handle_tcp_line(const char *line,
                                     uint32_t session_id,
                                     void *user_ctx)
{
    // [C/C++] user_ctx queda disponible para futuras extensiones del callback,
    // pero este firmware no necesita contexto adicional.
    (void)user_ctx;

    if (acuratex_usb_bridge_is_mounted())
    {
        // [ACURATEX] USB tiene prioridad total. Si USB esta activo, red no debe
        // procesar comandos para evitar dos maestros controlando el cabezal.
        return app_reply_dispatcher_enqueue(APP_TRANSPORT_TCP,
                                            session_id,
                                            "ERR USB_ACTIVE",
                                            pdMS_TO_TICKS(20));
    }

    app_fast_log_rx_line(line);
    return app_enqueue_command_from_transport(APP_TRANSPORT_TCP, session_id, line);
}

/**
 * [POR QUE EXISTE]
 * Esta tarea ejecuta el tick periodico del HeadStateManager fuera de app_main.
 *
 * [CORE]
 * Core 1, porque gobierna avance fisico J1..J8 y genera transmisiones CAN.
 *
 * [PRIORIDAD]
 * 7, por encima de head_control prioridad 6 para que procesar un comando no
 * retrase el scheduler periodico de RUN.
 *
 * [STACK]
 * 4096, suficiente para llamar app_head_state_manager_tick() y mantener
 * diagnosticos locales de jitter.
 *
 * [QUIEN LA DESPIERTA]
 * vTaskDelayUntil() la despierta cada 10 ms sin deriva acumulativa intencional.
 *
 * [QUE COLA CONSUME]
 * No consume colas. Lee el estado J protegido dentro de HeadStateManager.
 *
 * [QUE COLA PRODUCE]
 * No produce colas en esta etapa; usa el callback CAN existente para transmitir.
 *
 * [QUE ESTADO MODIFICA]
 * Avanza j_bit/j_next_due_ms/registros fisicos mediante HeadStateManager.
 *
 * [QUE MUTEX UTILIZA]
 * No toma mutex directamente. app_head_state_manager_tick() toma su mutex
 * interno y lo libera antes de hacer esperas largas.
 *
 * [QUE PASA SI SE BLOQUEA]
 * Si se bloquea, los RUN J pueden atrasarse. Por eso no procesa USB/TCP ni
 * archivos y solo ejecuta el tick fisico.
 */
static void app_head_scheduler_task(void *arg)
{
    (void)arg;

    const TickType_t period = pdMS_TO_TICKS(10);
    TickType_t last_wake = xTaskGetTickCount();
    uint32_t samples = 0;
    uint32_t late_ticks = 0;
    uint32_t max_lateness_ms = 0;
    uint64_t lateness_sum_ms = 0;

    ESP_LOGI(TAG,
             "[RTOS] head_scheduler core=%d priority=%u stack=4096 period_ms=10",
             xPortGetCoreID(),
             7U);

    while (true)
    {
        TickType_t expected = last_wake + period;
        vTaskDelayUntil(&last_wake, period);
        TickType_t actual = xTaskGetTickCount();
        uint32_t lateness_ms = 0;

        if ((int32_t)(actual - expected) > 0)
        {
            lateness_ms = (uint32_t)(actual - expected) * portTICK_PERIOD_MS;
            late_ticks++;
            if (lateness_ms > max_lateness_ms)
            {
                max_lateness_ms = lateness_ms;
            }
        }

        lateness_sum_ms += lateness_ms;
        samples++;

        (void)app_head_state_manager_tick(app_can_send_standard, app_now_ms());

        if (samples >= 500)
        {
            uint32_t avg_lateness_ms = (uint32_t)(lateness_sum_ms / samples);
            ESP_LOGI(TAG,
                     "[HEAD TICK] period_ms=10 max_lateness_ms=%u avg_lateness_ms=%u late_ticks=%u samples=%u",
                     (unsigned)max_lateness_ms,
                     (unsigned)avg_lateness_ms,
                     (unsigned)late_ticks,
                     (unsigned)samples);
            samples = 0;
            late_ticks = 0;
            max_lateness_ms = 0;
            lateness_sum_ms = 0;
        }
    }
}

/**
 * [POR QUE EXISTE]
 * Tarea de recepcion USB: separa comunicacion de ejecucion de comandos.
 *
 * [CORE]
 * Core 0.
 *
 * [PRIORIDAD]
 * 5, igual que TCP, para recibir lineas sin depender del loop supervisor.
 *
 * [STACK]
 * 4096, suficiente para buffers de linea y llamadas de encolado.
 *
 * [QUIEN LA DESPIERTA]
 * FreeRTOS; se ejecuta periodicamente con vTaskDelay().
 *
 * [QUE COLA CONSUME]
 * No consume colas.
 *
 * [QUE COLA PRODUCE]
 * app_command_ingress_queue.
 *
 * [QUE ESTADO MODIFICA]
 * s_usb_session_id cuando detecta nueva conexion USB.
 *
 * [QUE MUTEX UTILIZA]
 * No toma mutex directamente; app_send_banner_once toma el mutex USB.
 *
 * [QUE PASA SI SE BLOQUEA]
 * Si se bloquea, no entran comandos USB, pero el scheduler fisico no depende de
 * esta tarea.
 */
static void app_usb_rx_task(void *arg)
{
    (void)arg;
    bool last_mounted = false;
    bool banner_sent = false;
    uint8_t rx[APP_COMMAND_MAX_LINE_LENGTH];

    ESP_LOGI(TAG,
             "[RTOS] usb_rx core=%d priority=%u stack=4096",
             xPortGetCoreID(),
             5U);

    while (true)
    {
        bool mounted = acuratex_usb_bridge_is_mounted();

        if (mounted && !last_mounted)
        {
            app_usb_session_advance();
            banner_sent = false;
            ESP_LOGI(TAG,
                     "USB_RX_SESSION_START|SESSION=%u",
                     (unsigned)s_usb_session_id);
        }
        else if (!mounted && last_mounted)
        {
            app_usb_session_advance();
            banner_sent = false;
            ESP_LOGI(TAG,
                     "USB_RX_SESSION_END|SESSION=%u",
                     (unsigned)s_usb_session_id);
        }

        last_mounted = mounted;

        if (!mounted)
        {
            vTaskDelay(pdMS_TO_TICKS(20));
            continue;
        }

        if (!banner_sent)
        {
            banner_sent = true;
            app_send_banner_once();
        }

        int len = acuratex_usb_bridge_read_line(rx, sizeof(rx) - 1);
        if (len > 0)
        {
            rx[len] = '\0';
            char line[APP_COMMAND_MAX_LINE_LENGTH];
            strncpy(line, (const char *)rx, sizeof(line) - 1);
            line[sizeof(line) - 1] = '\0';
            app_trim_line(line);

            if (line[0] != '\0')
            {
                ESP_LOGI(TAG, "RX USB: %s", line);
                app_fast_log_rx_line(line);
                esp_err_t err = app_enqueue_command_from_transport(APP_TRANSPORT_USB,
                                                                   s_usb_session_id,
                                                                   line);
                if (err != ESP_OK)
                {
                    ESP_LOGW(TAG, "No se pudo encolar comando USB: %s", esp_err_to_name(err));
                }
            }
        }

        vTaskDelay(pdMS_TO_TICKS(2));
    }
}

/**
 * [POR QUE EXISTE]
 * Tarea que ejecuta el parser central para comandos ya copiados en cola.
 *
 * [CORE]
 * Core 0.
 *
 * [PRIORIDAD]
 * 4, por debajo de recepcion/respuesta; la fisica migrara a Core 1.
 *
 * [STACK]
 * 6144, porque puede ejecutar FILE_* y parseo de comandos.
 *
 * [QUIEN LA DESPIERTA]
 * La cola app_command_ingress_queue cuando USB/TCP/UART encolan lineas.
 *
 * [QUE COLA CONSUME]
 * app_command_ingress_queue.
 *
 * [QUE COLA PRODUCE]
 * app_reply_queue mediante app_reply_dispatcher_reply().
 *
 * [QUE ESTADO MODIFICA]
 * Puede modificar estado de servicios no fisicos y, hasta completar la etapa de
 * Core 1, delega segun command_processor.
 *
 * [QUE MUTEX UTILIZA]
 * No toma mutex directamente; los modulos delegados protegen sus recursos.
 *
 * [QUE PASA SI SE BLOQUEA]
 * Si se bloquea, se acumulan comandos, pero USB/TCP siguen recibiendo hasta
 * llenar la cola y el scheduler fisico se movera fuera de app_main.
 */
static void app_command_dispatch_task(void *arg)
{
    (void)arg;

    ESP_LOGI(TAG,
             "[RTOS] command_dispatch core=%d priority=%u stack=6144",
             xPortGetCoreID(),
             4U);

    while (true)
    {
        app_command_message_t message = {};
        if (!app_command_ingress_receive(&message, portMAX_DELAY))
        {
            continue;
        }

        app_reply_route_t route = {
            .transport = message.transport,
            .session_id = message.session_id,
        };
        bool usb_active = message.transport == APP_TRANSPORT_USB
            ? true
            : acuratex_usb_bridge_is_mounted();

        ESP_LOGI(TAG,
                 "COMMAND_DISPATCH|TRANSPORT=%d|SESSION=%u|LINE=%s",
                 (int)message.transport,
                 (unsigned)message.session_id,
                 message.line);

        esp_err_t err = app_head_runtime_enqueue(message.transport,
                                                 message.session_id,
                                                 message.line,
                                                 usb_active,
                                                 0);
        if (err == ESP_OK)
        {
            (void)app_reply_dispatcher_reply("QUEUED", &route);
            continue;
        }

        (void)app_reply_dispatcher_reply("ERR command queue full", &route);
        ESP_LOGW(TAG, "command_dispatch fallo: %s", esp_err_to_name(err));
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion apaga los servicios de red cuando USB esta montado, porque el
 * firmware da prioridad total al transporte USB.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main en cada vuelta donde USB aparece montado.
 *
 * [CUANDO SE EJECUTA]
 * Al detectar USB activo o mientras permanece activo.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Detiene discovery UDP, servidor TCP y WiFi si estaban activos.
 *
 * [CONCURRENCIA]
 * Puede coordinarse con tareas internas de TCP/discovery a traves de sus APIs
 * de stop; app_main espera que esos modulos cierren sus recursos.
 *
 * [FLUJO ACURATEX]
 * USB montado -> detener red -> comandos solo por USB.
 *
 * [EQUIVALENCIA MCU]
 * Equivale a elegir un unico maestro de comunicacion y apagar el otro bus para
 * evitar conflictos.
 *
 * [SI NO EXISTIERA]
 * La aplicacion podria controlar simultaneamente por USB y TCP.
 */
static void app_stop_network_for_usb(void)
{
    if (app_network_discovery_is_running())
    {
        ESP_LOGI(TAG, "USB activo: cerrando discovery UDP");
        app_network_discovery_stop();
    }

    if (app_tcp_server_is_running())
    {
        ESP_LOGI(TAG, "USB activo: cerrando TCP");
        app_tcp_server_stop();
    }

    if (app_wifi_manager_is_started())
    {
        // [ACURATEX] Al parar WiFi tambien se fuerza que, al retirar USB, el
        // firmware vuelva a evaluar wifi.txt y reintente la ruta de red.
        app_wifi_manager_stop();
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion mantiene la ruta WiFi/TCP cuando USB no esta conectado.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_main en la rama `else` cuando acuratex_usb_bridge_is_mounted()
 * devuelve false.
 *
 * [CUANDO SE EJECUTA]
 * Periodicamente durante el bucle principal sin USB.
 *
 * [ENTRADAS]
 * Recibe el tick actual, punteros a proximos tiempos de reintento y un puntero
 * a la bandera que evita repetir logs de WiFi inactivo.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Puede actualizar next_wifi_check, next_tcp_start y wifi_inactive_reported;
 * tambien puede iniciar o detener WiFi, TCP y discovery UDP.
 *
 * [CONCURRENCIA]
 * app_tcp_server_start() y app_network_discovery_start() crean tareas internas
 * FreeRTOS en sus modulos. Esta funcion solo decide cuando iniciarlas o
 * detenerlas.
 *
 * [FLUJO ACURATEX]
 * Sin USB -> leer wifi.txt -> iniciar WiFi -> iniciar TCP -> iniciar discovery
 * UDP -> recibir comandos de app por red.
 *
 * [EQUIVALENCIA MCU]
 * Es una maquina de estados de comunicacion: apagado, esperando configuracion,
 * WiFi iniciado, conectado, TCP activo y discovery activo.
 *
 * [SI NO EXISTIERA]
 * Al desconectar USB no habria reintentos de WiFi/TCP ni discovery para la app.
 */
static void app_poll_network_without_usb(TickType_t now,
                                         TickType_t *next_wifi_check,
                                         TickType_t *next_tcp_start,
                                         bool *wifi_inactive_reported)
{
    if (!app_wifi_manager_is_started())
    {
        // [FREERTOS] next_wifi_check esta expresado en ticks. app_tick_due()
        // decide si ya toca mirar wifi.txt o reintentar iniciar WiFi.
        if (next_wifi_check == NULL || !app_tick_due(now, *next_wifi_check))
        {
            return;
        }

        app_wifi_settings_t settings;
        char reason[128];

        // [ACURATEX] app_wifi_manager_load_settings() lee la configuracion
        // persistida en LittleFS como wifi.txt. Si falta o es invalida, no se
        // arranca WiFi y se registra la razon.
        if (app_wifi_manager_load_settings(&settings, reason, sizeof(reason)))
        {
            if (wifi_inactive_reported != NULL)
            {
                *wifi_inactive_reported = false;
            }

            esp_err_t err = app_wifi_manager_start(&settings);
            if (err != ESP_OK)
            {
                // [ESP-IDF] esp_err_to_name() convierte esp_err_t a texto para
                // logs de diagnostico.
                ESP_LOGW(TAG, "No se pudo iniciar WiFi: %s", esp_err_to_name(err));
            }

            // [ACURATEX] Aunque falle o tarde la conexion, se programa un
            // reintento futuro sin bloquear el bucle principal.
            *next_wifi_check = now + pdMS_TO_TICKS(APP_WIFI_START_RETRY_MS);
            if (next_tcp_start != NULL)
            {
                *next_tcp_start = now;
            }
        }
        else
        {
            if (wifi_inactive_reported == NULL || !*wifi_inactive_reported)
            {
                ESP_LOGI(TAG, "WiFi inactivo: %s", reason);
            }

            if (wifi_inactive_reported != NULL)
            {
                *wifi_inactive_reported = true;
            }

            // [ACURATEX] Sin configuracion WiFi valida, se espera mas tiempo
            // antes de volver a revisar para no llenar el log.
            *next_wifi_check = now + pdMS_TO_TICKS(APP_WIFI_CONFIG_RECHECK_MS);
        }

        return;
    }

    if (!app_wifi_manager_is_connected())
    {
        // [ACURATEX] Si WiFi esta iniciado pero sin IP/conexion, TCP y discovery
        // no pueden operar correctamente, asi que se detienen.
        if (app_network_discovery_is_running())
        {
            app_network_discovery_stop();
        }

        if (app_tcp_server_is_running())
        {
            app_tcp_server_stop();
        }
        return;
    }

    if (app_network_discovery_is_running() && !app_tcp_server_is_running())
    {
        // [ACURATEX] Discovery solo tiene sentido si ya hay servidor TCP que la
        // aplicacion pueda usar despues de descubrir el equipo.
        app_network_discovery_stop();
    }

    if (next_tcp_start != NULL &&
        !app_tcp_server_is_running() &&
        app_tick_due(now, *next_tcp_start))
    {
        int port = app_wifi_manager_get_port();
        // [FREERTOS] app_tcp_server_start() crea la tarea del servidor TCP en
        // tcp_server.cpp. El callback app_handle_tcp_line queda registrado para
        // cada linea recibida.
        esp_err_t err = app_tcp_server_start(port, app_handle_tcp_line, NULL);
        if (err != ESP_OK)
        {
            ESP_LOGW(TAG, "No se pudo iniciar TCP puerto=%d: %s",
                     port,
                     esp_err_to_name(err));
            *next_tcp_start = now + pdMS_TO_TICKS(APP_TCP_START_RETRY_MS);
        }
    }

    if (app_tcp_server_is_running() && !app_network_discovery_is_running())
    {
        // [C/C++] app_discovery_info_t guarda punteros a strings administrados
        // por wifi_manager. No copia el contenido aqui.
        app_discovery_info_t discovery_info = {
            .hostname = app_wifi_manager_get_hostname(),
            .ip = app_wifi_manager_get_ip(),
            .ssid = app_wifi_manager_get_ssid(),
            .tcp_port = app_wifi_manager_get_port(),
        };

        // [FREERTOS] app_network_discovery_start() crea la tarea UDP de
        // discovery para que la app encuentre IP y puerto TCP.
        esp_err_t err = app_network_discovery_start(&discovery_info);
        if (err != ESP_OK)
        {
            ESP_LOGW(TAG, "No se pudo iniciar discovery UDP: %s", esp_err_to_name(err));
        }
    }
}

/**
 * [POR QUE EXISTE]
 * app_main() es el punto de entrada de la aplicacion ESP-IDF. Existe para
 * ordenar el arranque de los modulos Acuratex y mantener vivo el firmware
 * atendiendo USB, UART, estado de cabezal y red.
 *
 * [QUIEN LA LLAMA]
 * No la llama codigo propio. La llama el runtime de ESP-IDF despues de que el
 * bootloader carga la aplicacion y FreeRTOS queda listo.
 *
 * [CUANDO SE EJECUTA]
 * Se ejecuta una vez por reset del ESP32-S3 y luego queda dentro de un bucle
 * infinito con vTaskDelay().
 *
 * [ENTRADAS]
 * No recibe parametros. Lee configuracion desde sdkconfig, estado USB desde el
 * bridge, wifi.txt mediante wifi_manager y estado de otros modulos por APIs.
 *
 * [SALIDAS]
 * No devuelve. En ESP-IDF app_main normalmente permanece corriendo o crea otras
 * tareas y termina; este firmware permanece en su bucle principal.
 *
 * [ESTADO QUE MODIFICA]
 * Inicializa CAN/TWAI, USB, transferencia de archivos, runner de cabezal,
 * mutexes de respuesta y UART de rescate. Durante el bucle actualiza banderas
 * locales como banner_sent, last_mounted y tiempos de reintento de red.
 *
 * [CONCURRENCIA]
 * Corre en la tarea principal creada por ESP-IDF. No crea tareas directamente
 * aqui, pero llama modulos que pueden crearlas despues: TCP, discovery UDP y
 * ejecucion de acciones de cabezal. Usa vTaskDelay() para ceder CPU al
 * scheduler FreeRTOS.
 *
 * [FLUJO ACURATEX]
 * Reset ESP32 -> bootloader -> app_main -> inicializacion de modulos -> bucle
 * operativo -> USB/TCP/UART -> command_processor -> CAN/archivos/cabezal/estado.
 *
 * [EQUIVALENCIA MCU]
 * Puede compararse con setup() seguido de un loop() manual. La diferencia es
 * que ESP-IDF ya corre sobre FreeRTOS y el bucle debe ceder tiempo con
 * vTaskDelay().
 *
 * [SI NO EXISTIERA]
 * ESP-IDF no tendria punto de entrada de aplicacion y ningun modulo Acuratex se
 * inicializaria.
 */
extern "C" void app_main(void)
{
    // [C/C++] extern "C" evita name mangling de C++ para que ESP-IDF pueda
    // encontrar exactamente el simbolo app_main esperado por el runtime.
    ESP_LOGI(TAG, "Iniciando smoke test USB custom...");
    // [ESP-IDF] esp_reset_reason() permite diagnosticar si el arranque viene
    // de energia, software, watchdog, panic o brownout.
    ESP_LOGI(TAG, "LAST_RESET=%s", app_reset_reason_name(esp_reset_reason()));

    // [ACURATEX] CAN/TWAI se intenta inicializar al principio porque muchos
    // comandos de cabezal dependen de poder transmitir tramas despues.
    esp_err_t can_err = app_can_init();
    if (can_err != ESP_OK)
    {
        // [ACURATEX] El firmware no se detiene si CAN falla: deja registro en
        // log y permite que USB/archivos/red sigan disponibles para diagnostico.
        ESP_LOGE(TAG, "CAN/TWAI no disponible: %s", esp_err_to_name(can_err));
    }

    // [ACURATEX] El driver USB custom es critico. Si falla, no hay transporte
    // principal hacia la aplicacion, por eso el arranque queda bloqueado.
    if (!acuratex_usb_driver_init())
    {
        ESP_LOGE(TAG, "Fallo al iniciar acuratex_usb_driver_init()");
        while (true)
        {
            // [FREERTOS] Aunque se entra en un error permanente, vTaskDelay()
            // cede CPU al scheduler y evita un loop ocupado.
            vTaskDelay(pdMS_TO_TICKS(1000));
        }
    }

    // [ACURATEX] Inicializa el estado rapido del cabezal antes de aceptar
    // comandos cortos RUN/STOP y el scheduler fisico J.
    app_head_state_manager_init();
    // [FREERTOS] Los mutex de respuesta se crean despues de inicializar los
    // modulos que pueden reportar estado, pero antes de procesar comandos.
    app_reply_mutexes_init();

    if (app_command_ingress_init() != ESP_OK ||
        app_reply_dispatcher_init(app_reply_writer, NULL) != ESP_OK ||
        app_reply_dispatcher_start() != ESP_OK ||
        app_head_runtime_init(app_fill_command_env) != ESP_OK ||
        app_head_runtime_start() != ESP_OK)
    {
        ESP_LOGE(TAG, "Fallo al iniciar colas/tareas RTOS");
        while (true)
        {
            vTaskDelay(pdMS_TO_TICKS(1000));
        }
    }

    TaskHandle_t usb_rx_handle = NULL;
    BaseType_t usb_rx_ok = xTaskCreatePinnedToCore(app_usb_rx_task,
                                                   "usb_rx",
                                                   4096,
                                                   NULL,
                                                   5,
                                                   &usb_rx_handle,
                                                   0);
    TaskHandle_t command_dispatch_handle = NULL;
    BaseType_t command_dispatch_ok = xTaskCreatePinnedToCore(app_command_dispatch_task,
                                                             "command_dispatch",
                                                             6144,
                                                             NULL,
                                                             4,
                                                             &command_dispatch_handle,
                                                             0);
    TaskHandle_t head_scheduler_handle = NULL;
    BaseType_t head_scheduler_ok = xTaskCreatePinnedToCore(app_head_scheduler_task,
                                                           "head_scheduler",
                                                           4096,
                                                           NULL,
                                                           7,
                                                           &head_scheduler_handle,
                                                           1);
    if (usb_rx_ok != pdPASS || command_dispatch_ok != pdPASS || head_scheduler_ok != pdPASS)
    {
        ESP_LOGE(TAG, "Fallo al crear tareas RTOS propias");
        while (true)
        {
            vTaskDelay(pdMS_TO_TICKS(1000));
        }
    }

#if CONFIG_ESP_CONSOLE_UART
    // [ACURATEX] UART de rescate es opcional por configuracion de ESP-IDF.
    app_uart_rescue_init();
    app_send_uart_banner_once();
#endif

    int heartbeat = 0;
    // [ACURATEX] last_mounted permite detectar flancos: conexion o desconexion
    // USB entre una vuelta y la siguiente.
    bool last_mounted = false;
    // [ACURATEX] Evita repetir el mismo log de WiFi inactivo en cada iteracion.
    bool wifi_inactive_reported = false;
    // [FREERTOS] xTaskGetTickCount() entrega ticks del scheduler; pdMS_TO_TICKS
    // convierte los periodos de milisegundos a esa base temporal.
    TickType_t next_wifi_check = xTaskGetTickCount() + pdMS_TO_TICKS(APP_WIFI_BOOT_GRACE_MS);
    TickType_t next_tcp_start = xTaskGetTickCount();

    // [FLUJO]
    // Reset ESP32
    // -> bootloader
    // -> app_main
    // -> inicializacion de CAN/USB/archivos/cabezal/UART
    // -> bucle principal
    // -> USB si esta montado, o WiFi/TCP/UDP si no hay USB.
    while (true)
    {
        TickType_t now = xTaskGetTickCount();
        // [ACURATEX] El bridge USB informa si la aplicacion/host tiene montada
        // la interfaz vendor. Esa bandera decide la prioridad de transporte.
        bool mounted = acuratex_usb_bridge_is_mounted();

#if CONFIG_ESP_CONSOLE_UART
        // [ACURATEX] El canal UART se atiende por polling para no crear una
        // tarea adicional solo para rescate.
        app_poll_uart_rescue();
#endif

        if (mounted)
        {
            if (!last_mounted)
            {
                ESP_LOGI(TAG, "USB montado: prioridad total para USB");
            }

            // [ACURATEX] USB gana sobre red. La red se detiene para que no haya
            // dos rutas aceptando comandos simultaneamente.
            app_stop_network_for_usb();
            wifi_inactive_reported = false;
            next_wifi_check = now + pdMS_TO_TICKS(APP_WIFI_BOOT_GRACE_MS);

        }
        else
        {
            if (last_mounted)
            {
                // [ACURATEX] Al desconectar USB, el firmware vuelve a evaluar
                // wifi.txt inmediatamente para habilitar la ruta de red.
                ESP_LOGI(TAG, "USB desconectado: se reevalua wifi.txt y se reintenta WiFi STA");
                next_wifi_check = now;
                next_tcp_start = now;
                wifi_inactive_reported = false;
            }

            // [ACURATEX] Sin USB, el bucle principal mantiene WiFi/TCP/UDP por
            // maquina de estados no bloqueante.
            app_poll_network_without_usb(now,
                                         &next_wifi_check,
                                         &next_tcp_start,
                                         &wifi_inactive_reported);
        }

        heartbeat++;
        if ((heartbeat % 500) == 0)
        {
            // [ESP-IDF] Logs periodicos permiten ver que el firmware sigue vivo
            // y resumen transporte activo, bus CAN y estado de red.
            ESP_LOGI(TAG, "heartbeat=%d mounted=%d bus=%s wifi=%d ip=%s tcp=%d",
                     heartbeat,
                     (int)mounted,
                     app_can_get_selected_bus_name(),
                     (int)app_wifi_manager_is_connected(),
                     app_wifi_manager_get_ip(),
                     app_tcp_server_is_running() ? app_wifi_manager_get_port() : 0);
        }

        last_mounted = mounted;
        // [FREERTOS] vTaskDelay(10 ms) cede CPU a otras tareas del sistema,
        // como TinyUSB, WiFi, TCP/discovery y tareas de cabezal creadas por
        // otros modulos. Sin este delay el loop ocuparia el core continuamente.
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}
