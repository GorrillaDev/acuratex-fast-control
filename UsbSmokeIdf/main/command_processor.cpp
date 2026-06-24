#include <inttypes.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>

#include "esp_log.h"
#include "esp_timer.h"

#include "command_processor.h"
#include "line_codec.h"
#include "file_transfer.h"
#include "command_head_program_runner.h"

// [ACURATEX] Este archivo es el clasificador central de lineas.
// Recibe texto ya separado por un transporte y decide si es comando simple,
// FILE_*, HEAD_*, CAN explicito o texto generico.

// [ESP-IDF] TAG identifica los logs emitidos por este modulo.
static const char *TAG = "command_processor";

/**
 * [POR QUE EXISTE]
 * Esta funcion existe para etiquetar en logs si una linea fue procesada como
 * proveniente de USB o TCP.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_send_status(), app_process_text_command() y
 * app_command_process_line() al emitir logs.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que se registra o clasifica una linea entrante.
 *
 * [ENTRADAS]
 * Recibe el puntero al entorno de comando creado por app_main o por el callback
 * TCP.
 *
 * [SALIDAS]
 * Devuelve `USB`, `TCP` o `?` si no hay entorno.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea y no usa mutex; solo lee env.
 *
 * [FLUJO ACURATEX]
 * Transporte -> env.usb_mounted -> etiqueta de log.
 *
 * [EQUIVALENCIA MCU]
 * Es comparable a imprimir por que puerto serie llego un comando.
 *
 * [SI NO EXISTIERA]
 * Los logs no indicarian la ruta de entrada del comando.
 */
static const char *app_transport_name(const app_command_env_t *env)
{
    if (env == NULL) {
        return "?";
    }

    // [ACURATEX] El entorno solo distingue USB activo frente a ruta de red.
    // En Fase 2 UART de rescate reutiliza el mismo entorno y puede verse como
    // USB/TCP segun usb_mounted.
    return env->usb_mounted ? "USB" : "TCP";
}

/**
 * [POR QUE EXISTE]
 * Esta funcion arma la respuesta textual del comando `status`.
 *
 * [QUIEN LA LLAMA]
 * Es llamada desde app_command_process_line() cuando la linea normalizada es
 * `status`.
 *
 * [CUANDO SE EJECUTA]
 * Bajo pedido explicito de la aplicacion o de un operador por transporte.
 *
 * [ENTRADAS]
 * Recibe el callback de respuesta, su contexto y el entorno actual.
 *
 * [SALIDAS]
 * Devuelve el resultado de reply().
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado; solo lee campos del entorno.
 *
 * [CONCURRENCIA]
 * No toma mutex directamente. El callback reply puede tomar el mutex del
 * transporte correspondiente.
 *
 * [FLUJO ACURATEX]
 * App -> `status` -> app_send_status -> respuesta `STATUS ...`.
 *
 * [EQUIVALENCIA MCU]
 * Es una funcion de diagnostico que devuelve una linea con flags actuales.
 *
 * [SI NO EXISTIERA]
 * La aplicacion no podria consultar en una sola linea USB, WiFi, IP, TCP y CAN.
 */
static esp_err_t app_send_status(app_reply_fn_t reply, void *ctx, const app_command_env_t *env)
{
    char line[160];

    // [ACURATEX] El formato de esta respuesta es parte del protocolo textual.
    snprintf(line, sizeof(line),
             "STATUS usb=%s wifi=%s ip=%s tcp_port=%d CAN=%s bus=%s ssid=%s",
             env->usb_mounted ? "mounted" : "detached",
             env->wifi_connected ? "connected" : "disconnected",
             env->wifi_ip,
             env->tcp_port,
             env->can_status,
             env->active_bus_name,
             env->wifi_ssid);

    return reply(line, ctx);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion prueba si una linea parece una trama CAN textual antes de
 * clasificarla definitivamente como texto generico.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_command_process_line().
 *
 * [CUANDO SE EJECUTA]
 * Despues de descartar comandos conocidos y `send `.
 *
 * [ENTRADAS]
 * Recibe la linea y los limites CAN del entorno.
 *
 * [SALIDAS]
 * Devuelve true si app_parse_frame_line() logra extraer ID y datos validos.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado externo. Usa variables locales temporales.
 *
 * [CONCURRENCIA]
 * No bloquea.
 *
 * [FLUJO ACURATEX]
 * Linea textual -> posible trama CAN -> app_process_frame_command().
 *
 * [EQUIVALENCIA MCU]
 * Es una validacion de formato antes de enviar bytes a un periferico.
 *
 * [SI NO EXISTIERA]
 * Una trama CAN sin prefijo `send` caeria como texto generico.
 */
static bool app_looks_like_frame(const char *line, const app_command_env_t *env)
{
    // [ACURATEX] `id` recibira el identificador estandar; `data` contiene bytes
    // crudos y `len` sera el DLC real que se pasa al driver.
    uint32_t id = 0;
    uint8_t data[64] = {0};
    size_t len = 0;

    // [ACURATEX] Se reutiliza el mismo parser que luego se usara para enviar,
    // garantizando que "parece trama" y "se puede enviar" tengan la misma regla.
    return app_parse_frame_line(line, &id, data, &len, env->can_max_frame_len, env->can_std_id_mask);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion convierte una linea de texto en una trama CAN y la transmite
 * usando el callback del entorno.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_command_process_line() para `send ...` o para lineas que
 * directamente parecen tramas CAN.
 *
 * [CUANDO SE EJECUTA]
 * Cuando la aplicacion pide transmitir CAN desde el protocolo textual.
 *
 * [ENTRADAS]
 * Recibe la linea con ID/datos hex, el callback de respuesta, contexto y
 * entorno CAN.
 *
 * [SALIDAS]
 * Responde `TX_OK ...` si el envio fue correcto o `ERR|CAN_TX|...` si el driver
 * devuelve error.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado propio. Puede modificar estado externo a traves de
 * env->can_send_standard si el driver actualiza estadisticas/estado.
 *
 * [CONCURRENCIA]
 * No usa mutex propio. El callback CAN puede bloquear segun el driver TWAI y el
 * callback reply puede bloquear por mutex de transporte.
 *
 * [FLUJO ACURATEX]
 * App -> `send` o trama -> parser HEX -> can_send_standard -> TWAI/CAN ->
 * respuesta a app.
 *
 * [EQUIVALENCIA MCU]
 * Es el puente entre comando ASCII y escritura en periferico CAN.
 *
 * [SI NO EXISTIERA]
 * La aplicacion no podria enviar tramas CAN arbitrarias por el protocolo base.
 */
static esp_err_t app_process_frame_command(const char *line, app_reply_fn_t reply, void *ctx, const app_command_env_t *env)
{
    uint32_t id = 0;
    uint8_t data[64] = {0};
    size_t len = 0;
    char response[160];
    esp_err_t err;

    // [ACURATEX] env->can_max_frame_len=8 y env->can_std_id_mask=0x7FF salen
    // de app_fill_command_env(), manteniendo CAN clasico estandar.
    if (!app_parse_frame_line(line, &id, data, &len, env->can_max_frame_len, env->can_std_id_mask)) {
        return reply("ERR frame invalido", ctx);
    }

    // [ACURATEX] Si no hay bus activo, se usa CAN1 por defecto. Este
    // comportamiento se conserva tal cual.
    err = env->can_send_standard(
        (env->active_bus == APP_CMD_CAN_BUS_NONE) ? APP_CMD_CAN_BUS_1 : env->active_bus,
        id, data, len);

    if (err != ESP_OK) {
        snprintf(response, sizeof(response), "ERR|CAN_TX|%s|CODE=%d",
                 esp_err_to_name(err),
                 (int)err);
        esp_err_t reply_err = reply(response, ctx);
        return reply_err == ESP_OK ? err : reply_err;
    }

    // [ACURATEX] El formato TX_OK informa bus, ID estandar y DLC final enviado.
    snprintf(response, sizeof(response), "TX_OK bus=%s id=0x%03" PRIX32 " dlc=%u",
             env->active_bus_name, id, (unsigned)len);
    return reply(response, ctx);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion maneja lineas que no coinciden con ningun comando conocido ni
 * con una trama CAN.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_command_process_line() como ultimo caso.
 *
 * [CUANDO SE EJECUTA]
 * Al final de la cadena de clasificacion.
 *
 * [ENTRADAS]
 * Recibe la linea, callback de respuesta, contexto y entorno.
 *
 * [SALIDAS]
 * Si env->text_input existe, devuelve lo que ese callback responda. Si no
 * existe, responde `ACK TXT ...`.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado propio.
 *
 * [CONCURRENCIA]
 * Puede bloquear indirectamente si el callback de texto o respuesta bloquea.
 *
 * [FLUJO ACURATEX]
 * App -> texto no clasificado -> text_input o ACK TXT -> respuesta.
 *
 * [EQUIVALENCIA MCU]
 * Es un handler por defecto de comandos no reconocidos.
 *
 * [SI NO EXISTIERA]
 * Las lineas no reconocidas no tendrian respuesta consistente.
 */
static esp_err_t app_process_text_command(const char *line, app_reply_fn_t reply, void *ctx, const app_command_env_t *env)
{
    ESP_LOGI(TAG, "TXT RX [%s]: %s", app_transport_name(env), line);

    if (env->text_input != NULL) {
        // [C/C++] text_input es un puntero a funcion provisto por app_main.
        // Permite redirigir este caso sin que el parser conozca la implementacion.
        return env->text_input(line, reply, ctx);
    }

    char response[160];
    snprintf(response, sizeof(response), "ACK TXT %s", line);
    return reply(response, ctx);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion entrega una marca temporal en milisegundos para los logs de ping.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_command_process_line() al procesar `ping` o `hello`.
 *
 * [CUANDO SE EJECUTA]
 * Solo durante esos comandos de prueba/vida.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve milisegundos desde arranque como long long.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea; consulta esp_timer.
 *
 * [FLUJO ACURATEX]
 * ping -> log FW_PING_RX/FW_PONG_TX con tiempo y estado de cabezal.
 *
 * [EQUIVALENCIA MCU]
 * Similar a usar millis() para marcar un evento de comunicacion.
 *
 * [SI NO EXISTIERA]
 * Los logs de ping no tendrian timestamp local.
 */
static long long app_now_ms(void)
{
    // [ESP-IDF] esp_timer_get_time() devuelve microsegundos; se divide para
    // registrar milisegundos.
    return (long long)(esp_timer_get_time() / 1000LL);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion existe para que Core 0 pueda reconocer comandos fisicos sin
 * ejecutarlos directamente.
 *
 * [QUIEN LA LLAMA]
 * La llama app_command_dispatch_task() antes de decidir si procesa localmente o
 * encola hacia head_runtime.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que una linea ya copiada sale de app_command_ingress_queue.
 *
 * [ENTRADAS]
 * Recibe la linea original y el entorno actual, usado solo para validar si una
 * linea sin prefijo parece trama CAN.
 *
 * [SALIDAS]
 * Devuelve true para HEAD_*, can1, can2, send y tramas CAN directas.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado. Solo normaliza una copia local y consulta parsers.
 *
 * [CONCURRENCIA]
 * No bloquea ni toma mutex; es segura para correr en Core 0 como clasificador.
 *
 * [FLUJO ACURATEX]
 * Transporte -> cola ingreso -> clasificador fisico -> cola Core 1.
 *
 * [EQUIVALENCIA MCU]
 * Equivale a separar comandos rapidos de UI de comandos que tocan perifericos.
 *
 * [SI NO EXISTIERA]
 * Core 0 tendria que ejecutar directamente HEAD_* o CAN para reconocerlos.
 */
bool app_command_line_is_physical(const char *incoming_line,
                                  const app_command_env_t *env)
{
    char line[192];

    if (incoming_line == NULL || env == NULL) {
        return false;
    }

    strncpy(line, incoming_line, sizeof(line) - 1);
    line[sizeof(line) - 1] = '\0';
    app_trim_line(line);

    if (line[0] == '\0') {
        return false;
    }

    if (strcasecmp(line, "can1") == 0 || strcasecmp(line, "can2") == 0) {
        return true;
    }

    if (app_head_program_is_command(line)) {
        return true;
    }

    if (strncasecmp(line, "send ", 5) == 0) {
        return true;
    }

    return app_looks_like_frame(line, env);
}

/**
 * [POR QUE EXISTE]
 * Esta es la funcion principal del procesador de comandos. Existe para que USB,
 * TCP y UART compartan la misma logica de protocolo.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_main() para USB, app_handle_uart_line() para UART de rescate y
 * app_handle_tcp_line() desde la tarea TCP.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que algun transporte entrega una linea completa no necesariamente
 * normalizada.
 *
 * [ENTRADAS]
 * Recibe la linea original, un callback de respuesta, contexto del transporte y
 * app_command_env_t con estado/callbacks actuales.
 *
 * [SALIDAS]
 * Devuelve ESP_OK o el error producido por el handler seleccionado.
 *
 * [ESTADO QUE MODIFICA]
 * No guarda estado propio. Puede cambiar estado externo al invocar callbacks:
 * seleccion de CAN, transmision CAN, FILE_* o HEAD_*.
 *
 * [CONCURRENCIA]
 * Puede ejecutarse desde el bucle principal o desde la tarea TCP. No usa mutex
 * interno; delega sincronizacion a transportes, CAN, archivos y runner.
 *
 * [FLUJO ACURATEX]
 * App -> transporte -> buffer de linea -> app_command_process_line ->
 * handler especifico -> respuesta.
 *
 * [EQUIVALENCIA MCU]
 * Es una tabla de decision implementada con if/else, parecida a un parser de
 * comandos por puerto serie.
 *
 * [SI NO EXISTIERA]
 * Cada transporte tendria que duplicar la logica de reconocer comandos.
 */
esp_err_t app_command_process_line(const char *incoming_line,
                                   app_reply_fn_t reply,
                                   void *ctx,
                                   const app_command_env_t *env)
{
    char line[192];
    esp_err_t err;

    if (incoming_line == NULL || reply == NULL || env == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    // [C/C++] Se copia a un buffer local modificable porque app_trim_line()
    // altera la cadena. incoming_line puede apuntar a memoria de otro modulo.
    strncpy(line, incoming_line, sizeof(line) - 1);
    line[sizeof(line) - 1] = '\0';
    app_trim_line(line);

    if (line[0] == '\0') {
        return ESP_OK;
    }

    ESP_LOGI(TAG, "CMD RX [%s]: %s", app_transport_name(env), line);

    // [ACURATEX] Comandos de vida. No cambian estado; confirman que el enlace y
    // el procesador estan respondiendo.
    if (strcasecmp(line, "ping") == 0 || strcasecmp(line, "hello") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: ping", app_transport_name(env));
        ESP_LOGI(TAG, "FW_PING_RX|TIME=%lld|HEAD_STATE=%s",
                 app_now_ms(),
                 app_head_program_runner_state_name());
        ESP_LOGI(TAG, "FW_PONG_TX|TIME=%lld|HEAD_STATE=%s",
                 app_now_ms(),
                 app_head_program_runner_state_name());
        return reply("PONG", ctx);
    }

    // [ACURATEX] Respuesta de ayuda textual. El texto forma parte del contrato
    // esperado por herramientas humanas o app.
    if (strcasecmp(line, "help") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: help", app_transport_name(env));
        return reply("OK cmds: ping,status,can1,can2,send <hex>,<hex line>,start,stop,testeo,FILE_*,HEAD_*", ctx);
    }

    // [ACURATEX] status resume transportes y CAN usando el entorno actual.
    if (strcasecmp(line, "status") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: status", app_transport_name(env));
        return app_send_status(reply, ctx, env);
    }

    // [ACURATEX] can1/can2 seleccionan el bus logico usado por futuras tramas.
    // No reinstalan TWAI ni cambian pines: actualizan el selector del driver.
    if (strcasecmp(line, "can1") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: can_select CAN1", app_transport_name(env));
        err = env->can_select_bus(APP_CMD_CAN_BUS_1);
        if (err != ESP_OK) {
            return reply("ERR no se pudo activar CAN1", ctx);
        }
        return reply("OK CAN1", ctx);
    }

    if (strcasecmp(line, "can2") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: can_select CAN2", app_transport_name(env));
        err = env->can_select_bus(APP_CMD_CAN_BUS_2);
        if (err != ESP_OK) {
            return reply("ERR no se pudo activar CAN2", ctx);
        }
        return reply("OK CAN2", ctx);
    }

    // [ACURATEX] Comandos simples conservados como ACK. No se reinterpretan en
    // esta fase porque cambiarian protocolo.
    if (strcasecmp(line, "start") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: start", app_transport_name(env));
        return reply("ACK start", ctx);
    }

    if (strcasecmp(line, "stop") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: stop", app_transport_name(env));
        return reply("ACK stop", ctx);
    }

    if (strcasecmp(line, "testeo") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: testeo", app_transport_name(env));
        return reply("ACK testeo", ctx);
    }

    // [ACURATEX] HEAD_* se evalua antes que FILE_* y antes que CAN textual.
    // Ese orden evita que comandos de cabezal caigan al handler generico.
    if (app_head_program_is_command(line)) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: head_program", app_transport_name(env));
        return app_head_program_process_line(line, reply, ctx, env);
    }

    // [ACURATEX] FILE_* se procesa aqui, pero el detalle de LittleFS queda en
    // file_transfer.cpp. La respuesta se guarda en un buffer local y luego se
    // envia por el transporte original, igual que cualquier ACK/ERR de comando.
    if (app_file_transfer_is_command(line)) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: file_transfer", app_transport_name(env));
        char response[192];
        err = app_file_transfer_process_line(line, response, sizeof(response));
        if (err != ESP_OK) {
            return err;
        }
        return reply(response, ctx);
    }

    // [ACURATEX] `send ` fuerza que el resto de la linea sea interpretado como
    // trama CAN aunque no sea el primer caso de clasificacion. El prefijo no se
    // pasa al parser; el parser recibe solamente ID y bytes.
    if (strncasecmp(line, "send ", 5) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: can_send explicit", app_transport_name(env));
        return app_process_frame_command(line + 5, reply, ctx, env);
    }

    // [ACURATEX] Tambien se acepta una trama CAN directa, por ejemplo una linea
    // de ID y bytes hexadecimales sin prefijo.
    if (app_looks_like_frame(line, env)) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: can_frame", app_transport_name(env));
        return app_process_frame_command(line, reply, ctx, env);
    }

    // [ACURATEX] Ultimo caso: texto que no coincide con ningun comando conocido.
    ESP_LOGI(TAG, "CMD CLASS [%s]: text_passthrough", app_transport_name(env));
    return app_process_text_command(line, reply, ctx, env);
}
