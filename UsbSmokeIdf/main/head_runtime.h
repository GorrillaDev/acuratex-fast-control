#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"
#include "freertos/FreeRTOS.h"

#include "app_rtos_types.h"
#include "command_processor.h"

typedef void (*app_head_runtime_env_builder_fn_t)(app_command_env_t *env,
                                                  bool usb_mounted);

esp_err_t app_head_runtime_init(app_head_runtime_env_builder_fn_t env_builder);
esp_err_t app_head_runtime_start(void);
esp_err_t app_head_runtime_enqueue(app_transport_type_t transport,
                                   uint32_t session_id,
                                   const char *line,
                                   bool usb_mounted,
                                   TickType_t timeout);
uint32_t app_head_runtime_get_drops(void);
uint32_t app_head_runtime_get_high_water(void);
