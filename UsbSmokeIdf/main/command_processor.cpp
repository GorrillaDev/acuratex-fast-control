#include <inttypes.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>

#include "esp_log.h"
#include "esp_timer.h"

#include "command_processor.h"
#include "file_transfer.h"
#include "line_codec.h"
#include "head_state_manager.h"
#include "head_fast_diag.h"
#include "wifi_manager.h"

// [ACURATEX] Este archivo es el clasificador central de lineas.
// Recibe texto ya separado por un transporte y decide si es comando simple,
// FILE_*, HEAD_*, CAN explicito o texto generico.

// [ESP-IDF] TAG identifica los logs emitidos por este modulo.
static const char *TAG = "command_processor";
static bool app_command_is_wifi_config_set(const char *line)
{
    return line != NULL && strncasecmp(line, "WIFI_CONFIG_SET", 15) == 0;
}

static const char *app_command_log_line(const char *line, char *scratch, size_t scratch_size)
{
    if (line == NULL) {
        return "";
    }

    if (app_command_is_wifi_config_set(line)) {
        snprintf(scratch, scratch_size, "WIFI_CONFIG_SET|PASS=<redacted>");
        return scratch;
    }

    return line;
}

static bool app_wifi_arg_value(const char *token, const char *key, const char **value)
{
    size_t key_len;

    if (token == NULL || key == NULL || value == NULL) {
        return false;
    }

    key_len = strlen(key);
    if (strncasecmp(token, key, key_len) != 0 || token[key_len] != '=') {
        return false;
    }

    *value = token + key_len + 1;
    return true;
}

static bool app_wifi_copy_config_value(char *dest,
                                       size_t dest_size,
                                       const char *value)
{
    size_t len;

    if (dest == NULL || value == NULL || dest_size == 0) {
        return false;
    }

    len = strlen(value);
    if (len == 0 || len >= dest_size) {
        return false;
    }

    strlcpy(dest, value, dest_size);
    return true;
}

static bool app_wifi_parse_config_port(const char *value, int *port)
{
    char *endptr = NULL;
    long parsed;

    if (value == NULL || port == NULL || value[0] == '\0') {
        return false;
    }

    parsed = strtol(value, &endptr, 10);
    if (endptr == value || *endptr != '\0' || parsed < 1 || parsed > 65535) {
        return false;
    }

    *port = (int)parsed;
    return true;
}

static esp_err_t app_handle_wifi_config_get(app_reply_fn_t reply,
                                            void *ctx,
                                            const app_command_env_t *env)
{
    app_wifi_settings_t settings = {};
    char reason[96] = {};
    char response[192];
    bool loaded = app_wifi_manager_load_settings(&settings, reason, sizeof(reason));
    const char *status = "missing";

    if (loaded) {
        status = env != NULL && env->wifi_connected ? "connected" : "saved";
        ESP_LOGI(TAG, "WIFI_CONFIG_LOAD_OK|SSID=%s|PORT=%d", settings.ssid, settings.port);
    }

    snprintf(response,
             sizeof(response),
             "WIFI_CONFIG|SSID=%s|PORT=%d|STATUS=%s|IP=%s|REASON=%s",
             loaded ? settings.ssid : "",
             loaded ? settings.port : 3333,
             status,
             (env != NULL && env->wifi_ip != NULL) ? env->wifi_ip : "0.0.0.0",
             loaded ? "OK" : reason);
    return reply(response, ctx);
}

static esp_err_t app_handle_wifi_config_set(const char *line,
                                            app_reply_fn_t reply,
                                            void *ctx)
{
    char work[192];
    char *saveptr = NULL;
    char *token;
    app_wifi_settings_t next = {};
    app_wifi_settings_t existing = {};
    char reason[96] = {};
    bool has_existing = false;
    bool pass_provided = false;
    bool port_provided = false;

    if (line == NULL) {
        return reply("ERR WIFI_CONFIG", ctx);
    }

    has_existing = app_wifi_manager_load_settings(&existing, reason, sizeof(reason));
    if (has_existing) {
        next = existing;
    } else {
        next.port = 3333;
    }

    strlcpy(work, line, sizeof(work));
    token = strtok_r(work, "|", &saveptr);
    (void)token;

    while ((token = strtok_r(NULL, "|", &saveptr)) != NULL) {
        const char *value = NULL;

        if (app_wifi_arg_value(token, "SSID", &value)) {
            char clean[APP_WIFI_MAX_SSID_LEN + 1];
            strlcpy(clean, value, sizeof(clean));
            app_trim_line(clean);
            if (!app_wifi_copy_config_value(next.ssid, sizeof(next.ssid), clean)) {
                return reply("ERR WIFI_CONFIG SSID", ctx);
            }
            continue;
        }

        if (app_wifi_arg_value(token, "PASS", &value)) {
            char clean[APP_WIFI_MAX_PASS_LEN + 1];
            strlcpy(clean, value, sizeof(clean));
            app_trim_line(clean);
            if (clean[0] != '\0') {
                if (!app_wifi_copy_config_value(next.pass, sizeof(next.pass), clean)) {
                    return reply("ERR WIFI_CONFIG PASS", ctx);
                }
                pass_provided = true;
            }
            continue;
        }

        if (app_wifi_arg_value(token, "PORT", &value)) {
            char clean[12];
            strlcpy(clean, value, sizeof(clean));
            app_trim_line(clean);
            if (!app_wifi_parse_config_port(clean, &next.port)) {
                return reply("ERR WIFI_CONFIG PORT", ctx);
            }
            port_provided = true;
        }
    }

    if (next.ssid[0] == '\0') {
        return reply("ERR WIFI_CONFIG SSID", ctx);
    }

    if (next.pass[0] == '\0' && !pass_provided) {
        return reply("ERR WIFI_CONFIG PASS_REQUIRED", ctx);
    }

    if (!port_provided && next.port <= 0) {
        next.port = 3333;
    }

    if (!app_wifi_manager_save_settings(&next, reason, sizeof(reason))) {
        char response[128];
        snprintf(response, sizeof(response), "ERR WIFI_CONFIG %s", reason[0] != '\0' ? reason : "SAVE");
        return reply(response, ctx);
    }

    ESP_LOGI(TAG, "WIFI_CONFIG_SAVE_OK|SSID=%s|PORT=%d", next.ssid, next.port);
    return reply("ACK WIFI_CONFIG_SAVE", ctx);
}

static esp_err_t app_handle_wifi_connect(app_reply_fn_t reply,
                                         void *ctx,
                                         const app_command_env_t *env)
{
    app_wifi_settings_t settings = {};
    char reason[96] = {};

    if (env != NULL && env->usb_mounted) {
        return reply("ERR WIFI_USB_ACTIVE", ctx);
    }

    if (!app_wifi_manager_load_settings(&settings, reason, sizeof(reason))) {
        char response[128];
        snprintf(response, sizeof(response), "ERR WIFI_CONFIG %s", reason[0] != '\0' ? reason : "LOAD");
        return reply(response, ctx);
    }

    ESP_LOGI(TAG, "WIFI_CONNECT_BEGIN|SSID=%s|PORT=%d", settings.ssid, settings.port);
    esp_err_t err = app_wifi_manager_start(&settings);
    if (err != ESP_OK) {
        char response[96];
        snprintf(response, sizeof(response), "ERR WIFI_CONNECT %s", esp_err_to_name(err));
        ESP_LOGW(TAG, "WIFI_CONNECT_ERROR|ERR=%s", esp_err_to_name(err));
        return reply(response, ctx);
    }

    return reply("ACK WIFI_CONNECT", ctx);
}

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
    ESP_LOGW(TAG, "CMD UNKNOWN [%s]: %s", app_transport_name(env), line);
    return reply("ERR unknown command", ctx);
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

static bool app_parse_index_value_command(const char *line,
                                          const char *prefix,
                                          int *index_out,
                                          int *value_out)
{
    int index = 0;
    int value = 0;
    char extra = '\0';

    if (line == NULL || prefix == NULL) {
        return false;
    }

    size_t prefix_len = strlen(prefix);
    if (strncasecmp(line, prefix, prefix_len) != 0) {
        return false;
    }

    if (sscanf(line + prefix_len, "%d|%d%c", &index, &value, &extra) != 2) {
        return false;
    }

    if (index <= 0 || value < 0 || value > 0xFFFF) {
        return false;
    }

    if (index_out != NULL) {
        *index_out = index;
    }
    if (value_out != NULL) {
        *value_out = value;
    }
    return true;
}

static esp_err_t app_handle_short_position_command(const char *line,
                                                   const char *prefix,
                                                   int max_index,
                                                   int motor_index,
                                                   app_reply_fn_t reply,
                                                   void *ctx,
                                                   const app_command_env_t *env)
{
    int index = 0;
    int value = 0;

    if (!app_parse_index_value_command(line, prefix, &index, &value)) {
        return reply("ERR posicion invalida", ctx);
    }

    if (index < 1 || index > max_index) {
        return reply("ERR posicion invalida", ctx);
    }

    char frame[64];
    int physical_index = motor_index + (index - 1);
    snprintf(frame, sizeof(frame),
             "320 1C %02X %02X %02X",
             physical_index & 0xFF,
             value & 0xFF,
             (value >> 8) & 0xFF);
    return app_process_frame_command(frame, reply, ctx, env);
}

static esp_err_t app_handle_j_short_command(const char *line,
                                            app_reply_fn_t reply,
                                            void *ctx,
                                            const app_command_env_t *env)
{
    int bus = (env->active_bus == APP_CMD_CAN_BUS_NONE) ? APP_CMD_CAN_BUS_1 : env->active_bus;
    uint32_t now_ms = (uint32_t)app_now_ms();
    char response[80];

    if (strcasecmp(line, "j_run_all") == 0) {
        for (uint8_t j = 1; j <= APP_HEAD_STATE_MAX_J; ++j) {
            if (!app_head_state_manager_start_j_run(j, bus, now_ms)) {
                return reply("ERR|J_RUN_ALL", ctx);
            }
        }

        ESP_LOGI(TAG, "FW_RX|J_RUN_ALL");
        return reply("OK j_run_all", ctx);
    }

    if (strcasecmp(line, "j_stop_all") == 0) {
        app_head_state_manager_stop_all_j_runs();
        ESP_LOGI(TAG, "FW_RX|J_STOP_ALL");
        return reply("OK j_stop_all", ctx);
    }

    if (strncasecmp(line, "j_run_", 6) == 0) {
        int instance = 0;
        if (sscanf(line + 6, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_J) {
            return reply("ERR|J_RUN", ctx);
        }

        if (!app_head_state_manager_start_j_run((uint8_t)instance, bus, now_ms)) {
            return reply("ERR|J_RUN", ctx);
        }

        snprintf(response, sizeof(response), "OK j_run_%d", instance);
        ESP_LOGI(TAG, "FW_RX|J_RUN|J%d", instance);
        return reply(response, ctx);
    }

    if (strncasecmp(line, "j_stop_", 7) == 0) {
        int instance = 0;
        if (sscanf(line + 7, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_J) {
            return reply("ERR|J_STOP", ctx);
        }

        if (!app_head_state_manager_stop_j_run((uint8_t)instance)) {
            return reply("ERR|J_STOP", ctx);
        }

        snprintf(response, sizeof(response), "OK j_stop_%d", instance);
        ESP_LOGI(TAG, "FW_RX|J_STOP|J%d", instance);
        return reply(response, ctx);
    }

    return reply("ERR|J_CMD", ctx);
}

static int app_command_active_bus(const app_command_env_t *env)
{
    return (env != NULL && env->active_bus == APP_CMD_CAN_BUS_2) ? APP_CMD_CAN_BUS_2 : APP_CMD_CAN_BUS_1;
}

static esp_err_t app_handle_yarn_short_command(const char *line,
                                               app_reply_fn_t reply,
                                               void *ctx,
                                               const app_command_env_t *env)
{
    int bus = app_command_active_bus(env);
    uint32_t now_ms = (uint32_t)app_now_ms();

    if (strcasecmp(line, "y_run_all") == 0) {
        for (uint8_t y = 1; y <= APP_HEAD_STATE_MAX_YARN; ++y) {
            if (!app_head_state_manager_start_yarn_run(y, bus, now_ms)) {
                return reply("ERR|Y_RUN_ALL", ctx);
            }
        }

        ESP_LOGI(TAG, "FW_RX|Y_RUN_ALL");
        return reply("OK y_run_all", ctx);
    }

    if (strcasecmp(line, "y_stop_all") == 0) {
        app_head_state_manager_stop_all_yarn_runs();
        ESP_LOGI(TAG, "FW_RX|Y_STOP_ALL");
        return reply("OK y_stop_all", ctx);
    }

    if (strcasecmp(line, "y1_run") == 0) {
        if (!app_head_state_manager_start_yarn_run(1, bus, now_ms)) {
            return reply("ERR|Y1_RUN", ctx);
        }
        ESP_LOGI(TAG, "FW_RX|Y1_RUN");
        return reply("OK y1_run", ctx);
    }

    if (strcasecmp(line, "y1_stop") == 0) {
        if (!app_head_state_manager_stop_yarn_run(1)) {
            return reply("ERR|Y1_STOP", ctx);
        }
        ESP_LOGI(TAG, "FW_RX|Y1_STOP");
        return reply("OK y1_stop", ctx);
    }

    if (strcasecmp(line, "y2_run") == 0) {
        if (!app_head_state_manager_start_yarn_run(2, bus, now_ms)) {
            return reply("ERR|Y2_RUN", ctx);
        }
        ESP_LOGI(TAG, "FW_RX|Y2_RUN");
        return reply("OK y2_run", ctx);
    }

    if (strcasecmp(line, "y2_stop") == 0) {
        if (!app_head_state_manager_stop_yarn_run(2)) {
            return reply("ERR|Y2_STOP", ctx);
        }
        ESP_LOGI(TAG, "FW_RX|Y2_STOP");
        return reply("OK y2_stop", ctx);
    }

    return reply("ERR|Y_CMD", ctx);
}

static esp_err_t app_handle_stitch_short_command(const char *line,
                                                 app_reply_fn_t reply,
                                                 void *ctx,
                                                 const app_command_env_t *env)
{
    int bus = app_command_active_bus(env);
    uint32_t now_ms = (uint32_t)app_now_ms();
    char response[80];

    if (strcasecmp(line, "s_run_all") == 0) {
        for (uint8_t s = 1; s <= APP_HEAD_STATE_MAX_STITCH; ++s) {
            if (!app_head_state_manager_start_stitch_run(s, bus, now_ms)) {
                return reply("ERR|S_RUN_ALL", ctx);
            }
        }

        ESP_LOGI(TAG, "FW_RX|S_RUN_ALL");
        return reply("OK s_run_all", ctx);
    }

    if (strcasecmp(line, "s_stop_all") == 0) {
        app_head_state_manager_stop_all_stitch_runs();
        ESP_LOGI(TAG, "FW_RX|S_STOP_ALL");
        return reply("OK s_stop_all", ctx);
    }

    if (strncasecmp(line, "s_run_", 6) == 0) {
        int instance = 0;
        if (sscanf(line + 6, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_STITCH) {
            return reply("ERR|S_RUN", ctx);
        }

        if (!app_head_state_manager_start_stitch_run((uint8_t)instance, bus, now_ms)) {
            return reply("ERR|S_RUN", ctx);
        }

        snprintf(response, sizeof(response), "OK s_run_%d", instance);
        ESP_LOGI(TAG, "FW_RX|S_RUN|S%d", instance);
        return reply(response, ctx);
    }

    if (strncasecmp(line, "s_stop_", 7) == 0) {
        int instance = 0;
        if (sscanf(line + 7, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_STITCH) {
            return reply("ERR|S_STOP", ctx);
        }

        if (!app_head_state_manager_stop_stitch_run((uint8_t)instance)) {
            return reply("ERR|S_STOP", ctx);
        }

        snprintf(response, sizeof(response), "OK s_stop_%d", instance);
        ESP_LOGI(TAG, "FW_RX|S_STOP|S%d", instance);
        return reply(response, ctx);
    }

    return reply("ERR|S_CMD", ctx);
}

static esp_err_t app_handle_den_short_command(const char *line,
                                              app_reply_fn_t reply,
                                              void *ctx,
                                              const app_command_env_t *env)
{
    int bus = app_command_active_bus(env);
    uint32_t now_ms = (uint32_t)app_now_ms();
    char response[80];

    if (strncasecmp(line, "den_run1_", 9) == 0) {
        int instance = 0;
        if (sscanf(line + 9, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_J) {
            return reply("ERR|DEN_RUN1", ctx);
        }

        if (!app_head_state_manager_start_den_run1((uint8_t)instance, bus, now_ms)) {
            return reply("ERR|DEN_RUN1", ctx);
        }

        snprintf(response, sizeof(response), "OK den_run1_%d", instance);
        ESP_LOGI(TAG, "FW_RX|DEN_RUN1|J%d", instance);
        return reply(response, ctx);
    }

    if (strncasecmp(line, "den_stop1_", 10) == 0) {
        int instance = 0;
        if (sscanf(line + 10, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_J) {
            return reply("ERR|DEN_STOP1", ctx);
        }

        if (!app_head_state_manager_stop_den_run((uint8_t)instance)) {
            return reply("ERR|DEN_STOP1", ctx);
        }

        snprintf(response, sizeof(response), "OK den_stop1_%d", instance);
        ESP_LOGI(TAG, "FW_RX|DEN_STOP1|J%d", instance);
        return reply(response, ctx);
    }

    if (strncasecmp(line, "den_run_", 8) == 0) {
        int instance = 0;
        if (sscanf(line + 8, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_J) {
            return reply("ERR|DEN_RUN", ctx);
        }

        if (!app_head_state_manager_start_den_run((uint8_t)instance, bus, now_ms)) {
            return reply("ERR|DEN_RUN", ctx);
        }

        snprintf(response, sizeof(response), "OK den_run_%d", instance);
        ESP_LOGI(TAG, "FW_RX|DEN_RUN|J%d", instance);
        return reply(response, ctx);
    }

    if (strncasecmp(line, "den_stop_", 9) == 0) {
        int instance = 0;
        if (sscanf(line + 9, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_J) {
            return reply("ERR|DEN_STOP", ctx);
        }

        if (!app_head_state_manager_stop_den_run((uint8_t)instance)) {
            return reply("ERR|DEN_STOP", ctx);
        }

        snprintf(response, sizeof(response), "OK den_stop_%d", instance);
        ESP_LOGI(TAG, "FW_RX|DEN_STOP|J%d", instance);
        return reply(response, ctx);
    }

    return reply("ERR|DEN_CMD", ctx);
}

static esp_err_t app_handle_sic_short_command(const char *line,
                                              app_reply_fn_t reply,
                                              void *ctx,
                                              const app_command_env_t *env)
{
    int bus = app_command_active_bus(env);
    uint32_t now_ms = (uint32_t)app_now_ms();
    char response[80];

    if (strncasecmp(line, "sic_run_", 8) == 0) {
        int instance = 0;
        if (sscanf(line + 8, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_SIC) {
            return reply("ERR|SIC_RUN", ctx);
        }

        if (!app_head_state_manager_start_sic_run((uint8_t)instance, bus, now_ms)) {
            return reply("ERR|SIC_RUN", ctx);
        }

        snprintf(response, sizeof(response), "OK sic_run_%d", instance);
        ESP_LOGI(TAG, "FW_RX|SIC_RUN|S%d", instance);
        return reply(response, ctx);
    }

    if (strncasecmp(line, "sic_stop_", 9) == 0) {
        int instance = 0;
        if (sscanf(line + 9, "%d", &instance) != 1 || instance < 1 || instance > APP_HEAD_STATE_MAX_SIC) {
            return reply("ERR|SIC_STOP", ctx);
        }

        if (!app_head_state_manager_stop_sic_run((uint8_t)instance)) {
            return reply("ERR|SIC_STOP", ctx);
        }

        snprintf(response, sizeof(response), "OK sic_stop_%d", instance);
        ESP_LOGI(TAG, "FW_RX|SIC_STOP|S%d", instance);
        return reply(response, ctx);
    }

    return reply("ERR|SIC_CMD", ctx);
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
 * Devuelve true para can1, can2, j_run_*, j_stop_*, y_*, s_*, den_*, sic_*,
 * den_pos_*, sic_pos_*, send y tramas CAN directas.
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

    if (strncasecmp(line, "j_run_", 6) == 0
        || strncasecmp(line, "j_stop_", 7) == 0
        || strcasecmp(line, "y_run_all") == 0
        || strcasecmp(line, "y_stop_all") == 0
        || strcasecmp(line, "y1_run") == 0
        || strcasecmp(line, "y1_stop") == 0
        || strcasecmp(line, "y2_run") == 0
        || strcasecmp(line, "y2_stop") == 0
        || strcasecmp(line, "s_run_all") == 0
        || strcasecmp(line, "s_stop_all") == 0
        || strncasecmp(line, "s_run_", 6) == 0
        || strncasecmp(line, "s_stop_", 7) == 0
        || strncasecmp(line, "den_run1_", 9) == 0
        || strncasecmp(line, "den_stop1_", 10) == 0
        || strncasecmp(line, "den_run_", 8) == 0
        || strncasecmp(line, "den_stop_", 9) == 0
        || strncasecmp(line, "sic_run_", 8) == 0
        || strncasecmp(line, "sic_stop_", 9) == 0
        || strncasecmp(line, "den_pos_", 8) == 0
        || strncasecmp(line, "sic_pos_", 8) == 0
        || strncasecmp(line, "send ", 5) == 0
        || strcasecmp(line, "stop") == 0
        || strcasecmp(line, "emergency_stop") == 0) {
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

    char log_line[96];
    ESP_LOGI(TAG, "CMD RX [%s]: %s", app_transport_name(env), app_command_log_line(line, log_line, sizeof(log_line)));

    if (app_file_transfer_is_command(line)) {
        char response[256];
        ESP_LOGI(TAG, "CMD CLASS [%s]: file_transfer", app_transport_name(env));
        esp_err_t err = app_file_transfer_process_line(line, response, sizeof(response));
        if (err != ESP_OK) {
            return reply("ERR FILE_CMD", ctx);
        }
        if (response[0] == '\0') {
            return reply("ERR FILE_CMD", ctx);
        }
        ESP_LOGI(TAG, "FILE_TX|%s", response);
        return reply(response, ctx);
    }

    if (strcasecmp(line, "WIFI_CONFIG_GET") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: wifi_config_get", app_transport_name(env));
        return app_handle_wifi_config_get(reply, ctx, env);
    }

    if (strncasecmp(line, "WIFI_CONFIG_SET", 15) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: wifi_config_set", app_transport_name(env));
        return app_handle_wifi_config_set(line, reply, ctx);
    }

    if (strcasecmp(line, "WIFI_CONNECT") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: wifi_connect", app_transport_name(env));
        return app_handle_wifi_connect(reply, ctx, env);
    }

    if (strcasecmp(line, "ping") == 0 || strcasecmp(line, "hello") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: ping", app_transport_name(env));
        ESP_LOGI(TAG, "FW_PING_RX|TIME=%lld|HEAD_STATE=%s",
                 app_now_ms(),
                 "FAST");
        ESP_LOGI(TAG, "FW_PONG_TX|TIME=%lld|HEAD_STATE=%s",
                 app_now_ms(),
                 "FAST");
        return reply("PONG", ctx);
    }

    if (strcasecmp(line, "help") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: help", app_transport_name(env));
        return reply("OK cmds: ping,status,can1,can2,init,testeo,start,stop,emergency_stop,j_run_#,j_stop_#,j_run_all,j_stop_all,y1_run,y1_stop,y2_run,y2_stop,y_run_all,y_stop_all,s_run_#,s_stop_#,s_run_all,s_stop_all,den_run_#,den_run1_#,den_stop_#,den_stop1_#,sic_run_#,sic_stop_#,den_pos_#|#,sic_pos_#|#,send <hex>,<hex line>", ctx);
    }

    if (strcasecmp(line, "status") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: status", app_transport_name(env));
        return app_send_status(reply, ctx, env);
    }

    if (strcasecmp(line, "can1") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: can_select CAN1", app_transport_name(env));
        if (env->can_select_bus(APP_CMD_CAN_BUS_1) != ESP_OK) {
            return reply("ERR no se pudo activar CAN1", ctx);
        }
        return reply("OK CAN1", ctx);
    }

    if (strcasecmp(line, "can2") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: can_select CAN2", app_transport_name(env));
        if (env->can_select_bus(APP_CMD_CAN_BUS_2) != ESP_OK) {
            return reply("ERR no se pudo activar CAN2", ctx);
        }
        return reply("OK CAN2", ctx);
    }

    if (strcasecmp(line, "init") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: init", app_transport_name(env));
        if (app_head_fast_diag_is_busy()) {
            return reply("ERR|INIT|BUSY", ctx);
        }
        (void)app_head_state_manager_init();
        esp_err_t err = app_head_fast_diag_start_init(env, ctx);
        if (err != ESP_OK) {
            if (err == ESP_ERR_INVALID_STATE && app_head_fast_diag_is_busy()) {
                return reply("ERR|INIT|BUSY", ctx);
            }
            char response[120];
            snprintf(response, sizeof(response), "ERR|INIT|%s", esp_err_to_name(err));
            return reply(response, ctx);
        }
        return reply("OK init", ctx);
    }

    if (strcasecmp(line, "testeo") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: testeo", app_transport_name(env));
        if (app_head_fast_diag_is_busy()) {
            return reply("ERR|TESTEO|BUSY", ctx);
        }
        if (!app_head_fast_diag_testeo_can_start()) {
            return reply("ERR|TESTEO|LATCHED", ctx);
        }
        esp_err_t err = app_head_fast_diag_start_testeo(env, ctx);
        if (err != ESP_OK) {
            char response[120];
            snprintf(response, sizeof(response), "ERR|TESTEO|%s", esp_err_to_name(err));
            return reply(response, ctx);
        }
        return reply("OK testeo", ctx);
    }

    if (strcasecmp(line, "start") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: start", app_transport_name(env));
        return reply("OK start", ctx);
    }

    if (strcasecmp(line, "stop") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: stop", app_transport_name(env));
        app_head_state_manager_stop_all_motion();
        app_head_fast_diag_request_stop();
        return reply("OK stop", ctx);
    }

    if (strcasecmp(line, "emergency_stop") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: emergency_stop", app_transport_name(env));
        app_head_state_manager_stop_all_motion();
        app_head_fast_diag_request_stop();
        return reply("OK emergency_stop", ctx);
    }

    if (strcasecmp(line, "j_run_all") == 0
        || strcasecmp(line, "j_stop_all") == 0
        || strncasecmp(line, "j_run_", 6) == 0
        || strncasecmp(line, "j_stop_", 7) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: j_short", app_transport_name(env));
        return app_handle_j_short_command(line, reply, ctx, env);
    }

    if (strcasecmp(line, "y_run_all") == 0
        || strcasecmp(line, "y_stop_all") == 0
        || strcasecmp(line, "y1_run") == 0
        || strcasecmp(line, "y1_stop") == 0
        || strcasecmp(line, "y2_run") == 0
        || strcasecmp(line, "y2_stop") == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: y_short", app_transport_name(env));
        return app_handle_yarn_short_command(line, reply, ctx, env);
    }

    if (strcasecmp(line, "s_run_all") == 0
        || strcasecmp(line, "s_stop_all") == 0
        || strncasecmp(line, "s_run_", 6) == 0
        || strncasecmp(line, "s_stop_", 7) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: s_short", app_transport_name(env));
        return app_handle_stitch_short_command(line, reply, ctx, env);
    }

    if (strncasecmp(line, "den_run1_", 9) == 0
        || strncasecmp(line, "den_stop1_", 10) == 0
        || strncasecmp(line, "den_run_", 8) == 0
        || strncasecmp(line, "den_stop_", 9) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: den_short", app_transport_name(env));
        return app_handle_den_short_command(line, reply, ctx, env);
    }

    if (strncasecmp(line, "sic_run_", 8) == 0
        || strncasecmp(line, "sic_stop_", 9) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: sic_short", app_transport_name(env));
        return app_handle_sic_short_command(line, reply, ctx, env);
    }

    if (strncasecmp(line, "den_pos_", 8) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: den_pos", app_transport_name(env));
        return app_handle_short_position_command(line, "den_pos_", 8, 0, reply, ctx, env);
    }

    if (strncasecmp(line, "sic_pos_", 8) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: sic_pos", app_transport_name(env));
        return app_handle_short_position_command(line, "sic_pos_", 2, 0x08, reply, ctx, env);
    }

    if (strncasecmp(line, "send ", 5) == 0) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: can_send explicit", app_transport_name(env));
        return app_process_frame_command(line + 5, reply, ctx, env);
    }

    if (app_looks_like_frame(line, env)) {
        ESP_LOGI(TAG, "CMD CLASS [%s]: can_frame", app_transport_name(env));
        return app_process_frame_command(line, reply, ctx, env);
    }

    ESP_LOGI(TAG, "CMD CLASS [%s]: text_passthrough", app_transport_name(env));
    return app_process_text_command(line, reply, ctx, env);
}
