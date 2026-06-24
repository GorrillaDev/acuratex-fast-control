#include <stdlib.h>
#include <string.h>

#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"

#include "reply_dispatcher.h"

typedef struct
{
    app_reply_route_t route;
    char line[APP_REPLY_MAX_LINE_LENGTH];
} app_reply_message_t;

static const char *TAG = "reply_dispatch";

static QueueHandle_t s_reply_queue = NULL;
static TaskHandle_t s_reply_task_handle = NULL;
static app_reply_writer_fn_t s_writer = NULL;
static void *s_writer_ctx = NULL;
static uint32_t s_reply_queue_drops = 0;
static uint32_t s_reply_queue_high_water = 0;

static void app_reply_dispatcher_update_high_water(void)
{
    if (s_reply_queue == NULL)
    {
        return;
    }

    UBaseType_t waiting = uxQueueMessagesWaiting(s_reply_queue);
    if ((uint32_t)waiting > s_reply_queue_high_water)
    {
        s_reply_queue_high_water = (uint32_t)waiting;
    }
}

/**
 * [POR QUE EXISTE]
 * Esta tarea centraliza la escritura de respuestas hacia USB, TCP y UART.
 *
 * [CORE]
 * Core 0, porque escribe transportes y no debe correr en el core fisico.
 *
 * [PRIORIDAD]
 * Prioridad 5: suficiente para vaciar respuestas sin competir con CAN/Core 1.
 *
 * [STACK]
 * 4096, porque solo desencola, valida ruta y llama un writer.
 *
 * [QUIEN LA DESPIERTA]
 * FreeRTOS la despierta cuando app_reply_dispatcher_enqueue() agrega mensajes.
 *
 * [QUE COLA CONSUME]
 * s_reply_queue.
 *
 * [QUE COLA PRODUCE]
 * No produce colas; llama el writer registrado por app_main.
 *
 * [QUE ESTADO MODIFICA]
 * Actualiza diagnosticos de cola y usa el writer de transporte.
 *
 * [QUE MUTEX UTILIZA]
 * No toma mutex directamente. El writer toma los mutex USB/TCP/UART existentes.
 *
 * [QUE PASA SI SE BLOQUEA]
 * Si se bloquea, las respuestas se acumulan; Core 1 no escribe transportes.
 */
static void app_reply_dispatcher_task(void *arg)
{
    (void)arg;

    ESP_LOGI(TAG,
             "[RTOS] reply_dispatch core=%d priority=%u stack=4096",
             xPortGetCoreID(),
             5U);

    while (true)
    {
        app_reply_message_t message = {};
        if (xQueueReceive(s_reply_queue, &message, portMAX_DELAY) != pdTRUE)
        {
            continue;
        }

        app_reply_dispatcher_update_high_water();

        if (s_writer == NULL)
        {
            ESP_LOGW(TAG, "REPLY_NO_WRITER|TRANSPORT=%d", (int)message.route.transport);
            continue;
        }

        esp_err_t err = s_writer(&message.route, message.line, s_writer_ctx);
        if (err != ESP_OK)
        {
            ESP_LOGW(TAG,
                     "REPLY_DROP|TRANSPORT=%d|SESSION=%u|ERR=%s",
                     (int)message.route.transport,
                     (unsigned)message.route.session_id,
                     esp_err_to_name(err));
        }
    }
}

esp_err_t app_reply_dispatcher_init(app_reply_writer_fn_t writer, void *user_ctx)
{
    if (writer == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    s_writer = writer;
    s_writer_ctx = user_ctx;

    if (s_reply_queue != NULL)
    {
        return ESP_OK;
    }

    s_reply_queue = xQueueCreate(APP_REPLY_QUEUE_LENGTH, sizeof(app_reply_message_t));
    if (s_reply_queue == NULL)
    {
        ESP_LOGE(TAG, "No se pudo crear cola de respuestas");
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG,
             "[RTOS] reply_queue length=%d item=%u bytes",
             APP_REPLY_QUEUE_LENGTH,
             (unsigned)sizeof(app_reply_message_t));
    return ESP_OK;
}

esp_err_t app_reply_dispatcher_start(void)
{
    if (s_reply_queue == NULL)
    {
        return ESP_ERR_INVALID_STATE;
    }

    if (s_reply_task_handle != NULL)
    {
        return ESP_OK;
    }

    BaseType_t ok = xTaskCreatePinnedToCore(app_reply_dispatcher_task,
                                            "reply_dispatch",
                                            4096,
                                            NULL,
                                            5,
                                            &s_reply_task_handle,
                                            0);
    if (ok != pdPASS)
    {
        s_reply_task_handle = NULL;
        return ESP_ERR_NO_MEM;
    }

    return ESP_OK;
}

esp_err_t app_reply_dispatcher_enqueue(app_transport_type_t transport,
                                       uint32_t session_id,
                                       const char *line,
                                       TickType_t timeout)
{
    if (s_reply_queue == NULL || line == NULL)
    {
        return ESP_ERR_INVALID_STATE;
    }

    app_reply_message_t message = {};
    message.route.transport = transport;
    message.route.session_id = session_id;
    size_t copied = strlcpy(message.line, line, sizeof(message.line));
    if (copied >= sizeof(message.line)) {
        s_reply_queue_drops++;
        ESP_LOGW(TAG,
                 "REPLY_TRUNCATED|TRANSPORT=%d|SESSION=%u|REQUESTED=%u|CAPACITY=%u|DROPS=%u",
                 (int)transport,
                 (unsigned)session_id,
                 (unsigned)copied,
                 (unsigned)sizeof(message.line),
                 (unsigned)s_reply_queue_drops);
        return ESP_ERR_INVALID_SIZE;
    }

    if (xQueueSend(s_reply_queue, &message, timeout) != pdTRUE)
    {
        s_reply_queue_drops++;
        ESP_LOGW(TAG,
                 "REPLY_QUEUE_FULL|TRANSPORT=%d|SESSION=%u|DROPS=%u",
                 (int)transport,
                 (unsigned)session_id,
                 (unsigned)s_reply_queue_drops);
        return ESP_ERR_TIMEOUT;
    }

    app_reply_dispatcher_update_high_water();
    return ESP_OK;
}

esp_err_t app_reply_dispatcher_reply(const char *line, void *ctx)
{
    if (line == NULL || ctx == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    const app_reply_route_t *route = (const app_reply_route_t *)ctx;
    return app_reply_dispatcher_enqueue(route->transport,
                                        route->session_id,
                                        line,
                                        pdMS_TO_TICKS(50));
}

void *app_reply_dispatcher_ctx_clone(void *ctx)
{
    if (ctx == NULL)
    {
        return NULL;
    }

    app_reply_route_t *copy = (app_reply_route_t *)calloc(1, sizeof(app_reply_route_t));
    if (copy == NULL)
    {
        return NULL;
    }

    *copy = *(const app_reply_route_t *)ctx;
    return copy;
}

void app_reply_dispatcher_ctx_release(void *ctx)
{
    free(ctx);
}

uint32_t app_reply_dispatcher_get_drops(void)
{
    return s_reply_queue_drops;
}

uint32_t app_reply_dispatcher_get_high_water(void)
{
    return s_reply_queue_high_water;
}
