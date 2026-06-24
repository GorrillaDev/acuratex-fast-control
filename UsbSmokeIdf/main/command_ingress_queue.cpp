#include <string.h>

#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"

#include "command_ingress_queue.h"

static const char *TAG = "command_ingress";

static QueueHandle_t s_command_queue = NULL;
static uint32_t s_command_queue_drops = 0;
static uint32_t s_command_queue_high_water = 0;

static void app_command_ingress_update_high_water(void)
{
    if (s_command_queue == NULL)
    {
        return;
    }

    UBaseType_t waiting = uxQueueMessagesWaiting(s_command_queue);
    if ((uint32_t)waiting > s_command_queue_high_water)
    {
        s_command_queue_high_water = (uint32_t)waiting;
    }
}

esp_err_t app_command_ingress_init(void)
{
    if (s_command_queue != NULL)
    {
        return ESP_OK;
    }

    s_command_queue = xQueueCreate(APP_COMMAND_INGRESS_QUEUE_LENGTH,
                                   sizeof(app_command_message_t));
    if (s_command_queue == NULL)
    {
        ESP_LOGE(TAG, "No se pudo crear cola de comandos");
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG,
             "[RTOS] command_ingress_queue length=%d item=%u bytes",
             APP_COMMAND_INGRESS_QUEUE_LENGTH,
             (unsigned)sizeof(app_command_message_t));
    return ESP_OK;
}

esp_err_t app_command_ingress_enqueue(app_transport_type_t transport,
                                      uint32_t session_id,
                                      const char *line,
                                      TickType_t timeout)
{
    if (s_command_queue == NULL || line == NULL)
    {
        return ESP_ERR_INVALID_STATE;
    }

    app_command_message_t message = {};
    message.transport = transport;
    message.session_id = session_id;
    strlcpy(message.line, line, sizeof(message.line));

    if (xQueueSend(s_command_queue, &message, timeout) != pdTRUE)
    {
        s_command_queue_drops++;
        ESP_LOGW(TAG,
                 "COMMAND_QUEUE_FULL|TRANSPORT=%d|SESSION=%u|DROPS=%u",
                 (int)transport,
                 (unsigned)session_id,
                 (unsigned)s_command_queue_drops);
        return ESP_ERR_TIMEOUT;
    }

    app_command_ingress_update_high_water();
    return ESP_OK;
}

bool app_command_ingress_receive(app_command_message_t *message, TickType_t timeout)
{
    if (s_command_queue == NULL || message == NULL)
    {
        return false;
    }

    if (xQueueReceive(s_command_queue, message, timeout) != pdTRUE)
    {
        return false;
    }

    app_command_ingress_update_high_water();
    return true;
}

uint32_t app_command_ingress_get_drops(void)
{
    return s_command_queue_drops;
}

uint32_t app_command_ingress_get_high_water(void)
{
    return s_command_queue_high_water;
}
