#include <errno.h>
#include <stdlib.h>
#include <string.h>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"

#include "esp_log.h"
#include "lwip/inet.h"
#include "lwip/netdb.h"
#include "lwip/sockets.h"

#include "line_codec.h"
#include "command_head_program_runner.h"
#include "app_rtos_types.h"
#include "tcp_server.h"

// [ACURATEX] Este archivo implementa el transporte TCP de la aplicacion.
// Su responsabilidad es socket/listen/accept/recv/send y separar lineas.
// La interpretacion del protocolo queda en command_processor.cpp.

// [ESP-IDF] TAG identifica logs del servidor TCP.
static const char *TAG = "tcp_server";

// [ACURATEX] Contexto minimo que acompana una respuesta TCP.
// Se pasa como void* porque app_reply_fn_t es generico para USB/TCP/UART.
typedef struct
{
    int fd;
} app_tcp_reply_ctx_t;

// [C/C++] volatile indica que s_running puede cambiarse fuera del flujo local
// de la tarea TCP, por ejemplo desde app_tcp_server_stop().
static volatile bool s_running = false;
// [FREERTOS] TaskHandle_t identifica la tarea creada con xTaskCreate().
static TaskHandle_t s_task_handle = NULL;
// [LWIP] File descriptors de socket: listener y cliente actual.
static int s_listen_fd = -1;
static int s_client_fd = -1;
static int s_port = 0;
// [C/C++] s_handler es un puntero a funcion registrado por app_main.
static app_tcp_line_handler_t s_handler = NULL;
static void *s_handler_ctx = NULL;
// [FREERTOS] Mutex que serializa send() para evitar mezclar respuestas TCP.
static SemaphoreHandle_t s_send_mutex = NULL;
// [RTOS] Generacion de sesion TCP. Las respuestas diferidas solo se envian si
// la sesion que origino el comando sigue activa.
static uint32_t s_session_counter = 0;
static uint32_t s_active_session_id = 0;

/**
 * [POR QUE EXISTE]
 * Esta funcion cierra de forma centralizada un socket TCP y marca su descriptor
 * como invalido.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_tcp_server_task() y app_tcp_server_stop().
 *
 * [CUANDO SE EJECUTA]
 * Al detener el servidor, al cerrar un cliente o al manejar errores de socket.
 *
 * [ENTRADAS]
 * Recibe un puntero al descriptor que se quiere cerrar.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Puede cerrar el socket apuntado y escribir `-1` en `*fd`.
 *
 * [CONCURRENCIA]
 * Puede ser llamada desde la tarea TCP o desde app_main al detener red. El
 * cambio a `-1` evita reutilizar descriptores cerrados.
 *
 * [FLUJO ACURATEX]
 * USB activo o error TCP -> cerrar sockets -> servidor deja de aceptar/recibir.
 *
 * [EQUIVALENCIA MCU]
 * Es como deshabilitar un canal de comunicacion y marcarlo como no disponible.
 *
 * [SI NO EXISTIERA]
 * El codigo repetiria shutdown/close y podria dejar descriptores viejos vivos.
 */
static void app_tcp_close_fd(int *fd)
{
    if (fd == NULL || *fd < 0)
    {
        return;
    }

    // [LWIP] shutdown despierta operaciones bloqueadas de recv/accept/send.
    shutdown(*fd, SHUT_RDWR);
    // [LWIP] close libera el descriptor de socket.
    close(*fd);
    *fd = -1;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion adapta app_reply_fn_t al transporte TCP.
 *
 * [QUIEN LA LLAMA]
 * La llama command_processor.cpp a traves del callback reply recibido desde
 * app_tcp_process_line(), y tambien este modulo para `READY TCP` o errores de
 * linea.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que hay que enviar una respuesta textual al cliente TCP actual.
 *
 * [ENTRADAS]
 * Recibe la linea sin salto final y un ctx que apunta a app_tcp_reply_ctx_t.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si envia toda la linea; devuelve error si los argumentos son
 * invalidos, si vence el mutex o si send() falla.
 *
 * [ESTADO QUE MODIFICA]
 * No cambia estado de protocolo; usa s_send_mutex y escribe en el socket.
 *
 * [CONCURRENCIA]
 * Puede bloquear hasta 200 ms esperando s_send_mutex. send() puede bloquear
 * segun el estado del socket.
 *
 * [FLUJO ACURATEX]
 * Handler de comando -> app_tcp_reply -> send() -> aplicacion por TCP.
 *
 * [EQUIVALENCIA MCU]
 * Equivale a Serial.println() sobre red, protegido por mutex.
 *
 * [SI NO EXISTIERA]
 * El procesador comun no podria responder por TCP usando la misma firma que USB.
 */
static esp_err_t app_tcp_reply(const char *line, void *ctx)
{
    // [C/C++] ctx se castea desde void* al tipo real usado por TCP.
    app_tcp_reply_ctx_t *reply_ctx = (app_tcp_reply_ctx_t *)ctx;
    char tx[APP_REPLY_TX_BUFFER_SIZE];
    int len;
    int sent = 0;
    bool locked = false;
    const TickType_t mutex_timeout = pdMS_TO_TICKS(200);

    if (line == NULL || reply_ctx == NULL || reply_ctx->fd < 0)
    {
        return ESP_ERR_INVALID_ARG;
    }

    // [ACURATEX] Todas las respuestas de aplicacion se transmiten como lineas
    // terminadas en '\n'.
    len = snprintf(tx, sizeof(tx), "%s\n", line);
    if (len <= 0)
    {
        return ESP_FAIL;
    }

    if (len >= (int)sizeof(tx))
    {
        ESP_LOGW(TAG,
                 "HEAD_OUTPUT_TRUNCATED|CHANNEL=TCP|REQUESTED=%d|CAPACITY=%u",
                 len,
                 (unsigned)sizeof(tx));
        return ESP_ERR_INVALID_SIZE;
    }

    if (s_send_mutex != NULL)
    {
        if (xSemaphoreTake(s_send_mutex, mutex_timeout) == pdTRUE)
        {
            // [FREERTOS] El mutex queda tomado y debe liberarse antes de salir.
            locked = true;
        }
        else
        {
            ESP_LOGW(TAG, "HEAD_OUTPUT_MUTEX_TIMEOUT|CHANNEL=TCP|STAGE=%s",
                     app_head_program_runner_last_stage());
            return ESP_ERR_TIMEOUT;
        }
    }

    while (sent < len)
    {
        // [LWIP] send() puede escribir menos bytes que los pedidos; por eso se
        // repite hasta completar la linea.
        int written = send(reply_ctx->fd, tx + sent, len - sent, 0);
        if (written < 0)
        {
            if (locked)
            {
                xSemaphoreGive(s_send_mutex);
            }
            ESP_LOGW(TAG, "TX TCP fallo errno=%d", errno);
            return ESP_FAIL;
        }
        sent += written;
    }

    if (locked)
    {
        xSemaphoreGive(s_send_mutex);
    }

    ESP_LOGI(TAG, "TX TCP: %s", line);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion crea una copia heap del contexto de respuesta TCP.
 *
 * [QUIEN LA LLAMA]
 * Esta disponible para modulos que necesiten conservar el contexto despues de
 * volver del handler inmediato.
 *
 * [CUANDO SE EJECUTA]
 * Cuando un flujo asincrono necesita responder mas tarde por el mismo socket.
 *
 * [ENTRADAS]
 * Recibe un void* que debe apuntar a app_tcp_reply_ctx_t.
 *
 * [SALIDAS]
 * Devuelve un puntero a copia o NULL si no hay contexto/memoria.
 *
 * [ESTADO QUE MODIFICA]
 * Reserva memoria con calloc().
 *
 * [CONCURRENCIA]
 * No toma mutex. La validez real del fd depende de que el cliente siga conectado.
 *
 * [FLUJO ACURATEX]
 * Respuesta diferida -> clonar ctx TCP -> responder mas tarde -> liberar ctx.
 *
 * [EQUIVALENCIA MCU]
 * Es como guardar el identificador de un canal para contestar luego.
 *
 * [SI NO EXISTIERA]
 * Los modulos asincronos no tendrian una forma generica de conservar contexto TCP.
 */
void *app_tcp_reply_ctx_clone(void *ctx)
{
    app_tcp_reply_ctx_t *source = (app_tcp_reply_ctx_t *)ctx;
    app_tcp_reply_ctx_t *copy = NULL;

    if (source == NULL)
    {
        return NULL;
    }

    copy = (app_tcp_reply_ctx_t *)calloc(1, sizeof(app_tcp_reply_ctx_t));
    if (copy == NULL)
    {
        return NULL;
    }

    copy->fd = source->fd;
    return copy;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion libera una copia de contexto creada por
 * app_tcp_reply_ctx_clone().
 *
 * [QUIEN LA LLAMA]
 * La llama quien haya clonado el contexto.
 *
 * [CUANDO SE EJECUTA]
 * Cuando ya no se necesita responder de forma diferida por TCP.
 *
 * [ENTRADAS]
 * Recibe el puntero devuelto por app_tcp_reply_ctx_clone().
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Libera memoria heap.
 *
 * [CONCURRENCIA]
 * No usa mutex. El llamador debe no usar el puntero despues de liberar.
 *
 * [FLUJO ACURATEX]
 * ctx clonado -> respuesta diferida -> app_tcp_reply_ctx_release.
 *
 * [EQUIVALENCIA MCU]
 * Equivale a liberar una estructura auxiliar reservada para una comunicacion.
 *
 * [SI NO EXISTIERA]
 * Las copias de contexto TCP quedarian filtradas en heap.
 */
void app_tcp_reply_ctx_release(void *ctx)
{
    free(ctx);
}

esp_err_t app_tcp_server_send_to_session(uint32_t session_id, const char *line)
{
    char tx[APP_REPLY_TX_BUFFER_SIZE];
    int len;
    int sent = 0;
    bool locked = false;

    if (line == NULL || session_id == 0)
    {
        return ESP_ERR_INVALID_ARG;
    }

    len = snprintf(tx, sizeof(tx), "%s\n", line);
    if (len <= 0)
    {
        return ESP_FAIL;
    }
    if (len >= (int)sizeof(tx))
    {
        ESP_LOGW(TAG,
                 "HEAD_OUTPUT_TRUNCATED|CHANNEL=TCP_SESSION|SESSION=%u|REQUESTED=%d|CAPACITY=%u",
                 (unsigned)session_id,
                 len,
                 (unsigned)sizeof(tx));
        return ESP_ERR_INVALID_SIZE;
    }

    if (s_send_mutex != NULL)
    {
        if (xSemaphoreTake(s_send_mutex, pdMS_TO_TICKS(200)) != pdTRUE)
        {
            ESP_LOGW(TAG, "TCP_SESSION_SEND_MUTEX_TIMEOUT|SESSION=%u", (unsigned)session_id);
            return ESP_ERR_TIMEOUT;
        }
        locked = true;
    }

    if (!s_running || s_client_fd < 0 || s_active_session_id != session_id)
    {
        if (locked)
        {
            xSemaphoreGive(s_send_mutex);
        }
        ESP_LOGW(TAG,
                 "TCP_SESSION_STALE|SESSION=%u|ACTIVE=%u",
                 (unsigned)session_id,
                 (unsigned)s_active_session_id);
        return ESP_ERR_INVALID_STATE;
    }

    while (sent < len)
    {
        int written = send(s_client_fd, tx + sent, len - sent, 0);
        if (written < 0)
        {
            if (locked)
            {
                xSemaphoreGive(s_send_mutex);
            }
            ESP_LOGW(TAG, "TX TCP session fallo errno=%d", errno);
            return ESP_FAIL;
        }
        sent += written;
    }

    if (locked)
    {
        xSemaphoreGive(s_send_mutex);
    }

    ESP_LOGI(TAG, "TX TCP session=%u: %s", (unsigned)session_id, line);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion recibe una linea TCP completa, la limpia y la entrega al handler
 * comun registrado por app_main.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_tcp_client_loop() cuando detecta '\n'.
 *
 * [CUANDO SE EJECUTA]
 * Por cada linea completa recibida desde el cliente TCP.
 *
 * [ENTRADAS]
 * Recibe buffer modificable de linea y fd del cliente conectado.
 *
 * [SALIDAS]
 * No devuelve valor; responde usando app_tcp_reply si hace falta.
 *
 * [ESTADO QUE MODIFICA]
 * Puede modificar `line` al recortar espacios. No cambia globals salvo por lo
 * que haga el handler.
 *
 * [CONCURRENCIA]
 * Corre dentro de la tarea TCP. El handler puede llamar modulos compartidos y
 * callbacks protegidos por sus propios mutex.
 *
 * [FLUJO ACURATEX]
 * recv -> linea -> app_tcp_process_line -> app_handle_tcp_line -> command_processor.
 *
 * [EQUIVALENCIA MCU]
 * Es el equivalente a tomar una linea de un puerto serie y pasarla al parser.
 *
 * [SI NO EXISTIERA]
 * El servidor TCP recibiria bytes, pero no ejecutaria comandos.
 */
static void app_tcp_process_line(char *line, uint32_t session_id, int client_fd)
{
    app_tcp_reply_ctx_t reply_ctx = {
        .fd = client_fd,
    };

    // [ACURATEX] La linea se limpia antes del parser para igualar el tratamiento
    // que reciben USB y UART.
    app_trim_line(line);
    if (line[0] == '\0')
    {
        return;
    }

    ESP_LOGI(TAG, "RX TCP: %s", line);

    if (s_handler == NULL)
    {
        // [ACURATEX] Sin handler registrado no existe camino hacia
        // command_processor.
        app_tcp_reply("ERR TCP_HANDLER", &reply_ctx);
        return;
    }

    // [C/C++] s_handler es un puntero a funcion. Aqui normalmente apunta a
    // app_handle_tcp_line() definido en usb_smoketest_main.cpp.
    esp_err_t err = s_handler(line, session_id, s_handler_ctx);
    if (err != ESP_OK)
    {
        ESP_LOGW(TAG, "handler TCP fallo: %s", esp_err_to_name(err));
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion maneja la sesion de un cliente TCP conectado: saluda, recibe
 * bytes, arma lineas y las procesa.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_tcp_server_task() despues de accept().
 *
 * [CUANDO SE EJECUTA]
 * Mientras hay un cliente conectado y s_running sigue en true.
 *
 * [ENTRADAS]
 * Recibe el fd del cliente aceptado.
 *
 * [SALIDAS]
 * No devuelve valor visible; sale cuando el cliente cierra, hay error o el
 * servidor se detiene.
 *
 * [ESTADO QUE MODIFICA]
 * Mantiene buffers locales rx/line, line_len y overflow.
 *
 * [CONCURRENCIA]
 * Corre dentro de la tarea TCP. recv() bloquea hasta recibir datos, error o
 * cierre del socket.
 *
 * [FLUJO ACURATEX]
 * Cliente TCP -> READY TCP -> recv bytes -> separar por '\n' -> parser comun.
 *
 * [EQUIVALENCIA MCU]
 * Es un loop de lectura de puerto serie, pero usando socket TCP.
 *
 * [SI NO EXISTIERA]
 * El servidor aceptaria conexiones, pero no leeria comandos del cliente.
 */
static void app_tcp_client_loop(int client_fd, uint32_t session_id)
{
    char rx[96];
    char line[192];
    size_t line_len = 0;
    bool overflow = false;

    app_tcp_reply_ctx_t reply_ctx = {
        .fd = client_fd,
    };

    app_tcp_reply("READY TCP", &reply_ctx);

    while (s_running)
    {
        // [LWIP] recv() entrega bytes crudos del stream TCP. TCP no conserva
        // fronteras de mensajes, por eso el firmware separa por '\n'.
        int len = recv(client_fd, rx, sizeof(rx), 0);
        if (len < 0)
        {
            ESP_LOGW(TAG, "recv TCP fallo errno=%d", errno);
            break;
        }
        if (len == 0)
        {
            ESP_LOGI(TAG, "Cliente TCP cerro conexion");
            break;
        }

        for (int i = 0; i < len; i++)
        {
            char c = rx[i];

            if (c == '\r')
            {
                // [ACURATEX] Se ignora CR para aceptar terminadores CRLF.
                continue;
            }

            if (c == '\n')
            {
                if (overflow)
                {
                    // [ACURATEX] Si la linea excedio el buffer, se descarta y
                    // se responde error al cerrar la linea.
                    app_tcp_reply("ERR line too long", &reply_ctx);
                    overflow = false;
                }
                else
                {
                    // [C/C++] El parser recibe strings C terminados en '\0'.
                    line[line_len] = '\0';
                    app_tcp_process_line(line, session_id, client_fd);
                }

                line_len = 0;
                continue;
            }

            if (line_len >= (sizeof(line) - 1))
            {
                // [ACURATEX] Se sigue leyendo hasta '\n', pero no se agregan
                // mas bytes para evitar overflow del buffer.
                overflow = true;
                continue;
            }

            if (!overflow)
            {
                line[line_len++] = c;
            }
        }
    }
}

/**
 * [POR QUE EXISTE]
 * Esta es la tarea FreeRTOS del servidor TCP. Crea el socket de escucha,
 * acepta un cliente y delega la sesion a app_tcp_client_loop().
 *
 * [QUIEN LA LLAMA]
 * No se llama directamente desde codigo de aplicacion; FreeRTOS la ejecuta
 * despues de xTaskCreate() en app_tcp_server_start().
 *
 * [CUANDO SE EJECUTA]
 * Mientras s_running sea true y el servidor no falle.
 *
 * [ENTRADAS]
 * Recibe arg de FreeRTOS, no usado en este firmware.
 *
 * [SALIDAS]
 * No devuelve al llamador; termina con vTaskDelete(NULL).
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_listen_fd, s_client_fd, s_running y s_task_handle.
 *
 * [CONCURRENCIA]
 * Corre concurrentemente con app_main. app_tcp_server_stop() puede cerrar sus
 * sockets para forzar salida de accept()/recv().
 *
 * [FLUJO ACURATEX]
 * WiFi conectado -> app_tcp_server_start -> tarea TCP -> accept -> lineas ->
 * command_processor.
 *
 * [EQUIVALENCIA MCU]
 * Es una tarea dedicada a comunicacion, similar a un loop separado para Ethernet.
 *
 * [SI NO EXISTIERA]
 * No habria servidor TCP persistente para la aplicacion por WiFi.
 */
static void app_tcp_server_task(void *arg)
{
    // [C/C++] Firma exigida por FreeRTOS: void (*)(void*). El argumento no se
    // necesita porque el estado esta en variables static del modulo.
    (void)arg;

    int listen_fd = -1;
    int opt = 1;
    // [C/C++] Inicializacion a cero de sockaddr_in antes de llenar campos.
    struct sockaddr_in server_addr = {};

    // [LWIP] socket(AF_INET, SOCK_STREAM, IPPROTO_IP) crea un socket TCP IPv4.
    listen_fd = socket(AF_INET, SOCK_STREAM, IPPROTO_IP);
    if (listen_fd < 0)
    {
        ESP_LOGE(TAG, "socket fallo errno=%d", errno);
        s_running = false;
        s_task_handle = NULL;
        vTaskDelete(NULL);
        return;
    }

    s_listen_fd = listen_fd;
    // [LWIP] SO_REUSEADDR permite reutilizar el puerto si el socket anterior
    // quedo en estado de cierre.
    setsockopt(listen_fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    server_addr.sin_family = AF_INET;
    // [LWIP] INADDR_ANY escucha en la IP local asignada por WiFi.
    server_addr.sin_addr.s_addr = htonl(INADDR_ANY);
    // [LWIP] htons convierte puerto de endian de CPU a endian de red.
    server_addr.sin_port = htons((uint16_t)s_port);

    // [LWIP] bind asocia el socket al puerto configurado.
    if (bind(listen_fd, (struct sockaddr *)&server_addr, sizeof(server_addr)) != 0)
    {
        ESP_LOGE(TAG, "bind TCP puerto=%d fallo errno=%d", s_port, errno);
        app_tcp_close_fd(&s_listen_fd);
        s_running = false;
        s_task_handle = NULL;
        vTaskDelete(NULL);
        return;
    }

    // [LWIP] listen pone el socket en modo servidor. Backlog 1: un cliente en
    // espera como maximo.
    if (listen(listen_fd, 1) != 0)
    {
        ESP_LOGE(TAG, "listen TCP fallo errno=%d", errno);
        app_tcp_close_fd(&s_listen_fd);
        s_running = false;
        s_task_handle = NULL;
        vTaskDelete(NULL);
        return;
    }

    ESP_LOGI(TAG, "Servidor TCP escuchando en puerto %d", s_port);

    while (s_running)
    {
        struct sockaddr_in client_addr = {};
        socklen_t addr_len = sizeof(client_addr);
        // [LWIP] accept bloquea hasta que un cliente se conecta o el socket se
        // cierra desde app_tcp_server_stop().
        int client_fd = accept(listen_fd, (struct sockaddr *)&client_addr, &addr_len);

        if (client_fd < 0)
        {
            if (s_running)
            {
                ESP_LOGW(TAG, "accept TCP fallo errno=%d", errno);
            }
            break;
        }

        s_client_fd = client_fd;
        s_active_session_id = ++s_session_counter;
        if (s_active_session_id == 0)
        {
            s_active_session_id = ++s_session_counter;
        }
        // [LWIP] inet_ntoa convierte IP binaria del cliente a texto para log.
        ESP_LOGI(TAG,
                 "Cliente TCP conectado desde %s session=%u",
                 inet_ntoa(client_addr.sin_addr),
                 (unsigned)s_active_session_id);

        app_tcp_client_loop(client_fd, s_active_session_id);

        app_tcp_close_fd(&s_client_fd);
        s_active_session_id = 0;
        ESP_LOGI(TAG, "Cliente TCP desconectado");
    }

    app_tcp_close_fd(&s_client_fd);
    app_tcp_close_fd(&s_listen_fd);

    ESP_LOGI(TAG, "Servidor TCP detenido");
    s_running = false;
    s_task_handle = NULL;
    // [FREERTOS] La tarea se elimina a si misma. NULL significa tarea actual.
    vTaskDelete(NULL);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion inicia el servidor TCP y registra que funcion procesara cada
 * linea recibida.
 *
 * [QUIEN LA LLAMA]
 * La llama app_poll_network_without_usb() desde app_main cuando WiFi esta
 * conectado y USB no esta activo.
 *
 * [CUANDO SE EJECUTA]
 * Al habilitar la ruta de comunicacion por red.
 *
 * [ENTRADAS]
 * Recibe puerto TCP, callback de linea y contexto opcional de usuario.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si el servidor ya estaba correcto o si la tarea se creo; error
 * si los argumentos son invalidos o falta memoria.
 *
 * [ESTADO QUE MODIFICA]
 * Inicializa s_send_mutex, s_port, s_handler, s_handler_ctx, s_running y
 * s_task_handle.
 *
 * [CONCURRENCIA]
 * Crea una tarea FreeRTOS con stack 6144 y prioridad 5. No se cambian esos
 * parametros en este estudio.
 *
 * [FLUJO ACURATEX]
 * WiFi conectado -> app_tcp_server_start -> xTaskCreate -> servidor TCP listo.
 *
 * [EQUIVALENCIA MCU]
 * Es como arrancar una tarea dedicada para atender un puerto de comunicaciones.
 *
 * [SI NO EXISTIERA]
 * app_main no podria abrir el canal TCP aunque WiFi estuviera conectado.
 */
esp_err_t app_tcp_server_start(int port, app_tcp_line_handler_t handler, void *user_ctx)
{
    if (port <= 0 || port > 65535 || handler == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    if (s_send_mutex == NULL)
    {
        // [FREERTOS] El mutex de envio se crea una sola vez y protege send().
        s_send_mutex = xSemaphoreCreateMutex();
        if (s_send_mutex == NULL)
        {
            ESP_LOGE(TAG, "No se pudo crear mutex de envio TCP");
            return ESP_ERR_NO_MEM;
        }
    }

    if (s_task_handle != NULL)
    {
        if (s_running && s_port == port && s_handler == handler)
        {
            // [ACURATEX] Si ya corre con el mismo puerto/handler, iniciar de
            // nuevo es idempotente.
            return ESP_OK;
        }

        app_tcp_server_stop();
    }

    s_port = port;
    s_handler = handler;
    s_handler_ctx = user_ctx;
    s_running = true;

    // [FREERTOS] xTaskCreate recibe funcion de tarea, nombre, stack en bytes o
    // palabras segun puerto FreeRTOS/IDF, parametro, prioridad y handle de salida.
    BaseType_t ok = xTaskCreatePinnedToCore(app_tcp_server_task,
                                            "app_tcp_server",
                                            6144,
                                            NULL,
                                            5,
                                            &s_task_handle,
                                            0);
    if (ok != pdPASS)
    {
        s_running = false;
        s_task_handle = NULL;
        ESP_LOGE(TAG, "No se pudo crear tarea TCP");
        return ESP_ERR_NO_MEM;
    }

    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion detiene la ruta TCP cuando USB toma prioridad o cuando se debe
 * reiniciar el servidor.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_stop_network_for_usb(), app_poll_network_without_usb() y
 * app_tcp_server_start() si debe cambiar configuracion.
 *
 * [CUANDO SE EJECUTA]
 * Al montar USB, perder WiFi o cambiar parametros de servidor.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Pone s_running en false y cierra cliente/listener para desbloquear la tarea.
 *
 * [CONCURRENCIA]
 * Espera hasta 100 ciclos de 10 ms a que s_task_handle quede NULL. Esto cede CPU
 * con vTaskDelay() mientras la tarea TCP sale.
 *
 * [FLUJO ACURATEX]
 * USB activo o WiFi caido -> app_tcp_server_stop -> cerrar sockets -> tarea sale.
 *
 * [EQUIVALENCIA MCU]
 * Es una parada ordenada de una tarea de comunicacion.
 *
 * [SI NO EXISTIERA]
 * El servidor podria quedar aceptando comandos por red cuando no corresponde.
 */
void app_tcp_server_stop(void)
{
    TaskHandle_t task = s_task_handle;

    if (task == NULL && !s_running)
    {
        return;
    }

    ESP_LOGI(TAG, "Deteniendo servidor TCP");
    s_running = false;
    app_tcp_close_fd(&s_client_fd);
    app_tcp_close_fd(&s_listen_fd);

    for (int i = 0; i < 100 && s_task_handle != NULL; i++)
    {
        // [FREERTOS] Espera cooperativa: permite que la tarea TCP procese el
        // cierre de sockets y se borre a si misma.
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion informa al arranque si el servidor TCP esta actualmente activo.
 *
 * [QUIEN LA LLAMA]
 * La llama app_main/app_poll_network_without_usb() para decidir si iniciar,
 * detener o arrancar discovery UDP.
 *
 * [CUANDO SE EJECUTA]
 * En el bucle principal de firmware.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve true si s_running esta activo y existe handle de tarea.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee variables compartidas escritas por app_tcp_server_task() y stop().
 *
 * [FLUJO ACURATEX]
 * app_main -> consultar TCP -> decidir discovery/reintentos.
 *
 * [EQUIVALENCIA MCU]
 * Es una bandera de estado de un periférico/logica de comunicacion.
 *
 * [SI NO EXISTIERA]
 * app_main no podria saber si debe iniciar o detener servicios de red.
 */
bool app_tcp_server_is_running(void)
{
    return s_running && s_task_handle != NULL;
}
