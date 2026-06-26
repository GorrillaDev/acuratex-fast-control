#include <inttypes.h>
#include <string.h>
#include <strings.h>

#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"

#include "app_rtos_types.h"
#include "head_runtime.h"
#include "line_codec.h"
#include "reply_dispatcher.h"

typedef struct
{
    app_transport_type_t transport;
    uint32_t session_id;
    bool usb_mounted;
    uint64_t enqueue_us;
    char line[APP_COMMAND_MAX_LINE_LENGTH];
} app_head_command_message_t;

static const char *TAG = "head_runtime";

static QueueHandle_t s_head_command_queue = NULL;
static TaskHandle_t s_head_control_task_handle = NULL;
static app_head_runtime_env_builder_fn_t s_env_builder = NULL;
static uint32_t s_head_command_queue_drops = 0;
static uint32_t s_head_command_queue_high_water = 0;

static void app_head_runtime_update_high_water(void)
{
    if (s_head_command_queue == NULL)
    {
        return;
    }

    UBaseType_t waiting = uxQueueMessagesWaiting(s_head_command_queue);
    if ((uint32_t)waiting > s_head_command_queue_high_water)
    {
        s_head_command_queue_high_water = (uint32_t)waiting;
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion identifica comandos de parada cortos que no deben esperar
 * detras de la cola normal de comandos.
 *
 * [QUIEN LA LLAMA]
 * La llama app_head_runtime_enqueue() antes de decidir entre xQueueSend() y
 * xQueueSendToFront().
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que Core 0 intenta encolar una linea fisica hacia Core 1.
 *
 * [ENTRADAS]
 * Recibe la linea ya copiada desde el transporte.
 *
 * [SALIDAS]
 * Devuelve true para stop, j_stop_*, y tambien conserva compatibilidad con los
 * prefijos antiguos mientras se retiran del flujo operativo.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado; trabaja sobre una copia local para poder recortar CR/LF.
 *
 * [CONCURRENCIA]
 * No bloquea ni toma mutex.
 *
 * [FLUJO ACURATEX]
 * App -> STOP/J_STOP -> cola al frente -> control_task/HeadStateManager.
 *
 * [EQUIVALENCIA MCU]
 * Es una clasificacion de prioridad, similar a tratar STOP como evento urgente.
 *
 * [SI NO EXISTIERA]
 * Un STOP podria quedar detras de comandos normales en la cola fisica.
 */
static bool app_head_runtime_is_priority_stop(const char *line)
{
    char clean[APP_COMMAND_MAX_LINE_LENGTH];

    if (line == NULL)
    {
        return false;
    }

    strlcpy(clean, line, sizeof(clean));
    app_trim_line(clean);

    if (strcasecmp(clean, "HEAD_STOP") == 0
        || strcasecmp(clean, "stop") == 0
        || strcasecmp(clean, "emergency_stop") == 0
        || strcasecmp(clean, "j_stop_all") == 0
        || strncasecmp(clean, "j_stop_", 7) == 0
        || strcasecmp(clean, "y_stop_all") == 0
        || strncasecmp(clean, "y1_stop", 7) == 0
        || strncasecmp(clean, "y2_stop", 7) == 0
        || strcasecmp(clean, "s_stop_all") == 0
        || strncasecmp(clean, "s_stop_", 7) == 0
        || strncasecmp(clean, "den_stop1_", 10) == 0
        || strncasecmp(clean, "den_stop_", 9) == 0
        || strncasecmp(clean, "sic_stop_", 9) == 0)
    {
        return true;
    }

    if (strncasecmp(clean, "HEAD_ACTION|", 12) != 0)
    {
        return false;
    }

    const char *action = clean + 12;
    if (strncasecmp(action, "J", 1) != 0)
    {
        return false;
    }

    const char *dot = strchr(action, '.');
    return dot != NULL && strcasecmp(dot + 1, "STOP") == 0;
}

/**
 * [POR QUE EXISTE]
 * Esta tarea separa la ejecucion fisica de Cabezal/CAN del hilo de
 * comunicaciones. Core 0 solo encola y esta tarea ejecuta el parser existente.
 *
 * [CORE]
 * Core 1, reservado para control fisico de Cabezal y CAN de aplicacion.
 *
 * [PRIORIDAD]
 * Prioridad 6: queda por encima del dispatch de comunicaciones prioridad 4 y
 * de tareas de servicio, sin cambiar la prioridad 4 del worker HEAD_ACTION.
 *
 * [STACK]
 * 6144, igual que el dispatch de comandos que antes podia ejecutar estas rutas.
 *
 * [QUIEN LA DESPIERTA]
 * FreeRTOS la despierta cuando app_head_runtime_enqueue() agrega un comando.
 *
 * [QUE COLA CONSUME]
 * s_head_command_queue, que copia linea completa, transporte y session_id.
 *
 * [QUE COLA PRODUCE]
 * No produce comandos. Las respuestas salen por app_reply_queue mediante
 * app_reply_dispatcher_reply().
 *
 * [QUE ESTADO MODIFICA]
 * Puede modificar seleccion CAN, estado de Cabezal y estado del runner HEAD_* al
 * reutilizar app_command_process_line().
 *
 * [QUE MUTEX UTILIZA]
 * No toma mutex directamente. Los modulos llamados mantienen sus mutex internos.
 *
 * [QUE PASA SI SE BLOQUEA]
 * Si se bloquea por una operacion fisica, Core 0 sigue recibiendo comandos y
 * respuestas porque no ejecuta esta logica directamente.
 */
static void app_head_control_task(void *arg)
{
    (void)arg;

    ESP_LOGI(TAG,
             "[RTOS] head_control core=%d priority=%u stack=6144",
             xPortGetCoreID(),
             6U);

    while (true)
    {
        app_head_command_message_t message = {};
        if (xQueueReceive(s_head_command_queue, &message, portMAX_DELAY) != pdTRUE)
        {
            continue;
        }

        app_head_runtime_update_high_water();

        app_command_env_t env = {};
        if (s_env_builder != NULL)
        {
            s_env_builder(&env, message.usb_mounted);
        }

        if (FAST_PERF_LOG)
        {
            uint64_t now_us = (uint64_t)esp_timer_get_time();
            uint64_t latency_us = (message.enqueue_us == 0ULL || now_us < message.enqueue_us)
                ? 0ULL
                : (now_us - message.enqueue_us);
            ESP_LOGI(TAG,
                     "[FAST] EXEC command=%s latency_us=%" PRIu64,
                     message.line,
                     latency_us);
        }

        app_reply_route_t route = {
            .transport = message.transport,
            .session_id = message.session_id,
        };

        esp_err_t err = app_command_process_line(message.line,
                                                 app_reply_dispatcher_reply,
                                                 &route,
                                                 &env);
        if (err != ESP_OK)
        {
            ESP_LOGW(TAG,
                     "HEAD_CONTROL_PROCESS_ERR|TRANSPORT=%d|SESSION=%u|ERR=%s",
                     (int)message.transport,
                     (unsigned)message.session_id,
                     esp_err_to_name(err));
        }
    }
}

esp_err_t app_head_runtime_init(app_head_runtime_env_builder_fn_t env_builder)
{
    if (env_builder == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    s_env_builder = env_builder;

    if (s_head_command_queue != NULL)
    {
        return ESP_OK;
    }

    s_head_command_queue = xQueueCreate(APP_HEAD_COMMAND_QUEUE_LENGTH,
                                        sizeof(app_head_command_message_t));
    if (s_head_command_queue == NULL)
    {
        ESP_LOGE(TAG, "No se pudo crear cola de comandos de Cabezal");
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG,
             "[RTOS] head_command_queue length=%d item=%u bytes",
             APP_HEAD_COMMAND_QUEUE_LENGTH,
             (unsigned)sizeof(app_head_command_message_t));
    return ESP_OK;
}

esp_err_t app_head_runtime_start(void)
{
    if (s_head_command_queue == NULL)
    {
        return ESP_ERR_INVALID_STATE;
    }

    if (s_head_control_task_handle != NULL)
    {
        return ESP_OK;
    }

    BaseType_t ok = xTaskCreatePinnedToCore(app_head_control_task,
                                            "head_control",
                                            6144,
                                            NULL,
                                            6,
                                            &s_head_control_task_handle,
                                            1);
    if (ok != pdPASS)
    {
        s_head_control_task_handle = NULL;
        return ESP_ERR_NO_MEM;
    }

    return ESP_OK;
}

esp_err_t app_head_runtime_enqueue(app_transport_type_t transport,
                                   uint32_t session_id,
                                   const char *line,
                                   bool usb_mounted,
                                   TickType_t timeout)
{
    if (s_head_command_queue == NULL || line == NULL)
    {
        return ESP_ERR_INVALID_STATE;
    }

    app_head_command_message_t message = {};
    message.transport = transport;
    message.session_id = session_id;
    message.usb_mounted = usb_mounted;
    message.enqueue_us = (uint64_t)esp_timer_get_time();
    strlcpy(message.line, line, sizeof(message.line));

    bool priority_stop = app_head_runtime_is_priority_stop(message.line);
    BaseType_t queued = priority_stop
        ? xQueueSendToFront(s_head_command_queue, &message, 0)
        : xQueueSend(s_head_command_queue, &message, timeout);

    if (queued != pdTRUE && priority_stop)
    {
        app_head_command_message_t discarded = {};
        if (xQueueReceive(s_head_command_queue, &discarded, 0) == pdTRUE)
        {
            ESP_LOGW(TAG,
                     "HEAD_COMMAND_DROP_FOR_STOP|DROPPED=%s|STOP=%s",
                     discarded.line,
                     message.line);
            queued = xQueueSendToFront(s_head_command_queue, &message, 0);
        }
    }

    if (queued != pdTRUE)
    {
        s_head_command_queue_drops++;
        ESP_LOGW(TAG,
                 "HEAD_COMMAND_QUEUE_FULL|TRANSPORT=%d|SESSION=%u|DROPS=%u",
                 (int)transport,
                 (unsigned)session_id,
                 (unsigned)s_head_command_queue_drops);
        return ESP_ERR_TIMEOUT;
    }

    if (priority_stop)
    {
        ESP_LOGI(TAG,
                 "HEAD_COMMAND_PRIORITY_STOP|TRANSPORT=%d|SESSION=%u|LINE=%s",
                 (int)transport,
                 (unsigned)session_id,
                 message.line);
    }

    app_head_runtime_update_high_water();
    if (FAST_PERF_LOG)
    {
        ESP_LOGI(TAG,
                 "[FAST] QUEUED us=%" PRIu64 "|LINE=%s",
                 message.enqueue_us,
                 message.line);
    }
    return ESP_OK;
}

uint32_t app_head_runtime_get_drops(void)
{
    return s_head_command_queue_drops;
}

uint32_t app_head_runtime_get_high_water(void)
{
    return s_head_command_queue_high_water;
}
