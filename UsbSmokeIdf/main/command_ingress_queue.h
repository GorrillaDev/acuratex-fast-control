#pragma once

#include "esp_err.h"
#include "freertos/FreeRTOS.h"

#include "app_rtos_types.h"

typedef struct
{
    app_transport_type_t transport;
    uint32_t session_id;
    char line[APP_COMMAND_MAX_LINE_LENGTH];
} app_command_message_t;

esp_err_t app_command_ingress_init(void);
esp_err_t app_command_ingress_enqueue(app_transport_type_t transport,
                                      uint32_t session_id,
                                      const char *line,
                                      TickType_t timeout);
bool app_command_ingress_receive(app_command_message_t *message, TickType_t timeout);
uint32_t app_command_ingress_get_drops(void);
uint32_t app_command_ingress_get_high_water(void);
