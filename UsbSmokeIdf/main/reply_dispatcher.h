#pragma once

#include "esp_err.h"
#include "freertos/FreeRTOS.h"

#include "app_rtos_types.h"

typedef esp_err_t (*app_reply_writer_fn_t)(const app_reply_route_t *route,
                                           const char *line,
                                           void *user_ctx);

esp_err_t app_reply_dispatcher_init(app_reply_writer_fn_t writer, void *user_ctx);
esp_err_t app_reply_dispatcher_start(void);
esp_err_t app_reply_dispatcher_enqueue(app_transport_type_t transport,
                                       uint32_t session_id,
                                       const char *line,
                                       TickType_t timeout);
esp_err_t app_reply_dispatcher_reply(const char *line, void *ctx);
void *app_reply_dispatcher_ctx_clone(void *ctx);
void app_reply_dispatcher_ctx_release(void *ctx);
uint32_t app_reply_dispatcher_get_drops(void);
uint32_t app_reply_dispatcher_get_high_water(void);
