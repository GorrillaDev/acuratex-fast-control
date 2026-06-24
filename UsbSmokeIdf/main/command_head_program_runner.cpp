#include <ctype.h>
#include <errno.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <stdint.h>

#include "esp_log.h"
#include "esp_heap_caps.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"

#include "command_head_program_runner.h"
#include "head_state_manager.h"
#include "app_rtos_types.h"
#include "line_codec.h"

// [ACURATEX] Base LittleFS donde se guardan los programas TXT transferidos con
// FILE_* y luego seleccionados con HEAD_PROGRAM_SELECT.
#define APP_HEAD_FS_BASE "/fs"
// [ACURATEX] Limites de protocolo para evitar nombres, acciones y lineas sin
// frontera en buffers fijos.
#define APP_HEAD_MAX_NAME_LEN 48
#define APP_HEAD_MAX_ACTION_LEN 48
#define APP_HEAD_MAX_LINE_LEN 224
// [ACURATEX] WAIT/DELAY dentro de TXT no puede superar este valor.
#define APP_HEAD_MAX_WAIT_MS 10000
// [ACURATEX] Maximos y defaults de modulos leidos desde lineas MODULE|... del
// programa TXT. Se usan para estado/telemetria de cabezal.
#define APP_HEAD_DEN_MAX_COUNT 8
#define APP_HEAD_SIC_MAX_COUNT 2
#define APP_HEAD_J_MAX_COUNT 8
#define APP_HEAD_YARN_MAX_COUNT 2
#define APP_HEAD_STITCH_MAX_COUNT 4
#define APP_HEAD_DEN_DEFAULT_COUNT 8
#define APP_HEAD_SIC_DEFAULT_COUNT 2
#define APP_HEAD_J_DEFAULT_COUNT 8
#define APP_HEAD_YARN_DEFAULT_COUNT 2
#define APP_HEAD_STITCH_DEFAULT_COUNT 4
// [ACURATEX] Capacidad explicita para la respuesta HEAD_STATUS. Debe cubrir el
// estado actual y margen para campos futuros antes de llegar a la cola/USB/TCP.
#define APP_HEAD_STATUS_RESPONSE_CAPACITY APP_REPLY_MAX_LINE_LENGTH

// [ESP-IDF] TAG identifica logs de este modulo.
static const char *TAG = "head_program";

// [ACURATEX] Estado de alto nivel del runner HEAD_ACTION.
typedef enum {
    APP_HEAD_RUNNER_IDLE = 0,
    APP_HEAD_RUNNER_RUNNING,
    APP_HEAD_RUNNER_STOPPING,
    APP_HEAD_RUNNER_DONE,
    APP_HEAD_RUNNER_ERROR,
} app_head_runner_state_t;

// [ACURATEX] Resultado de buscar y ejecutar un bloque BEGIN|accion ... END.
typedef struct {
    bool found;
    bool has_value;
    int value;
    int error_line;
    char error[96];
} app_head_action_result_t;

// [ACURATEX] Contexto especial para acciones J dinamicas que usan placeholder
// XX. Vive dentro de los argumentos de la tarea HEAD_ACTION para que el calculo
// del byte candidato acompanhe a la ejecucion del TXT.
typedef struct {
    // [ACURATEX] true si la accion actual es Jx.CHy valida y puede usar XX.
    bool active;
    // [ACURATEX] true cuando el byte candidato ya fue confirmado en el gestor
    // de estado despues de CAN OK.
    bool state_committed;
    // [ACURATEX] Numero J en base 1: J1 = 1, J8 = 8.
    uint8_t instance;
    // [ACURATEX] Canal CH en base 1: CH1 = 1, CH8 = 8.
    uint8_t channel;
    // [ACURATEX] Registro fisico leido antes de ejecutar el TXT.
    uint8_t previous_physical;
    // [ACURATEX] Registro fisico propuesto despues de alternar el canal.
    uint8_t candidate_physical;
} app_head_j_dynamic_context_t;

// [ACURATEX] Conteos de modulos declarados por el TXT con lineas MODULE.
typedef struct {
    int den_count;
    int sic_count;
    int j_count;
    int yarn_count;
    int stitch_count;
    bool has_explicit_configuration;
} app_head_module_counts_t;

// [ACURATEX] Programa TXT activo seleccionado con HEAD_PROGRAM_SELECT.
static char s_active_program[APP_HEAD_MAX_NAME_LEN + 1];
// [ACURATEX] Accion actualmente en ejecucion, si existe.
static char s_current_action[APP_HEAD_MAX_ACTION_LEN + 1];
// [ACURATEX] Linea actual dentro del TXT mientras se ejecuta HEAD_ACTION.
static int s_current_line;
// [ACURATEX] Marca temporal de inicio de la accion actual.
static long long s_action_start_ms;
// [ACURATEX] Ultimo error y ultima etapa sirven para HEAD_STATUS y diagnostico.
static char s_last_error[96];
static char s_last_stage[48];
// [C/C++] volatile porque la bandera puede consultarse dentro de la tarea de
// accion y cambiarse desde HEAD_STOP.
static volatile bool s_stop_requested;
static app_head_runner_state_t s_runner_state = APP_HEAD_RUNNER_IDLE;
static app_head_module_counts_t s_module_counts;
// [FREERTOS] Handle de la tarea que ejecuta HEAD_ACTION.
static TaskHandle_t s_runner_task_handle = NULL;
// [FREERTOS] Mutex que protege estado compartido del runner.
static SemaphoreHandle_t s_state_mutex = NULL;

static void app_head_state_lock(void);
static void app_head_state_unlock(void);
static void app_head_set_stage(const char *stage);
static void app_head_set_error(const char *message);
static bool app_head_task_is_alive(TaskHandle_t handle);
static bool app_head_reply_status_line(app_reply_fn_t reply, void *ctx, const app_command_env_t *env);
static esp_err_t app_head_execute_allowed_line(const char *line,
                                               int line_number,
                                               const char *action,
                                               app_head_j_dynamic_context_t *dynamic_ctx,
                                               long long start_ms,
                                               app_reply_fn_t reply,
                                               void *ctx,
                                               const app_command_env_t *env,
                                               char *error,
                                               size_t error_len);
static bool app_head_parse_dynamic_j_action(const char *action, app_head_j_dynamic_context_t *dynamic_ctx);
static esp_err_t app_head_expand_send_line(const char *line,
                                           const app_head_j_dynamic_context_t *dynamic_ctx,
                                           char *expanded_line,
                                           size_t expanded_line_len,
                                           bool *used_placeholder,
                                           char *error,
                                           size_t error_len);
static bool app_head_emit_state_event(const char *action,
                                      bool has_value,
                                      int value,
                                      app_reply_fn_t reply,
                                      void *ctx);

typedef struct {
    char program[APP_HEAD_MAX_NAME_LEN + 1];
    char action[APP_HEAD_MAX_ACTION_LEN + 1];
    app_head_j_dynamic_context_t dynamic_ctx;
    app_reply_fn_t reply;
    void *reply_ctx;
    bool reply_ctx_owned;
    long long start_ms;
    app_command_env_t env;
} app_head_runner_task_args_t;

/**
 * [POR QUE EXISTE]
 * Centraliza el envio de respuestas HEAD_* y registra cada linea enviada.
 *
 * [QUIEN LA LLAMA]
 * La usan casi todas las funciones del runner cuando deben responder a la app.
 *
 * [CUANDO SE EJECUTA]
 * Durante HEAD_PROGRAM_SELECT, HEAD_ACTION, HEAD_STATUS, HEAD_STOP y dentro de
 * la tarea de accion.
 *
 * [ENTRADAS]
 * Recibe callback de respuesta, contexto del transporte y linea sin salto final.
 *
 * [SALIDAS]
 * Devuelve el esp_err_t del callback reply.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado funcional; solo emite log.
 *
 * [CONCURRENCIA]
 * Puede ejecutarse desde app_main o desde la tarea head_action_task. La
 * sincronizacion real de salida ocurre en el callback del transporte.
 *
 * [FLUJO ACURATEX]
 * Runner HEAD_* -> app_head_reply_logged -> USB/TCP/UART.
 *
 * [EQUIVALENCIA MCU]
 * Es una funcion Serial.println() comun para todos los transportes.
 *
 * [SI NO EXISTIERA]
 * Cada respuesta HEAD tendria que repetir log y validacion de linea vacia.
 */
static esp_err_t app_head_reply_logged(app_reply_fn_t reply, void *ctx, const char *line)
{
    const char *safe_line = (line != NULL && line[0] != '\0')
        ? line
        : "ERR|HEAD|EMPTY_RESPONSE";
    ESP_LOGI(TAG, "HEAD TX: %s", safe_line);
    return reply(safe_line, ctx);
}

/**
 * [POR QUE EXISTE]
 * Permite formar respuestas HEAD_* con formato printf sin duplicar buffers.
 *
 * [QUIEN LA LLAMA]
 * La llaman handlers de programa, accion, estado y limpieza de tarea.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que la respuesta necesita insertar accion, archivo, linea, error o
 * tiempo.
 *
 * [ENTRADAS]
 * Recibe callback, contexto, formato y argumentos variables.
 *
 * [SALIDAS]
 * Devuelve el resultado de app_head_reply_logged().
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Usa buffer local, por lo que no comparte memoria entre tareas.
 *
 * [FLUJO ACURATEX]
 * Evento del runner -> formato textual -> transporte -> app.
 *
 * [EQUIVALENCIA MCU]
 * Equivale a snprintf() seguido de Serial.println().
 *
 * [SI NO EXISTIERA]
 * Habria respuestas formateadas repetidas y mas faciles de escribir mal.
 */
static esp_err_t app_head_replyf(app_reply_fn_t reply, void *ctx, const char *fmt, ...)
{
    char response[APP_HEAD_STATUS_RESPONSE_CAPACITY];
    va_list args;
    int written;

    // [C/C++] va_list permite recibir argumentos variables como printf.
    va_start(args, fmt);
    written = vsnprintf(response, sizeof(response), fmt, args);
    va_end(args);

    if (written < 0 || written >= (int)sizeof(response)) {
        ESP_LOGW(TAG,
                 "HEAD_REPLY_TRUNCATED|CAPACITY=%u|REQUESTED=%d",
                 (unsigned)sizeof(response),
                 written);
    }

    return app_head_reply_logged(reply, ctx, response);
}

/**
 * [POR QUE EXISTE]
 * Traduce el enum interno del runner a texto de protocolo/status.
 *
 * [QUIEN LA LLAMA]
 * HEAD_STATUS, logs y funciones publicas de diagnostico.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que se necesita exponer IDLE/RUNNING/STOPPING/DONE/ERROR.
 *
 * [ENTRADAS]
 * Recibe el enum interno app_head_runner_state_t.
 *
 * [SALIDAS]
 * Devuelve un literal estable con el nombre del estado.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No toma mutex; el llamador decide si ya tiene snapshot protegido.
 *
 * [FLUJO ACURATEX]
 * s_runner_state -> texto -> HEAD_STATUS/log.
 *
 * [EQUIVALENCIA MCU]
 * Es convertir el estado de una maquina de estados a texto de debug.
 *
 * [SI NO EXISTIERA]
 * Cada respuesta tendria que repetir el switch de estados.
 */
static const char *app_head_state_name(app_head_runner_state_t state)
{
    switch (state) {
    case APP_HEAD_RUNNER_RUNNING:
        return "RUNNING";
    case APP_HEAD_RUNNER_STOPPING:
        return "STOPPING";
    case APP_HEAD_RUNNER_DONE:
        return "DONE";
    case APP_HEAD_RUNNER_ERROR:
        return "ERROR";
    case APP_HEAD_RUNNER_IDLE:
    default:
        return "IDLE";
    }
}

/**
 * [POR QUE EXISTE]
 * Expone el estado actual del runner para otros modulos y logs de comunicacion.
 *
 * [QUIEN LA LLAMA]
 * La llama command_processor.cpp al registrar ping y otros modulos de diagnostico.
 *
 * [CUANDO SE EJECUTA]
 * Bajo consulta externa, no necesariamente durante una accion.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve un literal con el estado actual.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Toma s_state_mutex para leer s_runner_state de forma coherente.
 *
 * [FLUJO ACURATEX]
 * Diagnostico -> estado runner -> log/status.
 *
 * [EQUIVALENCIA MCU]
 * Es leer el estado de una maquina de estados protegida.
 *
 * [SI NO EXISTIERA]
 * Otros modulos no podrian reportar si Cabezal esta IDLE/RUNNING/etc.
 */
const char *app_head_program_runner_state_name(void)
{
    app_head_runner_state_t state;

    app_head_state_lock();
    state = s_runner_state;
    app_head_state_unlock();
    return app_head_state_name(state);
}

/**
 * [POR QUE EXISTE]
 * Permite diagnosticar en que etapa interna quedo el runner.
 *
 * [QUIEN LA LLAMA]
 * La llaman callbacks de salida cuando hay timeout de mutex y comandos de
 * estado/diagnostico.
 *
 * [CUANDO SE EJECUTA]
 * Cuando se necesita reportar bloqueo o estado actual.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve una copia static de la ultima etapa o `NONE`.
 *
 * [ESTADO QUE MODIFICA]
 * Modifica solo stage_copy local static para devolver un puntero estable.
 *
 * [CONCURRENCIA]
 * Toma mutex para copiar s_last_stage. La copia static no debe modificarse por
 * el llamador.
 *
 * [FLUJO ACURATEX]
 * Error/timeout -> last_stage -> diagnostico hacia app/log.
 *
 * [EQUIVALENCIA MCU]
 * Es como guardar el ultimo punto alcanzado de una rutina larga.
 *
 * [SI NO EXISTIERA]
 * Un bloqueo de salida no indicaria si ocurrio abriendo archivo, buscando bloque
 * o ejecutando linea.
 */
const char *app_head_program_runner_last_stage(void)
{
    static char stage_copy[48];

    app_head_state_lock();
    strlcpy(stage_copy, s_last_stage[0] == '\0' ? "NONE" : s_last_stage, sizeof(stage_copy));
    app_head_state_unlock();
    return stage_copy;
}

/**
 * [POR QUE EXISTE]
 * Entrega tiempo en milisegundos para elapsed/progreso del runner.
 *
 * [QUIEN LA LLAMA]
 * La llaman progreso, status, inicio/fin de INIT y logs de WAIT.
 *
 * [CUANDO SE EJECUTA]
 * Durante ejecucion de acciones y respuestas de estado.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve milisegundos desde arranque.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea; consulta temporizador ESP-IDF.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION -> tiempo inicio/progreso -> app.
 *
 * [EQUIVALENCIA MCU]
 * Similar a millis().
 *
 * [SI NO EXISTIERA]
 * No habria ELAPSED ni marcas temporales en el flujo de Cabezal.
 */
static long long app_head_now_ms(void)
{
    return (long long)(esp_timer_get_time() / 1000LL);
}

/**
 * [POR QUE EXISTE]
 * Toma el mutex que protege el estado del runner de Cabezal.
 *
 * [QUIEN LA LLAMA]
 * Funciones que leen o modifican s_runner_state, accion actual, linea, etapa y
 * error.
 *
 * [CUANDO SE EJECUTA]
 * Antes de hacer snapshots o cambios de estado compartido.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * No cambia campos de Cabezal; ocupa el mutex.
 *
 * [CONCURRENCIA]
 * Usa portMAX_DELAY: la tarea espera indefinidamente hasta poder entrar.
 *
 * [FLUJO ACURATEX]
 * HEAD_STATUS/HEAD_ACTION -> tomar mutex -> leer o cambiar estado.
 *
 * [EQUIVALENCIA MCU]
 * Es entrar a una seccion critica protegida.
 *
 * [SI NO EXISTIERA]
 * HEAD_STATUS podria leer una mezcla de accion/linea/estado inconsistente.
 */
static void app_head_state_lock(void)
{
    if (s_state_mutex != NULL)
    {
        xSemaphoreTake(s_state_mutex, portMAX_DELAY);
    }
}

/**
 * [POR QUE EXISTE]
 * Libera el mutex de estado tomado por app_head_state_lock().
 *
 * [QUIEN LA LLAMA]
 * Las mismas rutas que entran a la seccion protegida.
 *
 * [CUANDO SE EJECUTA]
 * Al terminar una lectura o escritura protegida.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * No cambia campos del runner; libera el mutex.
 *
 * [CONCURRENCIA]
 * Permite que otra tarea consulte o actualice estado.
 *
 * [FLUJO ACURATEX]
 * Snapshot/cambio terminado -> liberar mutex -> otras rutas continuan.
 *
 * [EQUIVALENCIA MCU]
 * Es salir de una seccion critica.
 *
 * [SI NO EXISTIERA]
 * La primera ruta que tomara el mutex bloquearia las demas.
 */
static void app_head_state_unlock(void)
{
    if (s_state_mutex != NULL)
    {
        xSemaphoreGive(s_state_mutex);
    }
}

/**
 * [POR QUE EXISTE]
 * Actualiza la etapa visible en logs y HEAD_STATUS.
 *
 * [QUIEN LA LLAMA]
 * La tarea HEAD_ACTION al construir ruta, abrir archivo, buscar bloque, ejecutar
 * linea y limpiar.
 *
 * [CUANDO SE EJECUTA]
 * En puntos importantes del flujo largo del interprete TXT.
 *
 * [ENTRADAS]
 * Texto corto de etapa.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe s_last_stage.
 *
 * [CONCURRENCIA]
 * El llamador decide si ya esta bajo mutex; HEAD_STATUS lee una copia bajo mutex.
 *
 * [FLUJO ACURATEX]
 * OPEN_FILE/SEARCH_BLOCK/EXECUTE_LINE -> LAST_STAGE en HEAD_STATUS.
 *
 * [EQUIVALENCIA MCU]
 * Es guardar el ultimo paso alcanzado de una rutina larga.
 *
 * [SI NO EXISTIERA]
 * Un fallo solo diria ERROR, sin indicar en que etapa ocurrio.
 */
static void app_head_set_stage(const char *stage)
{
    if (stage == NULL || stage[0] == '\0') {
        s_last_stage[0] = '\0';
        return;
    }

    strlcpy(s_last_stage, stage, sizeof(s_last_stage));
}

/**
 * [POR QUE EXISTE]
 * Comprueba si un TaskHandle_t aun representa una tarea no eliminada.
 *
 * [QUIEN LA LLAMA]
 * app_head_status().
 *
 * [CUANDO SE EJECUTA]
 * Al responder HEAD_STATUS.
 *
 * [ENTRADAS]
 * Handle de tarea FreeRTOS.
 *
 * [SALIDAS]
 * true si el handle no es NULL y eTaskGetState() no devuelve eDeleted.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Consulta al scheduler de FreeRTOS.
 *
 * [FLUJO ACURATEX]
 * HEAD_STATUS -> task_alive -> diagnostico de accion viva.
 *
 * [EQUIVALENCIA MCU]
 * Es preguntar al RTOS si una tarea sigue existiendo.
 *
 * [SI NO EXISTIERA]
 * HEAD_STATUS no podria detectar RUNNING sin tarea viva.
 */
static bool app_head_task_is_alive(TaskHandle_t handle)
{
    if (handle == NULL) {
        return false;
    }

    eTaskState state = eTaskGetState(handle);
    return state != eDeleted;
}

/**
 * [POR QUE EXISTE]
 * Permite que una linea `status` dentro del TXT responda el estado general de
 * comunicacion sin salir del bloque BEGIN/END.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_execute_allowed_line() cuando encuentra `status`.
 *
 * [CUANDO SE EJECUTA]
 * Durante la ejecucion de una accion HEAD_ACTION.
 *
 * [ENTRADAS]
 * Recibe callback de respuesta, contexto y app_command_env_t capturado al crear
 * la tarea.
 *
 * [SALIDAS]
 * Devuelve true si la respuesta se envio correctamente.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Corre dentro de head_action_task; la respuesta usa mutex del transporte.
 *
 * [FLUJO ACURATEX]
 * TXT `status` -> STATUS usb/wifi/ip/CAN -> app.
 *
 * [EQUIVALENCIA MCU]
 * Es un comando de diagnostico dentro de un script.
 *
 * [SI NO EXISTIERA]
 * Un TXT no podria pedir diagnostico intermedio.
 */
static bool app_head_reply_status_line(app_reply_fn_t reply, void *ctx, const app_command_env_t *env)
{
    char line[192];

    if (env == NULL) {
        return false;
    }

    snprintf(line,
             sizeof(line),
             "STATUS usb=%s wifi=%s ip=%s tcp_port=%d CAN=%s bus=%s ssid=%s",
             env->usb_mounted ? "mounted" : "detached",
             env->wifi_connected ? "connected" : "disconnected",
             env->wifi_ip != NULL ? env->wifi_ip : "",
             env->tcp_port,
             env->can_status != NULL ? env->can_status : "ERROR",
             env->active_bus_name != NULL ? env->active_bus_name : "NONE",
             env->wifi_ssid != NULL ? env->wifi_ssid : "");

    return app_head_reply_logged(reply, ctx, line) == ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Copia en un solo punto los campos que forman el snapshot de HEAD_STATUS.
 *
 * [QUIEN LA LLAMA]
 * app_head_status().
 *
 * [CUANDO SE EJECUTA]
 * Cuando la app consulta HEAD_STATUS.
 *
 * [ENTRADAS]
 * Punteros opcionales de salida para estado, programa, accion, linea, tiempo,
 * error, etapa, conteos y handle.
 *
 * [SALIDAS]
 * No devuelve valor; llena los punteros no NULL.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * El llamador toma s_state_mutex antes de llamar, para que todos los campos
 * pertenezcan al mismo instante logico.
 *
 * [FLUJO ACURATEX]
 * HEAD_STATUS -> snapshot -> formatear respuesta.
 *
 * [EQUIVALENCIA MCU]
 * Es copiar variables de una maquina de estados antes de imprimirlas.
 *
 * [SI NO EXISTIERA]
 * HEAD_STATUS tendria lecturas dispersas y mas riesgo de inconsistencias.
 */
static void app_head_snapshot_state(app_head_runner_state_t *state,
                                    char *program,
                                    size_t program_len,
                                    char *action,
                                    size_t action_len,
                                    int *line,
                                    long long *start_ms,
                                    char *last_error,
                                    size_t last_error_len,
                                    char *last_stage,
                                    size_t last_stage_len,
                                    app_head_module_counts_t *counts,
                                    TaskHandle_t *task_handle)
{
    if (state != NULL)
    {
        *state = s_runner_state;
    }
    if (program != NULL && program_len > 0)
    {
        strlcpy(program, s_active_program, program_len);
    }
    if (action != NULL && action_len > 0)
    {
        strlcpy(action, s_current_action, action_len);
    }
    if (line != NULL)
    {
        *line = s_current_line;
    }
    if (start_ms != NULL)
    {
        *start_ms = s_action_start_ms;
    }
    if (last_error != NULL && last_error_len > 0)
    {
        strlcpy(last_error, s_last_error, last_error_len);
    }
    if (last_stage != NULL && last_stage_len > 0)
    {
        strlcpy(last_stage, s_last_stage, last_stage_len);
    }
    if (counts != NULL)
    {
        *counts = s_module_counts;
    }
    if (task_handle != NULL)
    {
        *task_handle = s_runner_task_handle;
    }
}

// [ACURATEX] Un estado ocupado impide seleccionar otro programa o iniciar otra
// accion simultanea.
static bool app_head_is_busy_state(app_head_runner_state_t state)
{
    return state == APP_HEAD_RUNNER_RUNNING || state == APP_HEAD_RUNNER_STOPPING;
}

/**
 * [POR QUE EXISTE]
 * Guarda el ultimo error visible por HEAD_STATUS.
 *
 * [QUIEN LA LLAMA]
 * Rutas de seleccion, ejecucion y limpieza de HEAD_ACTION.
 *
 * [CUANDO SE EJECUTA]
 * Cuando se detecta un error o cuando se limpia el error anterior.
 *
 * [ENTRADAS]
 * Texto de error o NULL para limpiar.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe s_last_error.
 *
 * [CONCURRENCIA]
 * Debe usarse dentro del esquema de proteccion del runner cuando hay concurrencia.
 *
 * [FLUJO ACURATEX]
 * Error -> s_last_error -> HEAD_STATUS|LAST_ERROR=...
 *
 * [EQUIVALENCIA MCU]
 * Es guardar un codigo de diagnostico persistente.
 *
 * [SI NO EXISTIERA]
 * HEAD_STATUS perderia la causa del ultimo fallo.
 */
static void app_head_set_error(const char *message)
{
    if (message == NULL) {
        s_last_error[0] = '\0';
        return;
    }

    strlcpy(s_last_error, message, sizeof(s_last_error));
}

/**
 * [POR QUE EXISTE]
 * Evita nombres de programa peligrosos o incompatibles con LittleFS/protocolo.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_is_valid_program_name().
 *
 * [CUANDO SE EJECUTA]
 * Durante HEAD_PROGRAM_SELECT y al construir rutas de archivo.
 *
 * [ENTRADAS]
 * Recibe nombre de archivo.
 *
 * [SALIDAS]
 * Devuelve true si contiene caracteres o secuencias no permitidas.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea.
 *
 * [FLUJO ACURATEX]
 * HEAD_PROGRAM_SELECT -> validar nombre -> construir /fs/nombre.
 *
 * [EQUIVALENCIA MCU]
 * Es una barrera para no salir del directorio esperado del sistema de archivos.
 *
 * [SI NO EXISTIERA]
 * Un nombre con `..`, `/` o `|` podria romper ruta o protocolo.
 */
static bool app_head_has_disallowed_name_chars(const char *name)
{
    if (name == NULL || name[0] == '\0') {
        return true;
    }

    return strstr(name, "..") != NULL
        || strchr(name, '/') != NULL
        || strchr(name, '\\') != NULL
        || strchr(name, '|') != NULL
        || strchr(name, '\r') != NULL
        || strchr(name, '\n') != NULL;
}

/**
 * [POR QUE EXISTE]
 * Define que nombres de programa TXT son aceptados por el runner de Cabezal.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_head_select_program(), app_head_build_path() y
 * app_head_program_exists().
 *
 * [CUANDO SE EJECUTA]
 * Al seleccionar programa o abrirlo para leer.
 *
 * [ENTRADAS]
 * Recibe nombre de archivo.
 *
 * [SALIDAS]
 * Devuelve true si cumple prefijo `cbz.uni.prog` o `cbz.mod.prog`, numero
 * positivo y sufijo `.txt`.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea.
 *
 * [FLUJO ACURATEX]
 * App -> HEAD_PROGRAM_SELECT|archivo -> validacion -> LittleFS.
 *
 * [EQUIVALENCIA MCU]
 * Es validar que un archivo de receta pertenece al formato esperado.
 *
 * [SI NO EXISTIERA]
 * Cualquier archivo de /fs podria intentar ejecutarse como programa de Cabezal.
 */
static bool app_head_is_valid_program_name(const char *name)
{
    const char *prefix_uni = "cbz.uni.prog";
    const char *prefix_mod = "cbz.mod.prog";
    const char *number_start = NULL;
    const char *dot_txt = NULL;
    size_t len;

    if (name == NULL) {
        return false;
    }

    len = strlen(name);
    if (len == 0 || len > APP_HEAD_MAX_NAME_LEN || app_head_has_disallowed_name_chars(name)) {
        return false;
    }

    if (strncasecmp(name, prefix_uni, strlen(prefix_uni)) == 0) {
        number_start = name + strlen(prefix_uni);
    } else if (strncasecmp(name, prefix_mod, strlen(prefix_mod)) == 0) {
        number_start = name + strlen(prefix_mod);
    } else {
        return false;
    }

    if (number_start[0] == '\0' || number_start[0] == '0') {
        return false;
    }

    dot_txt = strstr(number_start, ".txt");
    if (dot_txt == NULL || strcasecmp(dot_txt, ".txt") != 0 || dot_txt == number_start) {
        return false;
    }

    for (const char *p = number_start; p < dot_txt; ++p) {
        if (!isdigit((unsigned char)*p)) {
            return false;
        }
    }

    return true;
}

/**
 * [POR QUE EXISTE]
 * Construye la ruta LittleFS absoluta de un programa seleccionado.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_head_program_exists(), app_head_load_module_counts_from_program()
 * y app_head_find_and_execute_action().
 *
 * [CUANDO SE EJECUTA]
 * Antes de abrir el archivo TXT.
 *
 * [ENTRADAS]
 * Recibe nombre de archivo, buffer de salida y tamano del buffer.
 *
 * [SALIDAS]
 * Devuelve true si pudo formar `/fs/<archivo>` dentro del buffer.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe en `path`.
 *
 * [CONCURRENCIA]
 * No usa estado global.
 *
 * [FLUJO ACURATEX]
 * Programa activo -> path LittleFS -> fopen().
 *
 * [EQUIVALENCIA MCU]
 * Es formar la direccion de un archivo en memoria flash.
 *
 * [SI NO EXISTIERA]
 * Cada lector del TXT tendria que repetir validacion y snprintf de ruta.
 */
static bool app_head_build_path(const char *file_name, char *path, size_t path_len)
{
    int written;

    if (!app_head_is_valid_program_name(file_name) || path == NULL || path_len == 0) {
        return false;
    }

    written = snprintf(path, path_len, APP_HEAD_FS_BASE "/%s", file_name);
    return written > 0 && (size_t)written < path_len;
}

/**
 * [POR QUE EXISTE]
 * Verifica que el programa seleccionado exista en LittleFS antes de aceptarlo.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_select_program().
 *
 * [CUANDO SE EJECUTA]
 * Durante HEAD_PROGRAM_SELECT.
 *
 * [ENTRADAS]
 * Recibe nombre de archivo ya validado.
 *
 * [SALIDAS]
 * Devuelve true si fopen() puede abrir el archivo.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado persistente.
 *
 * [CONCURRENCIA]
 * Usa stdio sobre LittleFS, sin mutex propio.
 *
 * [FLUJO ACURATEX]
 * HEAD_PROGRAM_SELECT -> existe archivo -> programa activo.
 *
 * [EQUIVALENCIA MCU]
 * Es comprobar que una receta existe antes de seleccionarla.
 *
 * [SI NO EXISTIERA]
 * Se podria seleccionar un programa inexistente y fallar recien al ejecutar.
 */
static bool app_head_program_exists(const char *file_name)
{
    char path[96];
    FILE *file;

    if (!app_head_build_path(file_name, path, sizeof(path))) {
        return false;
    }

    file = fopen(path, "rb");
    if (file == NULL) {
        return false;
    }

    fclose(file);
    return true;
}

// [ACURATEX] Conteos por defecto cuando el TXT no declara MODULE|...|COUNT.
static app_head_module_counts_t app_head_default_module_counts(void)
{
    app_head_module_counts_t counts;
    counts.den_count = APP_HEAD_DEN_DEFAULT_COUNT;
    counts.sic_count = APP_HEAD_SIC_DEFAULT_COUNT;
    counts.j_count = APP_HEAD_J_DEFAULT_COUNT;
    counts.yarn_count = APP_HEAD_YARN_DEFAULT_COUNT;
    counts.stitch_count = APP_HEAD_STITCH_DEFAULT_COUNT;
    counts.has_explicit_configuration = false;

    return counts;
}

// [ACURATEX] Mapea el nombre textual de modulo a su contador, default y maximo.
static bool app_head_try_get_module_slot(app_head_module_counts_t *counts,
                                         const char *module_name,
                                         int **target_count,
                                         int *default_count,
                                         int *max_count)
{
    if (counts == NULL || module_name == NULL || target_count == NULL || default_count == NULL || max_count == NULL) {
        return false;
    }

    if (strcasecmp(module_name, "DEN") == 0) {
        *target_count = &counts->den_count;
        *default_count = APP_HEAD_DEN_DEFAULT_COUNT;
        *max_count = APP_HEAD_DEN_MAX_COUNT;
        return true;
    }

    if (strcasecmp(module_name, "SIC") == 0) {
        *target_count = &counts->sic_count;
        *default_count = APP_HEAD_SIC_DEFAULT_COUNT;
        *max_count = APP_HEAD_SIC_MAX_COUNT;
        return true;
    }

    if (strcasecmp(module_name, "J") == 0) {
        *target_count = &counts->j_count;
        *default_count = APP_HEAD_J_DEFAULT_COUNT;
        *max_count = APP_HEAD_J_MAX_COUNT;
        return true;
    }

    if (strcasecmp(module_name, "YARN") == 0) {
        *target_count = &counts->yarn_count;
        *default_count = APP_HEAD_YARN_DEFAULT_COUNT;
        *max_count = APP_HEAD_YARN_MAX_COUNT;
        return true;
    }

    if (strcasecmp(module_name, "STITCH") == 0) {
        *target_count = &counts->stitch_count;
        *default_count = APP_HEAD_STITCH_DEFAULT_COUNT;
        *max_count = APP_HEAD_STITCH_MAX_COUNT;
        return true;
    }

    return false;
}

// [C/C++] Parser decimal no negativo usado por MODULE|...|COUNT.
static bool app_head_try_parse_non_negative_int(const char *text, int *value)
{
    char *endptr = NULL;
    long parsed;

    if (text == NULL || value == NULL) {
        return false;
    }

    errno = 0;
    parsed = strtol(text, &endptr, 10);
    if (errno != 0 || endptr == text || *endptr != '\0' || parsed < 0 || parsed > 1000000L) {
        return false;
    }

    *value = (int)parsed;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Lee las lineas MODULE del programa TXT para ajustar cuantos modulos de cada
 * tipo existen en el estado de Cabezal.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_select_program() despues de guardar s_active_program.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que se selecciona un programa HEAD_PROGRAM_SELECT valido.
 *
 * [ENTRADAS]
 * Recibe el nombre del archivo TXT seleccionado.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_module_counts con defaults o valores leidos del TXT.
 *
 * [CONCURRENCIA]
 * Se llama con el mutex de estado tomado desde app_head_select_program().
 * Abre y lee LittleFS con fopen()/fgets().
 *
 * [FLUJO ACURATEX]
 * HEAD_PROGRAM_SELECT -> abrir TXT -> leer MODULE|J|COUNT|... -> HEAD_STATUS.
 *
 * [EQUIVALENCIA MCU]
 * Es leer parametros de una receta antes de ejecutar acciones.
 *
 * [SI NO EXISTIERA]
 * HEAD_STATUS no sabria adaptar conteos de modulos al programa activo.
 */
static void app_head_load_module_counts_from_program(const char *file_name)
{
    char path[96];
    char line[APP_HEAD_MAX_LINE_LEN];
    FILE *file;
    app_head_module_counts_t parsed_counts = app_head_default_module_counts();

    if (!app_head_build_path(file_name, path, sizeof(path))) {
        s_module_counts = parsed_counts;
        return;
    }

    // [LITTLEFS] El TXT se abre en modo binario lectura para no alterar bytes ni
    // finales de linea.
    file = fopen(path, "rb");
    if (file == NULL) {
        ESP_LOGW(TAG, "HEAD MODULE: no se pudo abrir %s, usando defaults", path);
        s_module_counts = parsed_counts;
        return;
    }

    while (fgets(line, sizeof(line), file) != NULL) {
        char buffer[APP_HEAD_MAX_LINE_LEN];
        char *saveptr = NULL;
        char *module_name = NULL;
        char *key = NULL;
        char *value_text = NULL;
        int *target_count = NULL;
        int default_count = 0;
        int max_count = 0;
        int parsed_count = 0;

        app_trim_line(line);
        if (line[0] == '\0' || line[0] == '#') {
            continue;
        }

        // [ACURATEX] Solo las lineas MODULE afectan conteos; BEGIN/END/send se
        // ignoran en esta pasada.
        if (strncasecmp(line, "MODULE|", 7) != 0) {
            continue;
        }

        strlcpy(buffer, line, sizeof(buffer));
        strtok_r(buffer, "|", &saveptr);
        module_name = strtok_r(NULL, "|", &saveptr);
        key = strtok_r(NULL, "|", &saveptr);
        value_text = strtok_r(NULL, "|", &saveptr);

        if (module_name == NULL) {
            ESP_LOGW(TAG, "HEAD MODULE: linea invalida (%s)", line);
            continue;
        }

        app_trim_line(module_name);
        if (!app_head_try_get_module_slot(&parsed_counts, module_name, &target_count, &default_count, &max_count)) {
            ESP_LOGW(TAG, "HEAD MODULE: tipo no soportado (%s), se ignora", module_name);
            continue;
        }

        parsed_counts.has_explicit_configuration = true;
        if (key != NULL) {
            app_trim_line(key);
        }

        if (key == NULL || value_text == NULL || strcasecmp(key, "COUNT") != 0) {
            *target_count = default_count;
            ESP_LOGW(TAG, "HEAD MODULE %s: formato invalido, se usa default %d", module_name, default_count);
            continue;
        }

        app_trim_line(value_text);
        if (!app_head_try_parse_non_negative_int(value_text, &parsed_count)) {
            *target_count = default_count;
            ESP_LOGW(TAG, "HEAD MODULE %s: COUNT invalido (%s), se usa default %d", module_name, value_text, default_count);
            continue;
        }

        if (parsed_count > max_count) {
            *target_count = max_count;
            ESP_LOGW(TAG, "MODULE %s COUNT=%d excede maximo %d. Se usara %d.",
                     module_name,
                     parsed_count,
                     max_count,
                     max_count);
            continue;
        }

        *target_count = parsed_count;
    }

    fclose(file);

    s_module_counts = parsed_counts;
    ESP_LOGI(TAG,
             "HEAD_MODULES|DEN=%d|SIC=%d|J=%d|YARN=%d|STITCH=%d",
             s_module_counts.den_count,
             s_module_counts.sic_count,
             s_module_counts.j_count,
             s_module_counts.yarn_count,
             s_module_counts.stitch_count);
}

static bool app_head_command_has_prefix(const char *line, const char *prefix)
{
    size_t len;
    char next;

    if (line == NULL || prefix == NULL) {
        return false;
    }

    len = strlen(prefix);
    if (strncasecmp(line, prefix, len) != 0) {
        return false;
    }

    next = line[len];
    return next == '\0' || next == ' ' || next == '|' || next == '\t';
}

/**
 * [POR QUE EXISTE]
 * Impide que un bloque TXT ejecute comandos de control global o transferencia de
 * archivos que no deben correr dentro de una accion de Cabezal.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_execute_allowed_line() antes de interpretar cada linea.
 *
 * [CUANDO SE EJECUTA]
 * Por cada linea dentro del bloque BEGIN|accion ... END.
 *
 * [ENTRADAS]
 * Recibe la linea ya recortada.
 *
 * [SALIDAS]
 * Devuelve true si la linea empieza con un comando prohibido.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea.
 *
 * [FLUJO ACURATEX]
 * TXT -> linea -> filtro de seguridad -> ejecucion permitida o error.
 *
 * [EQUIVALENCIA MCU]
 * Es una lista blanca/negra para evitar que una macro llame comandos de sistema.
 *
 * [SI NO EXISTIERA]
 * Un TXT podria intentar seleccionar otro programa, borrar archivos o lanzar otra
 * accion mientras ya se ejecuta una.
 */
static bool app_head_is_forbidden_script_command(const char *line)
{
    static const char *const forbidden[] = {
        "FILE_BEGIN",
        "FILE_DATA",
        "FILE_END",
        "FILE_DELETE",
        "FILE_GET",
        "FILE_GET_NEXT",
        "FILE_LIST",
        "FILE_SELECT",
        "HEAD_PROGRAM_SELECT",
        "HEAD_ACTION",
        "HEAD_STOP",
        "HEAD_STATUS",
        "SCRIPT_RUN",
        "SCRIPT_STOP",
        "SCRIPT_STATUS",
        "emergency_stop",
    };

    for (size_t i = 0; i < sizeof(forbidden) / sizeof(forbidden[0]); ++i) {
        if (app_head_command_has_prefix(line, forbidden[i])) {
            return true;
        }
    }

    return false;
}

/**
 * [POR QUE EXISTE]
 * Reconoce `WAIT` y `DELAY` dentro de un bloque TXT y extrae milisegundos.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_head_execute_allowed_line() y app_head_find_and_execute_action().
 *
 * [CUANDO SE EJECUTA]
 * Al procesar cada linea del bloque y al decidir si emitir progreso despues de
 * una linea.
 *
 * [ENTRADAS]
 * Recibe la linea y un puntero donde guardar milisegundos.
 *
 * [SALIDAS]
 * Devuelve true si la linea es WAIT/DELAY con valor decimal valido entre 0 y
 * APP_HEAD_MAX_WAIT_MS.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe `*milliseconds` si el formato es valido.
 *
 * [CONCURRENCIA]
 * No bloquea; solo parsea texto.
 *
 * [FLUJO ACURATEX]
 * TXT `WAIT 500` o `DELAY|500` -> milisegundos -> vTaskDelay por chunks.
 *
 * [EQUIVALENCIA MCU]
 * Es equivalente a interpretar delay(ms) desde un script.
 *
 * [SI NO EXISTIERA]
 * El TXT no podria pausar entre tramas CAN.
 */
static bool app_head_parse_wait_ms(const char *line, int *milliseconds)
{
    char buffer[APP_HEAD_MAX_LINE_LEN];
    char *saveptr = NULL;
    char *token = NULL;
    char *value_text = NULL;
    char *endptr = NULL;
    long parsed;

    if (line == NULL || milliseconds == NULL) {
        return false;
    }

    // [C/C++] strtok_r modifica el buffer, por eso se trabaja sobre copia.
    strlcpy(buffer, line, sizeof(buffer));
    // [ACURATEX] Se aceptan separadores espacio, tab o '|'.
    token = strtok_r(buffer, " \t|", &saveptr);
    if (token == NULL
        || (strcasecmp(token, "WAIT") != 0 && strcasecmp(token, "DELAY") != 0)) {
        return false;
    }

    value_text = strtok_r(NULL, " \t|", &saveptr);
    if (value_text == NULL || value_text[0] == '\0') {
        return false;
    }

    errno = 0;
    parsed = strtol(value_text, &endptr, 10);
    if (errno != 0 || endptr == value_text || *endptr != '\0' || parsed < 0 || parsed > APP_HEAD_MAX_WAIT_MS) {
        return false;
    }

    *milliseconds = (int)parsed;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Emite una linea de progreso mientras una accion larga se ejecuta.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_head_wait_cancelable() y app_head_find_and_execute_action().
 *
 * [CUANDO SE EJECUTA]
 * Durante WAIT/DELAY largos y despues de lineas no-WAIT ejecutadas.
 *
 * [ENTRADAS]
 * Recibe callback de respuesta, accion, numero de linea y tiempo de inicio.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado; envia respuesta/log.
 *
 * [CONCURRENCIA]
 * Corre en head_action_task y responde por el transporte capturado.
 *
 * [FLUJO ACURATEX]
 * Ejecucion TXT -> HEAD_PROGRESS -> app puede ver avance.
 *
 * [EQUIVALENCIA MCU]
 * Es como imprimir progreso por Serial mientras una rutina larga corre.
 *
 * [SI NO EXISTIERA]
 * La app no sabria en que linea va una accion lenta.
 */
static void app_head_emit_progress(app_reply_fn_t reply,
                                   void *ctx,
                                   const char *action,
                                   int line_number,
                                   long long start_ms)
{
    if (reply == NULL || action == NULL || action[0] == '\0')
    {
        return;
    }

    ESP_LOGI(TAG, "HEAD_PROGRESS|ACTION=%s|LINE=%d|ELAPSED=%lld",
             action,
             line_number,
             app_head_now_ms() - start_ms);
    (void)app_head_replyf(reply, ctx, "HEAD_PROGRESS|ACTION=%s|LINE=%d|ELAPSED=%lld",
                          action,
                          line_number,
                          app_head_now_ms() - start_ms);
}

/**
 * [POR QUE EXISTE]
 * Ejecuta WAIT/DELAY de manera cancelable, partiendo la espera en chunks.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_execute_allowed_line().
 *
 * [CUANDO SE EJECUTA]
 * Cuando el TXT contiene WAIT o DELAY con milisegundos mayores que cero.
 *
 * [ENTRADAS]
 * Recibe accion, linea, duracion, tiempo de inicio y callback de respuesta.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si termina la espera; ESP_ERR_INVALID_STATE si HEAD_STOP pide
 * cancelacion.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado directamente; lee s_stop_requested.
 *
 * [CONCURRENCIA]
 * Usa vTaskDelay() dentro de la tarea de accion. Revisa s_stop_requested antes
 * y despues de cada chunk para cancelar razonablemente rapido.
 *
 * [FLUJO ACURATEX]
 * TXT WAIT -> vTaskDelay por chunks -> progreso/cancelacion -> continuar o FAIL.
 *
 * [EQUIVALENCIA MCU]
 * Es un delay no bloqueante para el sistema, porque cede CPU a FreeRTOS.
 *
 * [SI NO EXISTIERA]
 * WAIT largo no podria cancelarse hasta terminar.
 */
static esp_err_t app_head_wait_cancelable(const char *action,
                                          int line_number,
                                          int wait_ms,
                                          long long start_ms,
                                          app_reply_fn_t reply,
                                          void *ctx)
{
    int remaining = wait_ms;
    int last_reported_ms = 0;

    while (remaining > 0) {
        // [ACURATEX] Chunk maximo 250 ms: conserva el tiempo total y permite
        // revisar cancelacion varias veces.
        int chunk = remaining > 250 ? 250 : remaining;

        if (s_stop_requested) {
            return ESP_ERR_INVALID_STATE;
        }

        vTaskDelay(pdMS_TO_TICKS(chunk));
        remaining -= chunk;
        last_reported_ms += chunk;

        if (s_stop_requested) {
            return ESP_ERR_INVALID_STATE;
        }

        if (last_reported_ms >= 1000 || remaining == 0) {
            app_head_emit_progress(reply, ctx, action, line_number, start_ms);
            last_reported_ms = 0;
        }
    }

    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Ejecuta una sola linea permitida dentro de un bloque BEGIN/END.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_find_and_execute_action() para cada linea del bloque activo.
 *
 * [CUANDO SE EJECUTA]
 * Durante la tarea head_action_task, linea por linea.
 *
 * [ENTRADAS]
 * Recibe la linea, numero de linea, accion, contexto dinamico J, tiempo de
 * inicio, callback de respuesta, entorno de comandos y buffer de error.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si la linea se ejecuto; devuelve error si el comando no es
 * soportado, falla CAN, falla seleccion de bus o se cancela.
 *
 * [ESTADO QUE MODIFICA]
 * Puede cambiar bus CAN mediante env->can_select_bus, transmitir CAN mediante
 * env->can_send_standard y, para J dinamico, confirmar estado J.
 *
 * [CONCURRENCIA]
 * Corre dentro de head_action_task. Puede bloquear por WAIT/DELAY o por el
 * driver CAN/callback de respuesta.
 *
 * [FLUJO ACURATEX]
 * BEGIN|accion -> linea TXT -> WAIT/can/status/send -> CAN o respuesta.
 *
 * [EQUIVALENCIA MCU]
 * Es el interprete de una instruccion de script de microcontrolador.
 *
 * [SI NO EXISTIERA]
 * El bloque TXT podria encontrarse, pero sus lineas no tendrian efecto.
 */
static esp_err_t app_head_execute_allowed_line(const char *line,
                                               int line_number,
                                               const char *action,
                                               app_head_j_dynamic_context_t *dynamic_ctx,
                                               long long start_ms,
                                               app_reply_fn_t reply,
                                               void *ctx,
                                               const app_command_env_t *env,
                                               char *error,
                                               size_t error_len)
{
    int wait_ms = 0;

    if (line == NULL || action == NULL || reply == NULL || env == NULL || error == NULL || error_len == 0) {
        return ESP_ERR_INVALID_ARG;
    }

    error[0] = '\0';

    if (s_stop_requested) {
        snprintf(error, error_len, "STOPPED");
        return ESP_ERR_INVALID_STATE;
    }

    // [ACURATEX] HEAD_* y FILE_* no son instrucciones validas dentro del TXT.
    if (app_head_is_forbidden_script_command(line)) {
        snprintf(error, error_len, "FORBIDDEN_COMMAND|LINE=%d", line_number);
        return ESP_ERR_INVALID_STATE;
    }

    if (app_head_parse_wait_ms(line, &wait_ms)) {
        if (wait_ms > 0) {
            ESP_LOGI(TAG, "HEAD_WAIT_BEGIN|MS=%d|TIME=%lld", wait_ms, app_head_now_ms());
            esp_err_t wait_err = app_head_wait_cancelable(action, line_number, wait_ms, start_ms, reply, ctx);
            if (wait_err != ESP_OK) {
                snprintf(error, error_len, "STOPPED");
                return wait_err;
            }
            ESP_LOGI(TAG, "HEAD_WAIT_END|MS=%d|TIME=%lld", wait_ms, app_head_now_ms());
        }
        return ESP_OK;
    }

    // [ACURATEX] `can1` y `can2` dentro del TXT cambian el bus logico antes de
    // futuras instrucciones send.
    if (strcasecmp(line, "can1") == 0 || strcasecmp(line, "can2") == 0) {
        int bus = strcasecmp(line, "can2") == 0 ? APP_CMD_CAN_BUS_2 : APP_CMD_CAN_BUS_1;
        const char *bus_name = bus == APP_CMD_CAN_BUS_2 ? "CAN2" : "CAN1";
        esp_err_t err = env->can_select_bus(bus);
        if (err != ESP_OK) {
            snprintf(error, error_len, "CAN_SELECT_FAILED|%s|CODE=%d", bus_name, (int)err);
            return err;
        }
        (void)app_head_reply_logged(reply, ctx, bus == APP_CMD_CAN_BUS_2 ? "OK CAN2" : "OK CAN1");
        return ESP_OK;
    }

    // [ACURATEX] `status` dentro del TXT no cambia estado; informa entorno al
    // host mientras la accion sigue viva.
    if (strcasecmp(line, "status") == 0) {
        (void)app_head_reply_status_line(reply, ctx, env);
        return ESP_OK;
    }

    // [ACURATEX] `send` convierte texto hex a trama CAN y transmite por TWAI.
    if (app_head_command_has_prefix(line, "send")) {
        const char *payload = line + 4;
        const char *send_source = line;
        uint32_t id = 0;
        uint8_t data[8] = {0};
        size_t len = 0;
        char send_line[APP_HEAD_MAX_LINE_LEN];
        char response[160];
        bool used_placeholder = false;

        while (*payload == ' ' || *payload == '\t' || *payload == '|') {
            payload++;
        }

        // [ACURATEX] Si la accion es J dinamica, el TXT puede tener un token
        // `XX` dentro de `send`. Ese token no es texto libre: se reemplaza por
        // el byte fisico candidato calculado desde el estado real de Jn.
        if (dynamic_ctx != NULL) {
            esp_err_t expand_err = app_head_expand_send_line(line,
                                                             dynamic_ctx,
                                                             send_line,
                                                             sizeof(send_line),
                                                             &used_placeholder,
                                                             error,
                                                             error_len);
            if (expand_err != ESP_OK) {
                return expand_err;
            }
            send_source = send_line;
            payload = send_source + 4;
            while (*payload == ' ' || *payload == '\t' || *payload == '|') {
                payload++;
            }
        }

        // [ACURATEX] app_parse_frame_line produce ID, data[] y len/DLC. En este
        // entorno los limites son CAN clasico: ID estandar hasta 0x7FF y DLC 8.
        if (!app_parse_frame_line(payload, &id, data, &len, env->can_max_frame_len, env->can_std_id_mask)) {
            snprintf(error, error_len, "FRAME_INVALID|LINE=%d", line_number);
            return ESP_ERR_INVALID_ARG;
        }

        {
            char data_hex[64] = {0};
            size_t offset = 0;
            for (size_t i = 0; i < len && offset + 3 < sizeof(data_hex); ++i) {
                int written = snprintf(data_hex + offset, sizeof(data_hex) - offset,
                                       i == 0 ? "%02X" : " %02X",
                                       data[i]);
                if (written < 0 || (size_t)written >= sizeof(data_hex) - offset) {
                    break;
                }
                offset += (size_t)written;
            }
            ESP_LOGI(TAG, "CAN_TX_BEGIN|ID=0x%03" PRIX32 "|DLC=%u|DATA=%s",
                     id,
                     (unsigned)len,
                     len > 0 ? data_hex : "");
        }

        // [ACURATEX] El bus activo viene de `can1`/`can2` dentro del TXT o del
        // entorno; si no hay seleccion explicita se conserva CAN1 por defecto.
        // El timeout real de transmision esta dentro de app_can_send_standard().
        esp_err_t err = env->can_send_standard(
            (env->active_bus == APP_CMD_CAN_BUS_NONE) ? APP_CMD_CAN_BUS_1 : env->active_bus,
            id,
            data,
            len);
        if (err != ESP_OK) {
            ESP_LOGE(TAG, "CAN_TX_RESULT|RESULT=%s|ID=0x%03" PRIX32,
                     esp_err_to_name(err),
                     id);
            snprintf(error, error_len, "CAN_TX_FAILED|CODE=%d", (int)err);
            return err;
        }

        // [ACURATEX] Commit J dinamico despues de enviar CAN correcto. Esto
        // evita que el estado interno diga que un bit cambio si la trama fallo.
        if (dynamic_ctx != NULL && dynamic_ctx->active && used_placeholder) {
            if (!app_head_state_manager_commit_j_physical_register(dynamic_ctx->instance,
                                                                   dynamic_ctx->candidate_physical)) {
                snprintf(error, error_len, "J_DYNAMIC_COMMIT_FAILED|J%u", (unsigned)dynamic_ctx->instance);
                return ESP_ERR_INVALID_STATE;
            }
            // [ACURATEX] `state_committed` deja rastro dentro de la tarea de
            // que el byte candidato ya paso a ser fuente de verdad.
            dynamic_ctx->state_committed = true;
            // [ACURATEX] Se desactiva para impedir otro commit por una segunda
            // linea send accidental dentro del mismo bloque.
            dynamic_ctx->active = false;
        }

        ESP_LOGI(TAG, "CAN_TX_RESULT|RESULT=OK|ID=0x%03" PRIX32 "|DLC=%u", id, (unsigned)len);
        snprintf(response, sizeof(response), "TX_OK bus=%s id=0x%03" PRIX32 " dlc=%u",
                 env->active_bus_name != NULL ? env->active_bus_name : "CAN1",
                 id,
                 (unsigned)len);
        (void)app_head_reply_logged(reply, ctx, response);
        return ESP_OK;
    }

    snprintf(error, error_len, "UNSUPPORTED_COMMAND|LINE=%d", line_number);
    return ESP_ERR_NOT_SUPPORTED;
}

/**
 * [POR QUE EXISTE]
 * Interpreta el encabezado `BEGIN|accion` y extrae opcionalmente `VALUE=`.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_find_and_execute_action() mientras busca el bloque objetivo.
 *
 * [CUANDO SE EJECUTA]
 * Por cada linea que podria ser inicio de bloque.
 *
 * [ENTRADAS]
 * Recibe linea, buffer de accion, banderas de valor y puntero de valor.
 *
 * [SALIDAS]
 * Devuelve true si la linea es BEGIN valido y llena action/has_value/value.
 *
 * [ESTADO QUE MODIFICA]
 * Solo escribe en buffers/punteros de salida.
 *
 * [CONCURRENCIA]
 * No usa estado global.
 *
 * [FLUJO ACURATEX]
 * TXT `BEGIN|J1.CH2|VALUE=1` -> accion `J1.CH2` -> bloque ejecutable.
 *
 * [EQUIVALENCIA MCU]
 * Es parsear la etiqueta de una rutina dentro de un archivo de script.
 *
 * [SI NO EXISTIERA]
 * El runner no podria saber donde empieza la accion pedida.
 */
static bool app_head_parse_begin_header(const char *line,
                                        char *action,
                                        size_t action_len,
                                        bool *has_value,
                                        int *value)
{
    char buffer[APP_HEAD_MAX_LINE_LEN];
    char *saveptr = NULL;
    char *token = NULL;
    char *action_token = NULL;

    if (line == NULL || action == NULL || action_len == 0 || has_value == NULL || value == NULL) {
        return false;
    }

    if (strncasecmp(line, "BEGIN|", 6) != 0) {
        return false;
    }

    strlcpy(buffer, line, sizeof(buffer));
    token = strtok_r(buffer, "|", &saveptr);
    if (token == NULL || strcasecmp(token, "BEGIN") != 0) {
        return false;
    }

    action_token = strtok_r(NULL, "|", &saveptr);
    if (action_token == NULL || action_token[0] == '\0') {
        return false;
    }

    app_trim_line(action_token);
    if (strlen(action_token) >= action_len) {
        return false;
    }

    strlcpy(action, action_token, action_len);
    *has_value = false;
    *value = 0;

    while ((token = strtok_r(NULL, "|", &saveptr)) != NULL) {
        app_trim_line(token);
        if (strncasecmp(token, "VALUE=", 6) == 0) {
            char *endptr = NULL;
            long parsed = strtol(token + 6, &endptr, 10);
            if (endptr != token + 6 && *endptr == '\0') {
                *has_value = true;
                *value = (int)parsed;
            }
        }
    }

    return true;
}

/**
 * [POR QUE EXISTE]
 * Busca en el programa TXT el bloque `BEGIN|target_action` y ejecuta sus lineas
 * hasta encontrar `END`.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_run_action_task().
 *
 * [CUANDO SE EJECUTA]
 * Dentro de la tarea FreeRTOS creada para HEAD_ACTION.
 *
 * [ENTRADAS]
 * Recibe archivo activo, accion objetivo, contexto J dinamico, tiempo de inicio,
 * callback de respuesta, entorno y estructura de resultado.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si encontro y ejecuto el bloque completo. Devuelve errores si
 * no existe el archivo, no existe la accion, falta END, hay BEGIN anidado o una
 * linea falla.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_current_line y etapas de diagnostico; llena `result`.
 *
 * [CONCURRENCIA]
 * Corre en head_action_task. Lee LittleFS con fopen()/fgets(), ejecuta CAN y
 * puede bloquear por WAIT/DELAY.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION -> abrir TXT -> buscar BEGIN|accion -> ejecutar lineas -> END ->
 * DONE/FAIL.
 *
 * [EQUIVALENCIA MCU]
 * Es como buscar una etiqueta en una tabla de comandos y ejecutar instrucciones
 * hasta una marca de fin.
 *
 * [SI NO EXISTIERA]
 * HEAD_ACTION no podria convertir una accion de la app en instrucciones CAN.
 */
static esp_err_t app_head_find_and_execute_action(const char *file_name,
                                                  const char *target_action,
                                                  app_head_j_dynamic_context_t *dynamic_ctx,
                                                  long long start_ms,
                                                  app_reply_fn_t reply,
                                                  void *ctx,
                                                  const app_command_env_t *env,
                                                  app_head_action_result_t *result)
{
    char path[96];
    char line[APP_HEAD_MAX_LINE_LEN];
    FILE *file;
    int line_number = 0;
    bool in_target_block = false;
    bool ended = false;

    if (result == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    memset(result, 0, sizeof(*result));

    app_head_set_stage("BUILD_PATH");
    (void)app_head_reply_logged(reply, ctx, "HEAD_TASK_STAGE|BUILD_PATH");
    if (!app_head_build_path(file_name, path, sizeof(path))) {
        snprintf(result->error, sizeof(result->error), "INVALID_PROGRAM");
        return ESP_ERR_INVALID_ARG;
    }

    app_head_set_stage("OPEN_FILE");
    (void)app_head_reply_logged(reply, ctx, "HEAD_TASK_STAGE|OPEN_FILE");
    // [LITTLEFS] El programa TXT se lee desde /fs en modo binario lectura.
    file = fopen(path, "rb");
    if (file == NULL) {
        snprintf(result->error, sizeof(result->error), "FILE_NOT_FOUND");
        return ESP_ERR_NOT_FOUND;
    }

    ESP_LOGI(TAG, "HEAD_FILE|%s/%s", APP_HEAD_FS_BASE, file_name);
    app_head_set_stage("FILE_OPEN_OK");
    (void)app_head_reply_logged(reply, ctx, "HEAD_TASK_STAGE|FILE_OPEN_OK");
    app_head_set_stage("SEARCH_BLOCK");
    (void)app_head_reply_logged(reply, ctx, "HEAD_TASK_STAGE|SEARCH_BLOCK");

    while (fgets(line, sizeof(line), file) != NULL) {
        char begin_action[APP_HEAD_MAX_ACTION_LEN + 1];
        bool begin_has_value = false;
        int begin_value = 0;

        line_number++;
        // [ACURATEX] Se limpia CR/LF y espacios antes de interpretar la linea.
        app_trim_line(line);

        // [ACURATEX] Lineas vacias y comentarios con # se ignoran.
        if (line[0] == '\0' || line[0] == '#') {
            continue;
        }

        if (!in_target_block) {
            // [ACURATEX] Antes de encontrar el bloque solo interesan BEGIN.
            if (app_head_parse_begin_header(
                    line,
                    begin_action,
                    sizeof(begin_action),
                    &begin_has_value,
                    &begin_value)
                && strcasecmp(begin_action, target_action) == 0) {
                result->found = true;
                result->has_value = begin_has_value;
                result->value = begin_value;
                in_target_block = true;
                ESP_LOGI(TAG, "HEAD_BLOCK_FOUND|%s", target_action);
                app_head_set_stage("BLOCK_FOUND");
                (void)app_head_reply_logged(reply, ctx, "HEAD_TASK_STAGE|BLOCK_FOUND");
            }
            continue;
        }

        // [ACURATEX] A partir de aqui estamos dentro del bloque objetivo.
        s_current_line = line_number;
        ESP_LOGI(TAG, "HEAD_LINE|%d|%s", line_number, line);
        app_head_set_stage("EXECUTE_LINE");
        (void)app_head_replyf(reply, ctx, "HEAD_TASK_STAGE|EXECUTE_LINE|LINE=%d|TEXT=%s", line_number, line);

        if (strcasecmp(line, "END") == 0) {
            // [ACURATEX] END termina correctamente la accion.
            ended = true;
            break;
        }

        if (strncasecmp(line, "BEGIN|", 6) == 0) {
            // [ACURATEX] No se permiten bloques anidados dentro de una accion.
            result->error_line = line_number;
            snprintf(result->error, sizeof(result->error), "NESTED_BEGIN");
            fclose(file);
            return ESP_ERR_INVALID_STATE;
        }

        esp_err_t err = app_head_execute_allowed_line(
            line,
            line_number,
            target_action,
            dynamic_ctx,
            start_ms,
            reply,
            ctx,
            env,
            result->error,
            sizeof(result->error));
        if (err != ESP_OK) {
            result->error_line = line_number;
            fclose(file);
            return err;
        }

        {
            int wait_ms_dummy = 0;
            // [ACURATEX] WAIT/DELAY ya emite progreso propio; otras lineas
            // emiten progreso al terminar.
            if (!app_head_parse_wait_ms(line, &wait_ms_dummy)) {
                app_head_emit_progress(reply, ctx, target_action, line_number, start_ms);
            }
        }
    }

    fclose(file);

    if (!result->found) {
        // [ACURATEX] La accion pedida no existe en el TXT activo.
        snprintf(result->error, sizeof(result->error), "NOT_FOUND");
        return ESP_ERR_NOT_FOUND;
    }

    if (!ended) {
        // [ACURATEX] Encontrar BEGIN sin END es error de formato del TXT.
        result->error_line = line_number;
        snprintf(result->error, sizeof(result->error), "MISSING_END");
        return ESP_ERR_INVALID_STATE;
    }

    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Libera recursos asociados a los argumentos de la tarea HEAD_ACTION.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_run_action_task() antes de liberar `args`.
 *
 * [CUANDO SE EJECUTA]
 * Al terminar la tarea, sea DONE, CANCELLED o FAILED.
 *
 * [ENTRADAS]
 * Recibe puntero a app_head_runner_task_args_t.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Puede liberar reply_ctx si era copia propia y marcarlo como no poseido.
 *
 * [CONCURRENCIA]
 * Corre dentro de head_action_task.
 *
 * [FLUJO ACURATEX]
 * Tarea termina -> liberar contexto TCP clonado -> free(args).
 *
 * [EQUIVALENCIA MCU]
 * Es limpieza de memoria reservada para una ejecucion asincrona.
 *
 * [SI NO EXISTIERA]
 * Las respuestas TCP diferidas podrian filtrar memoria.
 */
static void app_head_release_task_args(app_head_runner_task_args_t *args)
{
    if (args == NULL)
    {
        return;
    }

    if (args->reply_ctx_owned && args->reply_ctx != NULL && args->env.reply_ctx_release != NULL)
    {
        args->env.reply_ctx_release(args->reply_ctx);
        args->reply_ctx = NULL;
        args->reply_ctx_owned = false;
    }
}

typedef enum {
    APP_HEAD_TASK_OUTCOME_DONE = 0,
    APP_HEAD_TASK_OUTCOME_CANCELLED,
    APP_HEAD_TASK_OUTCOME_FAILED,
} app_head_task_outcome_t;

/**
 * [POR QUE EXISTE]
 * Cierra una accion HEAD_ACTION actualizando estado y emitiendo respuestas
 * finales coherentes.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_run_action_task() en su bloque cleanup.
 *
 * [CUANDO SE EJECUTA]
 * Una vez por accion, despues de ejecutar o fallar el bloque TXT.
 *
 * [ENTRADAS]
 * Recibe args de la tarea, resultado del interprete, error esp_err_t y outcome.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_runner_state, s_current_line, s_last_error, s_current_action,
 * s_action_start_ms, s_runner_task_handle y s_stop_requested.
 *
 * [CONCURRENCIA]
 * Toma s_state_mutex para modificar estado compartido. Luego responde por el
 * transporte capturado.
 *
 * [FLUJO ACURATEX]
 * Ejecucion TXT -> DONE/CANCELLED/ERROR -> OK|HEAD_ACTION o HEAD_ACTION_FAIL.
 *
 * [EQUIVALENCIA MCU]
 * Es el epilogo de una maquina de estados de accion.
 *
 * [SI NO EXISTIERA]
 * El estado quedaria RUNNING o la app no recibiria DONE/FAIL/CANCELLED.
 */
static void app_head_cleanup_task(app_head_runner_task_args_t *args,
                                  const app_head_action_result_t *result,
                                  esp_err_t execute_err,
                                  app_head_task_outcome_t outcome)
{
    const char *action;
    const char *state_name;
    const char *result_name;

    if (args == NULL) {
        return;
    }

    action = args->action;
    result_name = outcome == APP_HEAD_TASK_OUTCOME_DONE
        ? "DONE"
        : (outcome == APP_HEAD_TASK_OUTCOME_CANCELLED ? "CANCELLED" : "ERROR");
    state_name = outcome == APP_HEAD_TASK_OUTCOME_DONE
        ? "DONE"
        : (outcome == APP_HEAD_TASK_OUTCOME_CANCELLED ? "IDLE" : "ERROR");

    app_head_set_stage("HEAD_TASK_CLEANUP_BEGIN");
    ESP_LOGI(TAG, "HEAD_TASK_CLEANUP_BEGIN|RESULT=%s|ACTION=%s", result_name, action);
    (void)app_head_replyf(args->reply, args->reply_ctx, "HEAD_TASK_CLEANUP_BEGIN|RESULT=%s", result_name);

    app_head_state_lock();
    if (outcome == APP_HEAD_TASK_OUTCOME_DONE) {
        // [ACURATEX] DONE deja el runner en DONE y limpia accion/linea.
        s_runner_state = APP_HEAD_RUNNER_DONE;
        s_current_line = 0;
        s_last_error[0] = '\0';
        s_current_action[0] = '\0';
    } else if (outcome == APP_HEAD_TASK_OUTCOME_CANCELLED) {
        // [ACURATEX] Cancelacion vuelve a IDLE y no reporta fallo de script.
        s_runner_state = APP_HEAD_RUNNER_IDLE;
        s_current_line = 0;
        s_last_error[0] = '\0';
        s_current_action[0] = '\0';
    } else {
        // [ACURATEX] Error conserva linea/error para HEAD_STATUS.
        s_runner_state = APP_HEAD_RUNNER_ERROR;
        s_current_line = result != NULL && result->error_line > 0 ? result->error_line : s_current_line;
        if (result != NULL && result->error[0] != '\0') {
            strlcpy(s_last_error, result->error, sizeof(s_last_error));
        } else if (execute_err != ESP_OK) {
            snprintf(s_last_error, sizeof(s_last_error), "EXEC|CODE=%d", (int)execute_err);
        }
        s_current_action[0] = '\0';
    }
    s_action_start_ms = 0;
    s_runner_task_handle = NULL;
    s_stop_requested = false;
    app_head_state_unlock();

    if (outcome == APP_HEAD_TASK_OUTCOME_DONE) {
        if (strcasecmp(action, "INIT") == 0) {
            (void)app_head_replyf(args->reply, args->reply_ctx, "HEAD_INIT_DONE|TIME=%lld", app_head_now_ms());
        }
        // [ACURATEX] OK|HEAD_ACTION confirma que el firmware acepta la accion
        // como terminada correctamente para el protocolo de comandos.
        (void)app_head_replyf(args->reply, args->reply_ctx, "OK|HEAD_ACTION|%s", action);
        // [ACURATEX] HEAD_ACTION_DONE marca el cierre de la tarea de ejecucion.
        // No es lo mismo que 427: DONE cierra accion, 427 actualiza estado/UI.
        (void)app_head_replyf(args->reply, args->reply_ctx, "HEAD_ACTION_DONE|%s", action);
        if (!args->dynamic_ctx.state_committed) {
            // [ACURATEX] Si J dinamico ya hizo commit con XX, no se aplica otra
            // vez. Las acciones no dinamicas pueden actualizar estado aqui.
            (void)app_head_state_manager_apply_successful_action(action);
        }
        // [ACURATEX] Evento 427 opcional: solo se emite si la accion puede
        // traducirse a CH/POS/RUN segun app_head_emit_state_event().
        app_head_emit_state_event(action, result != NULL && result->has_value, result != NULL ? result->value : 0, args->reply, args->reply_ctx);
    } else if (outcome == APP_HEAD_TASK_OUTCOME_CANCELLED) {
        // [ACURATEX] CANCELLED significa que HEAD_STOP o STOPPED corto la accion;
        // no emite 427 porque no hay estado final confirmado por la accion.
        (void)app_head_replyf(args->reply, args->reply_ctx, "HEAD_ACTION_CANCELLED|%s", action);
    } else {
        const char *error_text = (result != NULL && result->error[0] != '\0')
            ? result->error
            : (execute_err == ESP_OK ? "EXEC" : esp_err_to_name(execute_err));

        if (result != NULL && result->error_line > 0) {
            (void)app_head_replyf(args->reply, args->reply_ctx, "ERR|HEAD_ACTION|LINE|%d|%s", result->error_line, error_text);
        } else {
            (void)app_head_replyf(args->reply, args->reply_ctx, "ERR|HEAD_ACTION|%s", error_text);
        }
        // [ACURATEX] FAIL informa accion y causa; se conserva separado de
        // ERR|HEAD_ACTION para que la app distinga error inmediato y cierre.
        (void)app_head_replyf(args->reply, args->reply_ctx, "HEAD_ACTION_FAIL|%s|%s", action, error_text);
    }

    app_head_set_stage("HEAD_TASK_CLEANUP_DONE");
    ESP_LOGI(TAG, "HEAD_TASK_CLEANUP_DONE|STATE=%s|ACTION=%s", state_name, action);
    (void)app_head_replyf(args->reply, args->reply_ctx, "HEAD_TASK_CLEANUP_DONE|STATE=%s", state_name);
}

/**
 * [POR QUE EXISTE]
 * Ejecuta una accion de Cabezal en una tarea FreeRTOS separada del transporte
 * que recibio HEAD_ACTION.
 *
 * [QUIEN LA LLAMA]
 * La ejecuta FreeRTOS despues de xTaskCreate() en app_head_run_action().
 *
 * [CUANDO SE EJECUTA]
 * Al recibir HEAD_ACTION valido y haber un programa activo.
 *
 * [ENTRADAS]
 * Recibe app_head_runner_task_args_t por `parameter`.
 *
 * [SALIDAS]
 * No devuelve al llamador; termina con vTaskDelete(NULL).
 *
 * [ESTADO QUE MODIFICA]
 * A traves de app_head_find_and_execute_action() y app_head_cleanup_task()
 * actualiza linea actual, estado, errores y respuestas DONE/FAIL.
 *
 * [CONCURRENCIA]
 * Corre concurrentemente con app_main/TCP. Usa s_state_mutex para estado y
 * vTaskDelete(NULL) para terminar la tarea actual.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION -> xTaskCreate -> abrir TXT -> BEGIN/END -> CAN/WAIT -> cleanup.
 *
 * [EQUIVALENCIA MCU]
 * Es una rutina larga ejecutada fuera del loop principal para no bloquear la
 * recepcion de comandos.
 *
 * [SI NO EXISTIERA]
 * HEAD_ACTION tendria que ejecutar el TXT en el mismo contexto del transporte y
 * bloquearia USB/TCP durante toda la accion.
 */
static void app_head_run_action_task(void *parameter)
{
    // [C/C++] FreeRTOS entrega void*. Se castea al tipo real de argumentos.
    app_head_runner_task_args_t *args = (app_head_runner_task_args_t *)parameter;
    app_head_action_result_t result = {};
    esp_err_t err = ESP_OK;
    const char *action;
    app_head_runner_state_t state_snapshot;
    int stack_free = 0;
    BaseType_t core = -1;

    if (args == NULL)
    {
        // [FREERTOS] NULL indica que la tarea actual se elimina a si misma.
        vTaskDelete(NULL);
        return;
    }

    action = args->action;
    core = xPortGetCoreID();
    // [FREERTOS] uxTaskGetStackHighWaterMark ayuda a diagnosticar margen de
    // stack disponible durante la accion.
    stack_free = (int)uxTaskGetStackHighWaterMark(NULL);
    app_head_set_stage("ENTER");
    ESP_LOGI(TAG,
             "HEAD_TASK_ENTER|ACTION=%s|PROGRAM=%s|CORE=%d|STACK_FREE=%d",
             action,
             args->program,
             (int)core,
             stack_free);
    (void)app_head_replyf(args->reply,
                          args->reply_ctx,
                          "HEAD_TASK_ENTER|ACTION=%s|PROGRAM=%s|CORE=%d|STACK_FREE=%d",
                          action,
                          args->program,
                          (int)core,
                          stack_free);

    app_head_set_stage("LOCK_STATE");
    (void)app_head_reply_logged(args->reply, args->reply_ctx, "HEAD_TASK_STAGE|LOCK_STATE");
    app_head_state_lock();
    state_snapshot = s_runner_state;
    app_head_state_unlock();
    if (state_snapshot != APP_HEAD_RUNNER_RUNNING) {
        err = ESP_FAIL;
        snprintf(result.error, sizeof(result.error), "RUNNER_STATE=%s", app_head_state_name(state_snapshot));
        goto cleanup;
    }

    err = app_head_find_and_execute_action(
        args->program,
        action,
        &args->dynamic_ctx,
        args->start_ms,
        args->reply,
        args->reply_ctx,
        &args->env,
        &result);

cleanup:
    app_head_cleanup_task(
        args,
        &result,
        err,
        err == ESP_OK
            ? APP_HEAD_TASK_OUTCOME_DONE
            : (s_stop_requested || strcmp(result.error, "STOPPED") == 0 || s_runner_state == APP_HEAD_RUNNER_STOPPING
                ? APP_HEAD_TASK_OUTCOME_CANCELLED
                : APP_HEAD_TASK_OUTCOME_FAILED));

    app_head_release_task_args(args);
    free(args);
    // [FREERTOS] La tarea de accion termina aqui despues de limpiar estado.
    vTaskDelete(NULL);
}

static bool app_head_split_action(const char *action,
                                  char *instance,
                                  size_t instance_len,
                                  char *type,
                                  size_t type_len)
{
    const char *dot = NULL;
    size_t left_len;

    if (action == NULL || instance == NULL || type == NULL) {
        return false;
    }

    dot = strchr(action, '.');
    if (dot == NULL || dot == action || dot[1] == '\0') {
        return false;
    }

    left_len = (size_t)(dot - action);
    if (left_len >= instance_len || strlen(dot + 1) >= type_len) {
        return false;
    }

    memcpy(instance, action, left_len);
    instance[left_len] = '\0';
    strlcpy(type, dot + 1, type_len);
    return true;
}

static bool app_head_parse_number_suffix(const char *text, const char *prefix, int *number)
{
    const char *value_text = NULL;
    char *endptr = NULL;
    long parsed;
    size_t prefix_len;

    if (text == NULL || prefix == NULL || number == NULL) {
        return false;
    }

    prefix_len = strlen(prefix);
    if (strncasecmp(text, prefix, prefix_len) != 0) {
        return false;
    }

    value_text = text + prefix_len;
    if (value_text[0] == '\0') {
        return false;
    }

    parsed = strtol(value_text, &endptr, 10);
    if (endptr == value_text || *endptr != '\0' || parsed <= 0 || parsed > 31) {
        return false;
    }

    *number = (int)parsed;
    return true;
}

static bool app_head_instance_starts_with(const char *instance, const char *prefix)
{
    return instance != NULL
        && prefix != NULL
        && strncasecmp(instance, prefix, strlen(prefix)) == 0;
}

static bool app_head_is_instance_enabled(const char *instance)
{
    int instance_index = 0;

    if (app_head_parse_number_suffix(instance, "DEN", &instance_index)) {
        return instance_index <= s_module_counts.den_count;
    }

    if (app_head_parse_number_suffix(instance, "SIC", &instance_index)) {
        return instance_index <= s_module_counts.sic_count;
    }

    if (app_head_parse_number_suffix(instance, "J", &instance_index)) {
        return instance_index <= s_module_counts.j_count;
    }

    if (app_head_parse_number_suffix(instance, "YARN", &instance_index)) {
        return instance_index <= s_module_counts.yarn_count;
    }

    if (app_head_parse_number_suffix(instance, "STITCH", &instance_index)) {
        return instance_index <= s_module_counts.stitch_count;
    }

    return true;
}

/**
 * [POR QUE EXISTE]
 * Detecta si HEAD_ACTION corresponde al caso dinamico `Jx.CHy`.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_run_action() antes de crear la tarea HEAD_ACTION.
 *
 * [CUANDO SE EJECUTA]
 * Una vez por accion, despues de limpiar el texto recibido desde la app.
 *
 * [ENTRADAS]
 * Recibe la accion textual y un puntero al contexto dinamico que debe llenar.
 *
 * [SALIDAS]
 * Devuelve true si es J valido con CH valido y hay registro fisico disponible.
 *
 * [ESTADO QUE MODIFICA]
 * No cambia todavia el estado global; solo llena dynamic_ctx con previo y
 * candidato.
 *
 * [CONCURRENCIA]
 * Lee el registro J mediante head_state_manager, que protege la lectura con
 * mutex. No bloquea por CAN ni por archivo.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION|J1.CH2 -> leer J1 fisico -> XOR bit CH2 -> candidato para XX.
 *
 * [EQUIVALENCIA MCU]
 * Es preparar el valor de salida de un puerto antes de escribirlo.
 *
 * [SI NO EXISTIERA]
 * `XX` no sabria que byte insertar y Jx.CHy no podria ser dinamico.
 */
static bool app_head_parse_dynamic_j_action(const char *action, app_head_j_dynamic_context_t *dynamic_ctx)
{
    char instance[32];
    char action_type[32];
    int instance_number = 0;
    int channel_number = 0;
    uint8_t previous_physical = 0xFF;

    if (action == NULL || dynamic_ctx == NULL) {
        return false;
    }

    // [C/C++] Se limpia todo el contexto para que un fallo de parseo no deje
    // banderas viejas activas.
    memset(dynamic_ctx, 0, sizeof(*dynamic_ctx));

    // [ACURATEX] Espera el formato con punto: instancia `J1` y accion `CH2`.
    if (!app_head_split_action(action, instance, sizeof(instance), action_type, sizeof(action_type))) {
        return false;
    }

    // [ACURATEX] Solo los modulos J usan esta ruta dinamica; otros modulos se
    // ejecutan como TXT normal.
    if (!app_head_parse_number_suffix(instance, "J", &instance_number)) {
        return false;
    }

    // [ACURATEX] Solo CHn usa `XX`. RUN/STOP/ON_ALL/OFF_ALL tienen otras rutas.
    if (!app_head_parse_number_suffix(action_type, "CH", &channel_number)) {
        return false;
    }

    if (instance_number <= 0
        || instance_number > s_module_counts.j_count
        || channel_number <= 0
        || channel_number > 8) {
        return false;
    }

    // [ACURATEX] Fuente de verdad antes de tocar el TXT. Ejemplo: J1 fisico FF
    // significa logico 00; alternar CH1 produce FE.
    if (!app_head_state_manager_get_j_physical_register((uint8_t)instance_number, &previous_physical)) {
        return false;
    }

    dynamic_ctx->active = true;
    dynamic_ctx->state_committed = false;
    dynamic_ctx->instance = (uint8_t)instance_number;
    dynamic_ctx->channel = (uint8_t)channel_number;
    dynamic_ctx->previous_physical = previous_physical;
    // [ACURATEX] Toggle real de CHn: XOR cambia solo el bit del canal. Por ser
    // activo-bajo, FF ^ 01 = FE equivale a logico 01.
    dynamic_ctx->candidate_physical = (uint8_t)(previous_physical ^ (uint8_t)(1U << (channel_number - 1)));
    return true;
}

/**
 * [POR QUE EXISTE]
 * Sustituye el token `XX` dentro de una linea `send` por el byte dinamico J.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_execute_allowed_line() justo antes de parsear y enviar CAN.
 *
 * [CUANDO SE EJECUTA]
 * Para cada linea `send` de un bloque TXT cuando la accion es Jx.CHy.
 *
 * [ENTRADAS]
 * Recibe la linea original, el contexto J, buffer de salida, bandera de uso y
 * buffer de error.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si genero una linea valida; devuelve error si XX esta en ID,
 * aparece sin accion J, aparece mas de una vez o no cabe.
 *
 * [ESTADO QUE MODIFICA]
 * No cambia estado global; solo produce la linea expandida y marca
 * used_placeholder.
 *
 * [CONCURRENCIA]
 * Trabaja con buffers locales de la tarea HEAD_ACTION. No toma mutex.
 *
 * [FLUJO ACURATEX]
 * send 320 1D 00 XX -> send 320 1D 00 FE -> parser CAN -> TWAI.
 *
 * [EQUIVALENCIA MCU]
 * Es como resolver un macro de ensamblador antes de emitir bytes.
 *
 * [SI NO EXISTIERA]
 * El TXT tendria que contener bytes fijos y no podria depender del estado real.
 */
static esp_err_t app_head_expand_send_line(const char *line,
                                           const app_head_j_dynamic_context_t *dynamic_ctx,
                                           char *expanded_line,
                                           size_t expanded_line_len,
                                           bool *used_placeholder,
                                           char *error,
                                           size_t error_len)
{
    char buffer[APP_HEAD_MAX_LINE_LEN];
    char *saveptr = NULL;
    char *token = NULL;
    size_t used = 0;
    int token_index = 0;
    bool placeholder_found = false;
    char replacement[8];

    if (used_placeholder != NULL) {
        *used_placeholder = false;
    }
    if (error != NULL && error_len > 0) {
        error[0] = '\0';
    }

    if (line == NULL || expanded_line == NULL || expanded_line_len == 0) {
        return ESP_ERR_INVALID_ARG;
    }

    if (dynamic_ctx != NULL && dynamic_ctx->active) {
        // [C/C++] `%02X` convierte el byte a dos digitos hexadecimales, igual
        // al formato que app_parse_frame_line espera en `send`.
        snprintf(replacement, sizeof(replacement), "%02X", dynamic_ctx->candidate_physical);
    } else {
        replacement[0] = '\0';
    }

    // [C/C++] strtok_r modifica el buffer recibido, por eso se trabaja sobre
    // una copia y no sobre la linea original del TXT.
    strlcpy(buffer, line, sizeof(buffer));
    token = strtok_r(buffer, " \t|", &saveptr);
    while (token != NULL) {
        const char *out_token = token;
        size_t token_len = 0;

        if (strcasecmp(token, "XX") == 0) {
            if (token_index == 1) {
                // [ACURATEX] token_index 1 corresponde al ID CAN despues del
                // verbo `send`; el protocolo no permite cambiar dinamicamente
                // el identificador.
                if (error != NULL && error_len > 0) {
                    snprintf(error, error_len, "J_DYNAMIC_XX_IN_ID");
                }
                return ESP_ERR_INVALID_STATE;
            }

            if (dynamic_ctx == NULL || !dynamic_ctx->active) {
                // [ACURATEX] XX solo tiene sentido dentro de Jx.CHy. Fuera de
                // ese contexto seria ambiguo.
                if (error != NULL && error_len > 0) {
                    snprintf(error, error_len, "J_DYNAMIC_XX_OUTSIDE_J");
                }
                return ESP_ERR_INVALID_STATE;
            }

            if (placeholder_found) {
                // [ACURATEX] Se acepta un solo XX para que haya un unico commit
                // de estado asociado a una unica escritura de registro.
                if (error != NULL && error_len > 0) {
                    snprintf(error, error_len, "J_DYNAMIC_MULTIPLE_XX");
                }
                return ESP_ERR_INVALID_STATE;
            }

            // [ACURATEX] Desde este punto la linea de salida ya contiene el
            // candidato fisico, no el texto `XX`.
            out_token = replacement;
            placeholder_found = true;
            if (used_placeholder != NULL) {
                *used_placeholder = true;
            }
        }

        token_len = strlen(out_token);
        if (used > 0) {
            if (used + 1 >= expanded_line_len) {
                if (error != NULL && error_len > 0) {
                    snprintf(error, error_len, "J_DYNAMIC_SEND_TOO_LONG");
                }
                return ESP_ERR_INVALID_SIZE;
            }
            // [ACURATEX] Normaliza separadores del TXT a espacios para que el
            // parser CAN reciba una linea estable.
            expanded_line[used++] = ' ';
        }

        if (used + token_len >= expanded_line_len) {
            if (error != NULL && error_len > 0) {
                snprintf(error, error_len, "J_DYNAMIC_SEND_TOO_LONG");
            }
            return ESP_ERR_INVALID_SIZE;
        }

        memcpy(expanded_line + used, out_token, token_len);
        used += token_len;
        expanded_line[used] = '\0';
        token_index++;
        token = strtok_r(NULL, " \t|", &saveptr);
    }

    if (!placeholder_found && dynamic_ctx != NULL && dynamic_ctx->active) {
        if (used_placeholder != NULL) {
            *used_placeholder = false;
        }
    }

    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Construye una mascara binaria textual para mensajes 427.
 *
 * [QUIEN LA LLAMA]
 * app_head_emit_state_event().
 *
 * [CUANDO SE EJECUTA]
 * Al reportar POSn o CHn hacia la app.
 *
 * [ENTRADAS]
 * Numero de bit en base 1, ancho de mascara y buffer de salida.
 *
 * [SALIDAS]
 * Devuelve true si pudo escribir una cadena tipo `0b00000010`.
 *
 * [ESTADO QUE MODIFICA]
 * Solo escribe el buffer de salida.
 *
 * [CONCURRENCIA]
 * No usa estado compartido.
 *
 * [FLUJO ACURATEX]
 * accion J1.CH2 -> mascara 0b00000010 -> 427|J1|CH|...|420.
 *
 * [EQUIVALENCIA MCU]
 * Es mostrar un bit de registro en forma legible.
 *
 * [SI NO EXISTIERA]
 * La app no recibiria la mascara textual esperada para 427.
 */
static bool app_head_build_binary_mask(int bit_number, int width, char *mask, size_t mask_len)
{
    uint32_t value;
    size_t required;

    if (bit_number <= 0 || bit_number > width || width <= 0 || width > 16 || mask == NULL) {
        return false;
    }

    required = (size_t)width + 3;
    if (mask_len < required) {
        return false;
    }

    // [ACURATEX] El protocolo habla de bits en base 1; C desplaza en base 0.
    value = 1U << (bit_number - 1);
    mask[0] = '0';
    mask[1] = 'b';
    for (int i = width - 1; i >= 0; --i) {
        // [ACURATEX] Se escribe de MSB a LSB para que la mascara tenga lectura
        // convencional de izquierda a derecha.
        mask[2 + (width - 1 - i)] = (value & (1U << i)) ? '1' : '0';
    }
    mask[2 + width] = '\0';
    return true;
}

/**
 * [POR QUE EXISTE]
 * Agrega texto formateado al buffer acumulado de HEAD_STATUS.
 *
 * [QUIEN LA LLAMA]
 * app_head_status() y app_head_append_j_status_fields().
 *
 * [CUANDO SE EJECUTA]
 * Al construir una respuesta HEAD_STATUS grande por partes.
 *
 * [ENTRADAS]
 * Buffer, tamano, contador `used` y formato tipo printf.
 *
 * [SALIDAS]
 * Devuelve true si el texto cupo completo.
 *
 * [ESTADO QUE MODIFICA]
 * Modifica el buffer y el contador `*used`.
 *
 * [CONCURRENCIA]
 * Usa buffers locales de HEAD_STATUS; no toca estado global.
 *
 * [FLUJO ACURATEX]
 * snapshot -> append campos -> respuesta HEAD_STATUS.
 *
 * [EQUIVALENCIA MCU]
 * Es construir una linea de diagnostico en un buffer fijo.
 *
 * [SI NO EXISTIERA]
 * HEAD_STATUS tendria snprintf repetidos y mas riesgo de overflow.
 */
static bool app_head_append_status_line(char *buffer,
                                        size_t buffer_size,
                                        size_t *used,
                                        const char *format,
                                        ...)
{
    va_list args;
    int written;

    if (buffer == NULL || used == NULL || *used >= buffer_size) {
        return false;
    }

    // [C/C++] va_list permite reutilizar una funcion para distintos fragmentos
    // sin cambiar el formato del protocolo.
    va_start(args, format);
    written = vsnprintf(buffer + *used, buffer_size - *used, format, args);
    va_end(args);

    if (written < 0 || (size_t)written >= (buffer_size - *used)) {
        ESP_LOGW(TAG,
                 "HEAD_STATUS_BUILD_TRUNCATED|CAPACITY=%u|USED=%u|REQUESTED=%d",
                 (unsigned)buffer_size,
                 (unsigned)*used,
                 written);
        return false;
    }

    // [ACURATEX] `used` mantiene el cursor de escritura dentro del buffer.
    *used += (size_t)written;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Agrega al HEAD_STATUS los campos resumidos de cada J.
 *
 * [QUIEN LA LLAMA]
 * app_head_status().
 *
 * [CUANDO SE EJECUTA]
 * Cuando la app pide HEAD_STATUS.
 *
 * [ENTRADAS]
 * Buffer de respuesta, contador de bytes usados y cantidad J declarada.
 *
 * [SALIDAS]
 * Devuelve false si no pudo leer estado o si el buffer no alcanza.
 *
 * [ESTADO QUE MODIFICA]
 * No cambia J; solo lee snapshots.
 *
 * [CONCURRENCIA]
 * Cada J se lee mediante head_state_manager con mutex.
 *
 * [FLUJO ACURATEX]
 * HEAD_STATUS -> |J1=FF|J1R=0|... -> app.
 *
 * [EQUIVALENCIA MCU]
 * Es serializar registros de estado hacia una terminal.
 *
 * [SI NO EXISTIERA]
 * HEAD_STATUS no informaria byte fisico ni RUN de J.
 */
static bool app_head_append_j_status_fields(char *buffer,
                                            size_t buffer_size,
                                            size_t *used,
                                            int j_count)
{
    for (int i = 0; i < j_count; ++i) {
        uint8_t physical = 0xFF;
        bool running = false;

        // [ACURATEX] Se consulta J en base 1 porque esa es la numeracion del
        // protocolo, aunque el bucle interno usa i base 0.
        if (!app_head_state_manager_get_j_status((uint8_t)(i + 1), &physical, &running)) {
            return false;
        }

        if (!app_head_append_status_line(buffer,
                                         buffer_size,
                                         used,
                                         "|J%d=%02X|J%dR=%d",
                                         i + 1,
                                         physical,
                                         i + 1,
                                         running ? 1 : 0)) {
            return false;
        }
    }

    return true;
}

/**
 * [POR QUE EXISTE]
 * Convierte una accion completada en telemetria compacta `427|...|420`.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_run_action_task() al cerrar una accion.
 *
 * [CUANDO SE EJECUTA]
 * Despues de HEAD_ACTION_DONE/FAIL cuando hay accion que puede mapear a estado.
 *
 * [ENTRADAS]
 * Accion, valor opcional extraido del TXT y callback de respuesta.
 *
 * [SALIDAS]
 * Devuelve true si emitio algun 427.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado; solo notifica a la app.
 *
 * [CONCURRENCIA]
 * Corre en la tarea HEAD_ACTION y usa el callback de respuesta existente.
 *
 * [FLUJO ACURATEX]
 * J1.CH2 terminado -> 427|J1|CH|0b00000010|420.
 *
 * [EQUIVALENCIA MCU]
 * Es publicar un evento de estado despues de una rutina.
 *
 * [SI NO EXISTIERA]
 * La app podria recibir DONE pero no el evento 427 que actualiza la UI.
 */
static bool app_head_emit_state_event(const char *action,
                                      bool has_value,
                                      int value,
                                      app_reply_fn_t reply,
                                      void *ctx)
{
    char instance[32];
    char action_type[32];
    char mask[24];
    int number = 0;
    int width = 0;

    if (!app_head_split_action(action, instance, sizeof(instance), action_type, sizeof(action_type))) {
        return false;
    }

    if (app_head_parse_number_suffix(action_type, "POS", &number)) {
        // [ACURATEX] POS usa anchos distintos segun modulo. Ese ancho define la
        // cantidad de bits que la app espera en la mascara 427.
        if (app_head_instance_starts_with(instance, "DEN")) {
            width = 5;
        } else if (app_head_instance_starts_with(instance, "SIC")) {
            width = 3;
        } else if (app_head_instance_starts_with(instance, "STITCH")) {
            width = 4;
        } else {
            return false;
        }

        if (!app_head_build_binary_mask(number, width, mask, sizeof(mask))) {
            return false;
        }

        if (has_value) {
            // [ACURATEX] VALUE aparece solo cuando el bloque TXT extrajo un
            // resultado numerico asociado a la posicion.
            app_head_replyf(reply, ctx, "427|%s|POS|%s|VALUE=%d|420", instance, mask, value);
        } else {
            app_head_replyf(reply, ctx, "427|%s|POS|%s|420", instance, mask);
        }
        return true;
    }

    if (app_head_parse_number_suffix(action_type, "CH", &number)) {
        if (!app_head_instance_starts_with(instance, "J")
            && !app_head_instance_starts_with(instance, "YARN")) {
            return false;
        }

        // [ACURATEX] Para J y YARN los canales reportados al host tienen ancho
        // fijo de 8 bits.
        if (!app_head_build_binary_mask(number, 8, mask, sizeof(mask))) {
            return false;
        }

        app_head_replyf(reply, ctx, "427|%s|CH|%s|420", instance, mask);
        return true;
    }

    if (strcasecmp(action_type, "RUN") == 0 || strcasecmp(action_type, "STOP") == 0) {
        // [ACURATEX] RUN/STOP se publican como valor binario para que la app no
        // tenga que inferirlo del texto de accion.
        int run_value = strcasecmp(action_type, "RUN") == 0 ? 1 : 0;
        app_head_replyf(reply, ctx, "427|%s|RUN|%d|420", instance, run_value);
        return true;
    }

    if (strcasecmp(action_type, "RUN1") == 0 || strcasecmp(action_type, "STOP1") == 0) {
        // [ACURATEX] RUN1/STOP1 conserva una variante de protocolo separada de
        // RUN/STOP.
        int run_value = strcasecmp(action_type, "RUN1") == 0 ? 1 : 0;
        app_head_replyf(reply, ctx, "427|%s|RUN1|%d|420", instance, run_value);
        return true;
    }

    return false;
}

/**
 * [POR QUE EXISTE]
 * Implementa HEAD_PROGRAM_SELECT: valida, verifica existencia y deja un programa
 * TXT como activo para futuras acciones.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_program_process_line() cuando recibe
 * `HEAD_PROGRAM_SELECT|archivo`.
 *
 * [CUANDO SE EJECUTA]
 * Antes de HEAD_ACTION, normalmente despues de transferir/listar archivos.
 *
 * [ENTRADAS]
 * Recibe nombre de archivo, callback de respuesta y contexto del transporte.
 *
 * [SALIDAS]
 * Responde OK|HEAD_PROGRAM_SELECT|archivo o un ERR especifico.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_active_program, s_runner_state, s_current_action, s_current_line,
 * s_action_start_ms, s_last_error, s_module_counts, s_runner_task_handle y
 * s_stop_requested.
 *
 * [CONCURRENCIA]
 * Toma s_state_mutex. Rechaza seleccion si el runner esta RUNNING/STOPPING.
 *
 * [FLUJO ACURATEX]
 * App -> HEAD_PROGRAM_SELECT -> validar nombre -> fopen en /fs -> programa
 * activo.
 *
 * [EQUIVALENCIA MCU]
 * Es elegir la receta activa antes de ejecutar una rutina.
 *
 * [SI NO EXISTIERA]
 * HEAD_ACTION no sabria que TXT abrir.
 */
static esp_err_t app_head_select_program(const char *file_name, app_reply_fn_t reply, void *ctx)
{
    char clean_name[APP_HEAD_MAX_NAME_LEN + 1];
    bool exists = false;
    app_head_runner_state_t current_state;
    char current_action[APP_HEAD_MAX_ACTION_LEN + 1];

    if (file_name == NULL) {
        ESP_LOGI(TAG, "HEAD SELECT: file=<null>");
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_PROGRAM_SELECT|INVALID_NAME|<null>");
    }

    strlcpy(clean_name, file_name, sizeof(clean_name));
    app_trim_line(clean_name);
    if (!app_head_is_valid_program_name(clean_name)) {
        return app_head_replyf(reply, ctx, "ERR|HEAD_PROGRAM_SELECT|INVALID_NAME|%s",
                               clean_name[0] == '\0' ? "<empty>" : clean_name);
    }

    exists = app_head_program_exists(clean_name);
    if (!exists) {
        return app_head_replyf(reply, ctx, "ERR|HEAD_PROGRAM_SELECT|FILE_NOT_FOUND|%s", clean_name);
    }

    app_head_state_lock();
    current_state = s_runner_state;
    strlcpy(current_action, s_current_action, sizeof(current_action));
    if (app_head_is_busy_state(current_state)) {
        app_head_state_unlock();
        return app_head_replyf(reply, ctx, "ERR|HEAD_PROGRAM_SELECT|BUSY|%s",
                               current_action[0] == '\0' ? "NONE" : current_action);
    }

    app_head_state_manager_stop_all_j_runs();
    // [ACURATEX] A partir de este punto el archivo pasa a ser la fuente activa
    // para HEAD_ACTION.
    strlcpy(s_active_program, clean_name, sizeof(s_active_program));
    s_runner_state = APP_HEAD_RUNNER_IDLE;
    s_current_action[0] = '\0';
    s_current_line = 0;
    s_action_start_ms = 0;
    app_head_set_error("");
    app_head_load_module_counts_from_program(s_active_program);
    s_runner_task_handle = NULL;
    s_stop_requested = false;
    app_head_state_unlock();

    return app_head_replyf(reply, ctx, "OK|HEAD_PROGRAM_SELECT|%s", s_active_program);
}

/**
 * [POR QUE EXISTE]
 * Implementa HEAD_STATUS para reportar estado detallado del runner y del
 * programa activo.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_program_process_line() con `HEAD_STATUS`.
 *
 * [CUANDO SE EJECUTA]
 * Bajo consulta de la app, incluso durante una accion.
 *
 * [ENTRADAS]
 * Recibe callback de respuesta y contexto.
 *
 * [SALIDAS]
 * Responde HEAD_STATUS|... o ERR|HEAD_STATUS|SNAPSHOT_FAILED.
 *
 * [ESTADO QUE MODIFICA]
 * Puede corregir un estado RUNNING sin tarea viva a ERROR.
 *
 * [CONCURRENCIA]
 * Toma snapshot con s_state_mutex. Consulta high-water mark si la tarea vive.
 *
 * [FLUJO ACURATEX]
 * App -> HEAD_STATUS -> snapshot runner -> respuesta con programa/accion/linea.
 *
 * [EQUIVALENCIA MCU]
 * Es una lectura de diagnostico de maquina de estados.
 *
 * [SI NO EXISTIERA]
 * La app no podria saber que accion esta corriendo ni en que linea va.
 */
static esp_err_t app_head_status(app_reply_fn_t reply, void *ctx)
{
    app_head_runner_state_t state;
    char program[APP_HEAD_MAX_NAME_LEN + 1];
    char action[APP_HEAD_MAX_ACTION_LEN + 1];
    char last_error[96];
    char last_stage[48];
    int line;
    long long start_ms;
    app_head_module_counts_t counts;
    TaskHandle_t task_handle = NULL;
    bool task_alive = false;
    UBaseType_t stack_high_water = 0;
    // [ESP-IDF] heap_caps_get_free_size() reporta memoria libre del heap por
    // capacidad. MALLOC_CAP_DEFAULT es el heap general usado por malloc/calloc.
    size_t free_heap = heap_caps_get_free_size(MALLOC_CAP_DEFAULT);

    app_head_state_lock();
    // [ACURATEX] Se copia todo el estado bajo mutex y luego se formatea fuera.
    app_head_snapshot_state(
        &state,
        program,
        sizeof(program),
        action,
        sizeof(action),
        &line,
        &start_ms,
        last_error,
        sizeof(last_error),
        last_stage,
        sizeof(last_stage),
        &counts,
        &task_handle);
    app_head_state_unlock();

    if (program[0] == '\0') {
        strlcpy(program, "NONE", sizeof(program));
    }

    if (action[0] == '\0') {
        strlcpy(action, "NONE", sizeof(action));
    }

    task_alive = app_head_task_is_alive(task_handle);
    if (state == APP_HEAD_RUNNER_RUNNING && !task_alive) {
        // [ACURATEX] Defensa de consistencia: RUNNING sin tarea viva es error.
        app_head_state_lock();
        s_runner_state = APP_HEAD_RUNNER_ERROR;
        s_runner_task_handle = NULL;
        s_current_action[0] = '\0';
        s_current_line = 0;
        s_action_start_ms = 0;
        app_head_set_error("RUNNING_WITHOUT_TASK");
        app_head_state_unlock();
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_STATE|RUNNING_WITHOUT_TASK");
    }

    if (task_alive) {
        // [FREERTOS] El high-water mark ayuda a saber si el stack de 8192 tiene
        // margen suficiente durante la accion.
        stack_high_water = uxTaskGetStackHighWaterMark(task_handle);
    }

    char response[APP_HEAD_STATUS_RESPONSE_CAPACITY];
    size_t used = 0;

    if (state == APP_HEAD_RUNNER_RUNNING) {
        // [ACURATEX] RUNNING incluye LINE y ELAPSED porque hay una accion viva.
        if (!app_head_append_status_line(response,
                                         sizeof(response),
                                         &used,
                                         "HEAD_STATUS|STATE=%s|PROGRAM=%s|ACTION=%s|LINE=%d|ELAPSED=%lld|TASK_HANDLE=%p|TASK_ALIVE=%d|FREE_HEAP=%u|STACK_HIGH_WATER_MARK=%u|LAST_STAGE=%s|LAST_ERROR=%s|DEN=%d|SIC=%d|J=%d|YARN=%d|STITCH=%d",
                                         app_head_state_name(state),
                                         program,
                                         action,
                                         line,
                                         app_head_now_ms() - start_ms,
                                         (void *)task_handle,
                                         task_alive ? 1 : 0,
                                         (unsigned)free_heap,
                                         (unsigned)stack_high_water,
                                         last_stage[0] != '\0' ? last_stage : "NONE",
                                         last_error[0] != '\0' ? last_error : "NONE",
                                         counts.den_count,
                                         counts.sic_count,
                                         counts.j_count,
                                         counts.yarn_count,
                                         counts.stitch_count)) {
            return app_head_reply_logged(reply, ctx, "ERR|HEAD_STATUS|SNAPSHOT_FAILED");
        }
    } else if (state == APP_HEAD_RUNNER_ERROR && last_error[0] != '\0') {
        // [ACURATEX] ERROR expone un campo ERROR corto ademas de LAST_ERROR para
        // compatibilidad con diagnostico de la app.
        if (!app_head_append_status_line(response,
                                         sizeof(response),
                                         &used,
                                         "HEAD_STATUS|STATE=%s|PROGRAM=%s|ACTION=%s|ERROR=%s|TASK_HANDLE=%p|TASK_ALIVE=%d|FREE_HEAP=%u|STACK_HIGH_WATER_MARK=%u|LAST_STAGE=%s|LAST_ERROR=%s|DEN=%d|SIC=%d|J=%d|YARN=%d|STITCH=%d",
                                         app_head_state_name(state),
                                         program,
                                         action,
                                         last_error,
                                         (void *)task_handle,
                                         task_alive ? 1 : 0,
                                         (unsigned)free_heap,
                                         (unsigned)stack_high_water,
                                         last_stage[0] != '\0' ? last_stage : "NONE",
                                         last_error[0] != '\0' ? last_error : "NONE",
                                         counts.den_count,
                                         counts.sic_count,
                                         counts.j_count,
                                         counts.yarn_count,
                                         counts.stitch_count)) {
            return app_head_reply_logged(reply, ctx, "ERR|HEAD_STATUS|SNAPSHOT_FAILED");
        }
    } else {
        // [ACURATEX] IDLE/DONE/STOPPING sin error usan una forma mas corta:
        // no hay LINE ni ELAPSED porque no hay tarea activa de accion.
        if (!app_head_append_status_line(response,
                                         sizeof(response),
                                         &used,
                                         "HEAD_STATUS|STATE=%s|PROGRAM=%s|ACTION=%s|TASK_HANDLE=%p|TASK_ALIVE=%d|FREE_HEAP=%u|STACK_HIGH_WATER_MARK=%u|LAST_STAGE=%s|LAST_ERROR=%s|DEN=%d|SIC=%d|J=%d|YARN=%d|STITCH=%d",
                                         app_head_state_name(state),
                                         program,
                                         action,
                                         (void *)task_handle,
                                         task_alive ? 1 : 0,
                                         (unsigned)free_heap,
                                         (unsigned)stack_high_water,
                                         last_stage[0] != '\0' ? last_stage : "NONE",
                                         last_error[0] != '\0' ? last_error : "NONE",
                                         counts.den_count,
                                         counts.sic_count,
                                         counts.j_count,
                                         counts.yarn_count,
                                         counts.stitch_count)) {
            return app_head_reply_logged(reply, ctx, "ERR|HEAD_STATUS|SNAPSHOT_FAILED");
        }
    }

    // [ACURATEX] Al final se agregan los campos Jn=byte fisico y JnR=RUN. Si
    // no caben, se devuelve SNAPSHOT_FAILED sin truncar silenciosamente.
    if (!app_head_append_j_status_fields(response, sizeof(response), &used, counts.j_count)) {
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_STATUS|SNAPSHOT_FAILED");
    }

    return app_head_reply_logged(reply, ctx, response);
}

/**
 * [POR QUE EXISTE]
 * Reporta informacion del programa activo sin iniciar una accion.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_program_process_line() con `HEAD_PROGRAM_INFO`.
 *
 * [CUANDO SE EJECUTA]
 * Bajo consulta de la app.
 *
 * [ENTRADAS]
 * Recibe callback de respuesta y contexto.
 *
 * [SALIDAS]
 * Responde HEAD_PROGRAM_INFO|... o ERR|HEAD_PROGRAM_INFO|NO_PROGRAM.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee s_active_program y s_module_counts sin tomar mutex en esta version.
 *
 * [FLUJO ACURATEX]
 * App -> HEAD_PROGRAM_INFO -> programa activo y conteos MODULE.
 *
 * [EQUIVALENCIA MCU]
 * Es consultar que receta esta cargada.
 *
 * [SI NO EXISTIERA]
 * La app tendria que inferir el programa activo por otros comandos.
 */
static esp_err_t app_head_program_info(app_reply_fn_t reply, void *ctx)
{
    if (s_active_program[0] == '\0') {
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_PROGRAM_INFO|NO_PROGRAM");
    }

    return app_head_replyf(
        reply,
        ctx,
        "HEAD_PROGRAM_INFO|PROGRAM=%s|DEN=%d|SIC=%d|J=%d|YARN=%d|STITCH=%d",
        s_active_program,
        s_module_counts.den_count,
        s_module_counts.sic_count,
        s_module_counts.j_count,
        s_module_counts.yarn_count,
        s_module_counts.stitch_count);
}

/**
 * [POR QUE EXISTE]
 * Extrae los detalles comunes de cualquier accion J.
 *
 * [QUIEN LA LLAMA]
 * app_head_run_action() antes de decidir RUN/STOP o flujo TXT.
 *
 * [CUANDO SE EJECUTA]
 * Al recibir HEAD_ACTION con texto ya limpio.
 *
 * [ENTRADAS]
 * Accion y buffers/punteros donde dejar instancia, numero y tipo.
 *
 * [SALIDAS]
 * true si la instancia empieza con J y tiene numero valido.
 *
 * [ESTADO QUE MODIFICA]
 * Solo escribe buffers de salida.
 *
 * [CONCURRENCIA]
 * No toca estado global.
 *
 * [FLUJO ACURATEX]
 * J1.RUN/J1.CH2/J1.ON_ALL -> instancia J1 y tipo RUN/CH2/ON_ALL.
 *
 * [EQUIVALENCIA MCU]
 * Es decodificar un comando antes de escoger el handler.
 *
 * [SI NO EXISTIERA]
 * El runner no podria separar acciones J directas de acciones TXT.
 */
static bool app_head_parse_j_action_details(const char *action,
                                            char *instance,
                                            size_t instance_len,
                                            int *instance_number,
                                            char *action_type,
                                            size_t action_type_len)
{
    if (!app_head_split_action(action, instance, instance_len, action_type, action_type_len)) {
        return false;
    }

    // [ACURATEX] instance_number queda en base 1 porque las APIs publicas de J
    // reciben J1..J8 como los comandos.
    if (!app_head_parse_number_suffix(instance, "J", instance_number)) {
        return false;
    }

    return true;
}

/**
 * [POR QUE EXISTE]
 * Ejecuta Jx.RUN y Jx.STOP sin buscar un bloque TXT.
 *
 * [QUIEN LA LLAMA]
 * app_head_run_action() cuando detecta action_type RUN o STOP.
 *
 * [CUANDO SE EJECUTA]
 * Inmediatamente al recibir HEAD_ACTION|Jx.RUN o HEAD_ACTION|Jx.STOP.
 *
 * [ENTRADAS]
 * Accion limpia, instancia, numero J, tipo, callback y entorno de comandos.
 *
 * [SALIDAS]
 * Responde OK|HEAD_ACTION|accion o ERR|HEAD_ACTION|RUN_FAILED/STOP_FAILED.
 *
 * [ESTADO QUE MODIFICA]
 * Cambia la maquina RUN del J correspondiente.
 *
 * [CONCURRENCIA]
 * Usa el gestor de estado J protegido por mutex. No crea tarea TXT.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION|J1.RUN -> j_running[0]=true -> tick periodico envia CAN.
 *
 * [EQUIVALENCIA MCU]
 * Es un comando directo a una maquina de estados, no una receta en archivo.
 *
 * [SI NO EXISTIERA]
 * RUN/STOP tendrian que existir como bloques TXT y no serian inmediatos.
 */
static esp_err_t app_head_handle_direct_j_action(const char *clean_action,
                                                 const char *action_instance,
                                                 int instance_number,
                                                 const char *action_type,
                                                 app_reply_fn_t reply,
                                                 void *ctx,
                                                 const app_command_env_t *env)
{
    if (env == NULL || action_instance == NULL || action_type == NULL) {
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|INVALID_ACTION");
    }

    if (strcasecmp(action_type, "RUN") == 0) {
        // [ACURATEX] RUN usa el bus activo si es CAN2; en cualquier otro caso
        // cae a CAN1 para mantener el comportamiento historico.
        int bus = env->active_bus == APP_CMD_CAN_BUS_2 ? APP_CMD_CAN_BUS_2 : APP_CMD_CAN_BUS_1;

        // [CONCURRENCIA] Se conserva el mismo mutex externo del runner al
        // llamar al gestor, manteniendo serializada la decision de RUN.
        app_head_state_lock();
        bool started = app_head_state_manager_start_j_run((uint8_t)instance_number,
                                                          bus,
                                                          (uint32_t)app_head_now_ms());
        app_head_state_unlock();
        if (!started) {
            return app_head_replyf(reply, ctx, "ERR|HEAD_ACTION|RUN_FAILED|%s", clean_action);
        }

        ESP_LOGI(TAG, "FW_RX|HEAD_ACTION|%s", clean_action);
        return app_head_replyf(reply, ctx, "OK|HEAD_ACTION|%s", clean_action);
    }

    if (strcasecmp(action_type, "STOP") == 0) {
        bool stopped = false;

        app_head_state_lock();
        stopped = app_head_state_manager_stop_j_run((uint8_t)instance_number);
        app_head_state_unlock();
        if (!stopped) {
            return app_head_replyf(reply, ctx, "ERR|HEAD_ACTION|STOP_FAILED|%s", clean_action);
        }

        ESP_LOGI(TAG, "FW_RX|HEAD_ACTION|%s", clean_action);
        return app_head_replyf(reply, ctx, "OK|HEAD_ACTION|%s", clean_action);
    }

    return ESP_ERR_NOT_SUPPORTED;
}

/**
 * [POR QUE EXISTE]
 * Implementa HEAD_STOP: solicita cancelar una accion en curso.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_program_process_line() con `HEAD_STOP`.
 *
 * [CUANDO SE EJECUTA]
 * Mientras una accion puede estar corriendo o cuando el runner esta idle.
 *
 * [ENTRADAS]
 * Recibe callback de respuesta y contexto.
 *
 * [SALIDAS]
 * Responde OK|HEAD_STOP|REQUESTED si marco cancelacion u OK|HEAD_STOP|IDLE si
 * no habia accion activa.
 *
 * [ESTADO QUE MODIFICA]
 * Puede poner s_stop_requested=true y s_runner_state=STOPPING.
 *
 * [CONCURRENCIA]
 * Toma s_state_mutex. La tarea de accion observa s_stop_requested en WAIT y
 * antes de lineas.
 *
 * [FLUJO ACURATEX]
 * App -> HEAD_STOP -> s_stop_requested -> accion cancela -> HEAD_ACTION_CANCELLED.
 *
 * [EQUIVALENCIA MCU]
 * Es una bandera de cancelacion revisada por una rutina larga.
 *
 * [SI NO EXISTIERA]
 * Una accion con WAIT/DELAY o muchas lineas no podria cancelarse ordenadamente.
 */
static esp_err_t app_head_stop(app_reply_fn_t reply, void *ctx)
{
    bool requested = false;

    app_head_state_lock();
    if (app_head_is_busy_state(s_runner_state)) {
        s_stop_requested = true;
        s_runner_state = APP_HEAD_RUNNER_STOPPING;
        requested = true;
    }
    else {
        s_stop_requested = false;
    }
    app_head_state_unlock();

    if (requested) {
        return app_head_reply_logged(reply, ctx, "OK|HEAD_STOP|REQUESTED");
    }

    return app_head_reply_logged(reply, ctx, "OK|HEAD_STOP|IDLE");
}

/**
 * [POR QUE EXISTE]
 * Implementa HEAD_ACTION: valida la accion, prepara argumentos y crea la tarea
 * que ejecutara el bloque TXT correspondiente.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_program_process_line() con `HEAD_ACTION|accion`.
 *
 * [CUANDO SE EJECUTA]
 * Despues de seleccionar un programa activo con HEAD_PROGRAM_SELECT.
 *
 * [ENTRADAS]
 * Recibe accion textual, callback de respuesta, contexto y entorno de comandos.
 *
 * [SALIDAS]
 * Responde errores inmediatos, o HEAD_ACTION_START/OK|HEAD_ACTION_STARTED si la
 * tarea fue creada.
 *
 * [ESTADO QUE MODIFICA]
 * Cambia runner a RUNNING, guarda accion/tiempo, reserva memoria para args y
 * guarda s_runner_task_handle.
 *
 * [CONCURRENCIA]
 * Crea tarea FreeRTOS `head_action_task` fijada a Core 1 con stack 8192 y
 * prioridad 4. No se cambian stack ni prioridad.
 *
 * [FLUJO ACURATEX]
 * App -> HEAD_ACTION -> validar programa/accion -> xTaskCreatePinnedToCore -> BEGIN/END.
 *
 * [EQUIVALENCIA MCU]
 * Es lanzar una rutina de receta en segundo plano.
 *
 * [SI NO EXISTIERA]
 * La app podria seleccionar programa pero no ejecutar acciones.
 */
static esp_err_t app_head_run_action(const char *action,
                                     app_reply_fn_t reply,
                                     void *ctx,
                                     const app_command_env_t *env)
{
    char clean_action[APP_HEAD_MAX_ACTION_LEN + 1];
    char action_instance[32];
    char action_type[32];
    int instance_number = 0;
    app_head_runner_task_args_t *task_args = NULL;
    app_head_runner_state_t current_state;
    char current_action[APP_HEAD_MAX_ACTION_LEN + 1];
    void *reply_ctx_copy = NULL;
    BaseType_t task_ok;
    const UBaseType_t head_stack_size = 8192;
    size_t free_heap = 0;

    if (s_active_program[0] == '\0') {
        // [ACURATEX] Sin HEAD_PROGRAM_SELECT previo no hay archivo TXT que abrir.
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|NO_PROGRAM");
    }

    if (action == NULL) {
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|INVALID_ACTION");
    }

    if (strlen(action) > APP_HEAD_MAX_ACTION_LEN) {
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|INVALID_ACTION");
    }

    strlcpy(clean_action, action, sizeof(clean_action));
    app_trim_line(clean_action);
    if (clean_action[0] == '\0' || strchr(clean_action, '|') != NULL || strlen(clean_action) > APP_HEAD_MAX_ACTION_LEN) {
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|INVALID_ACTION");
    }

    if (app_head_parse_j_action_details(clean_action,
                                        action_instance,
                                        sizeof(action_instance),
                                        &instance_number,
                                        action_type,
                                        sizeof(action_type))) {
        if (!app_head_is_instance_enabled(action_instance)) {
            return app_head_replyf(reply, ctx, "ERR|HEAD_ACTION|MODULE_DISABLED|%s", action_instance);
        }

        if (strcasecmp(action_type, "RUN") == 0 || strcasecmp(action_type, "STOP") == 0) {
            return app_head_handle_direct_j_action(clean_action,
                                                   action_instance,
                                                   instance_number,
                                                   action_type,
                                                   reply,
                                                   ctx,
                                                   env);
        }
    }

    app_head_state_lock();
    current_state = s_runner_state;
    strlcpy(current_action, s_current_action, sizeof(current_action));
    if (app_head_is_busy_state(current_state)) {
        app_head_state_unlock();
        return app_head_replyf(reply, ctx, "ERR|HEAD_ACTION|BUSY|%s",
                               current_action[0] == '\0' ? "NONE" : current_action);
    }

    s_stop_requested = false;
    s_runner_state = APP_HEAD_RUNNER_RUNNING;
    s_current_line = 0;
    s_action_start_ms = app_head_now_ms();
    strlcpy(s_current_action, clean_action, sizeof(s_current_action));
    app_head_set_error("");
    s_runner_task_handle = NULL;
    app_head_state_unlock();

    task_args = (app_head_runner_task_args_t *)calloc(1, sizeof(app_head_runner_task_args_t));
    if (task_args == NULL) {
        app_head_state_lock();
        s_runner_state = APP_HEAD_RUNNER_ERROR;
        s_current_action[0] = '\0';
        s_current_line = 0;
        app_head_set_error("NO_MEMORY");
        app_head_state_unlock();
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|NO_MEMORY");
    }

    if (ctx != NULL && env->reply_ctx_clone != NULL) {
        // [RTOS] El contexto puede apuntar a una ruta de respuesta creada en el
        // dispatcher. Se clona para que head_action_task no guarde punteros a
        // stack ni sockets crudos despues de que el handler retorne.
        reply_ctx_copy = env->reply_ctx_clone(ctx);
        if (reply_ctx_copy == NULL) {
            free(task_args);
            app_head_state_lock();
            s_runner_state = APP_HEAD_RUNNER_ERROR;
            s_current_action[0] = '\0';
            s_current_line = 0;
            app_head_set_error("NO_MEMORY");
            app_head_state_unlock();
            return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|NO_MEMORY");
        }
        task_args->reply_ctx = reply_ctx_copy;
        task_args->reply_ctx_owned = true;
    }
    else {
        task_args->reply_ctx = ctx;
        task_args->reply_ctx_owned = false;
    }

    strlcpy(task_args->program, s_active_program, sizeof(task_args->program));
    strlcpy(task_args->action, clean_action, sizeof(task_args->action));
    // [ACURATEX] Para Jx.CHy se captura ahora el estado fisico anterior y el
    // byte candidato. La tarea usara este contexto mas adelante cuando encuentre
    // `XX` en una linea `send`.
    (void)app_head_parse_dynamic_j_action(clean_action, &task_args->dynamic_ctx);
    task_args->reply = reply;
    task_args->start_ms = s_action_start_ms;
    task_args->env = *env;

    if (app_head_parse_j_action_details(clean_action,
                                        action_instance,
                                        sizeof(action_instance),
                                        &instance_number,
                                        action_type,
                                        sizeof(action_type))) {
        bool running = false;

        // [ACURATEX] Esta comprobacion conserva literalmente la condicion del
        // firmware: compara `CH` exacto, ademas de ON_ALL/OFF_ALL. Las acciones
        // Jx.CHn dinamicas se detectan antes con app_head_parse_dynamic_j_action().
        if ((strcasecmp(action_type, "CH") == 0
             || strcasecmp(action_type, "ON_ALL") == 0
             || strcasecmp(action_type, "OFF_ALL") == 0)
            && app_head_state_manager_get_j_status((uint8_t)instance_number, NULL, &running)
            && running) {
            // [ACURATEX] Si esta condicion aplica, se detiene RUN antes de
            // lanzar el bloque TXT para que la accion no compita con el barrido.
            (void)app_head_state_manager_stop_j_run((uint8_t)instance_number);
        }
    }

    if (!task_args->dynamic_ctx.active) {
        if (app_head_split_action(clean_action,
                                  action_instance,
                                  sizeof(action_instance),
                                  action_type,
                                  sizeof(action_type))
            && app_head_instance_starts_with(action_instance, "J")
            && strncasecmp(action_type, "CH", 2) == 0) {
            // [ACURATEX] Una accion Jx.CHn sin contexto dinamico valido se
            // rechaza. Esto evita ejecutar un bloque TXT que esperaba XX sin
            // saber que byte fisico corresponde.
            app_head_release_task_args(task_args);
            free(task_args);
            app_head_state_lock();
            s_runner_state = APP_HEAD_RUNNER_IDLE;
            s_current_action[0] = '\0';
            s_current_line = 0;
            s_action_start_ms = 0;
            app_head_set_error("");
            s_runner_task_handle = NULL;
            s_stop_requested = false;
            app_head_state_unlock();
            return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|INVALID_ACTION");
        }
    }

    ESP_LOGI(TAG, "FW_RX|HEAD_ACTION|%s", clean_action);
    app_head_set_stage("TASK_CREATE_BEGIN");
    free_heap = heap_caps_get_free_size(MALLOC_CAP_DEFAULT);
    (void)app_head_replyf(reply, ctx, "HEAD_TASK_CREATE_BEGIN|FREE_HEAP=%u|STACK=%u",
                          (unsigned)free_heap,
                          (unsigned)head_stack_size);

    // [FREERTOS] xTaskCreatePinnedToCore lanza la ejecucion TXT fuera del
    // contexto que recibio el comando y la fija en Core 1 para que no dependa
    // de la recepcion USB/TCP.
    task_ok = xTaskCreatePinnedToCore(
        app_head_run_action_task,
        "head_action_task",
        head_stack_size,
        task_args,
        4,
        &s_runner_task_handle,
        1);
    ESP_LOGI(TAG, "HEAD_TASK_CREATE_RESULT|RESULT=%s|HANDLE=%p",
             task_ok == pdPASS ? "pdPASS" : "FAIL",
             (void *)s_runner_task_handle);
    (void)app_head_replyf(reply, ctx, "HEAD_TASK_CREATE_RESULT|RESULT=%s|HANDLE=%p",
                          task_ok == pdPASS ? "pdPASS" : "FAIL",
                          (void *)s_runner_task_handle);
    if (task_ok != pdPASS) {
        app_head_set_stage("TASK_CREATE_FAILED");
        app_head_release_task_args(task_args);
        free(task_args);
        app_head_state_lock();
        s_runner_state = APP_HEAD_RUNNER_IDLE;
        s_current_action[0] = '\0';
        s_current_line = 0;
        s_action_start_ms = 0;
        app_head_set_error("");
        s_runner_task_handle = NULL;
        s_stop_requested = false;
        app_head_state_unlock();
        return app_head_reply_logged(reply, ctx, "ERR|HEAD_ACTION|TASK_CREATE_FAILED");
    }

    app_head_set_stage("TASK_CREATED");

    (void)app_head_replyf(reply, ctx, "HEAD_ACTION_START|%s", clean_action);
    if (strcasecmp(clean_action, "INIT") == 0) {
        (void)app_head_replyf(reply, ctx, "HEAD_INIT_START|TIME=%lld", s_action_start_ms);
    }

    (void)app_head_replyf(reply, ctx, "OK|HEAD_ACTION_STARTED|%s", clean_action);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Inicializa el runner de programas de Cabezal al arrancar el firmware.
 *
 * [QUIEN LA LLAMA]
 * La llama app_main() durante el arranque.
 *
 * [CUANDO SE EJECUTA]
 * Una vez antes de aceptar comandos HEAD_*.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Crea s_state_mutex si falta, limpia programa/accion/error/etapa, deja estado
 * IDLE, reinicia conteos y llama app_head_state_manager_init().
 *
 * [CONCURRENCIA]
 * Crea mutex FreeRTOS y lo usa para inicializar estado compartido.
 *
 * [FLUJO ACURATEX]
 * app_main -> app_head_program_runner_init -> HEAD_* listo.
 *
 * [EQUIVALENCIA MCU]
 * Es inicializar una maquina de estados y sus variables globales.
 *
 * [SI NO EXISTIERA]
 * HEAD_PROGRAM_SELECT/HEAD_ACTION trabajarian sobre estado sin inicializar.
 */
void app_head_program_runner_init()
{
    if (s_state_mutex == NULL) {
        // [FREERTOS] Mutex que protege estado entre app_main/TCP y tarea de accion.
        s_state_mutex = xSemaphoreCreateMutex();
    }

    app_head_state_lock();
    s_active_program[0] = '\0';
    s_current_action[0] = '\0';
    s_current_line = 0;
    s_action_start_ms = 0;
    s_last_error[0] = '\0';
    s_last_stage[0] = '\0';
    s_stop_requested = false;
    s_runner_state = APP_HEAD_RUNNER_IDLE;
    s_runner_task_handle = NULL;
    s_module_counts = app_head_default_module_counts();
    app_head_state_unlock();

    (void)app_head_state_manager_init();
}

/**
 * [POR QUE EXISTE]
 * Permite a command_processor.cpp reconocer si una linea pertenece a HEAD_*.
 *
 * [QUIEN LA LLAMA]
 * La llama app_command_process_line().
 *
 * [CUANDO SE EJECUTA]
 * Antes de FILE_*, send y texto generico.
 *
 * [ENTRADAS]
 * Recibe linea ya normalizada.
 *
 * [SALIDAS]
 * Devuelve true para HEAD_PROGRAM_SELECT, HEAD_PROGRAM_INFO, HEAD_ACTION,
 * HEAD_STOP o HEAD_STATUS.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea.
 *
 * [FLUJO ACURATEX]
 * command_processor -> app_head_program_is_command -> dispatcher HEAD.
 *
 * [EQUIVALENCIA MCU]
 * Es un filtro de prefijos de comandos.
 *
 * [SI NO EXISTIERA]
 * Los comandos HEAD_* caerian en otros handlers o texto generico.
 */
bool app_head_program_is_command(const char *line)
{
    return line != NULL
        && (app_head_command_has_prefix(line, "HEAD_PROGRAM_SELECT")
            || app_head_command_has_prefix(line, "HEAD_PROGRAM_INFO")
            || app_head_command_has_prefix(line, "HEAD_ACTION")
            || app_head_command_has_prefix(line, "HEAD_STOP")
            || app_head_command_has_prefix(line, "HEAD_STATUS"));
}

/**
 * [POR QUE EXISTE]
 * Es el dispatcher publico de todos los comandos HEAD_*.
 *
 * [QUIEN LA LLAMA]
 * La llama command_processor.cpp despues de identificar una linea HEAD_*.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que la app envia HEAD_PROGRAM_SELECT, HEAD_ACTION, HEAD_STOP,
 * HEAD_STATUS o HEAD_PROGRAM_INFO.
 *
 * [ENTRADAS]
 * Recibe linea completa, callback de respuesta, contexto y entorno del comando.
 *
 * [SALIDAS]
 * Devuelve el resultado del handler especifico o responde ERR|HEAD|UNKNOWN_COMMAND.
 *
 * [ESTADO QUE MODIFICA]
 * Depende del comando: puede seleccionar programa, iniciar/cancelar accion o
 * solo reportar estado.
 *
 * [CONCURRENCIA]
 * Corre en el contexto del transporte. HEAD_ACTION crea una tarea separada para
 * no bloquear el transporte.
 *
 * [FLUJO ACURATEX]
 * App -> command_processor -> app_head_program_process_line -> handler HEAD_*.
 *
 * [EQUIVALENCIA MCU]
 * Es un switch/if de comandos especificos de Cabezal.
 *
 * [SI NO EXISTIERA]
 * El protocolo HEAD_* no tendria punto de entrada desde la aplicacion.
 */
esp_err_t app_head_program_process_line(const char *incoming_line,
                                        app_reply_fn_t reply,
                                        void *ctx,
                                        const app_command_env_t *env)
{
    char line[192];

    if (incoming_line == NULL || reply == NULL || env == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    strlcpy(line, incoming_line, sizeof(line));
    app_trim_line(line);
    ESP_LOGI(TAG, "HEAD RX: %s", line);

    if (strncasecmp(line, "HEAD_PROGRAM_SELECT|", 20) == 0) {
        // [ACURATEX] El nombre empieza justo despues del separador.
        return app_head_select_program(line + 20, reply, ctx);
    }

    if (strcasecmp(line, "HEAD_PROGRAM_INFO") == 0) {
        return app_head_program_info(reply, ctx);
    }

    if (strncasecmp(line, "HEAD_ACTION|", 12) == 0) {
        // [ACURATEX] La accion se ejecutara contra el programa activo.
        return app_head_run_action(line + 12, reply, ctx, env);
    }

    if (strcasecmp(line, "HEAD_STOP") == 0) {
        return app_head_stop(reply, ctx);
    }

    if (strcasecmp(line, "HEAD_STATUS") == 0) {
        return app_head_status(reply, ctx);
    }

    return app_head_reply_logged(reply, ctx, "ERR|HEAD|UNKNOWN_COMMAND");
}
