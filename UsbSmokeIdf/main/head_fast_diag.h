#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"

#include "command_processor.h"

esp_err_t app_head_fast_diag_start_testeo(const app_command_env_t *env,
                                          void *reply_ctx);

esp_err_t app_head_fast_diag_start_init(const app_command_env_t *env,
                                        void *reply_ctx);

void app_head_fast_diag_request_stop(void);

bool app_head_fast_diag_is_busy(void);

bool app_head_fast_diag_testeo_can_start(void);
