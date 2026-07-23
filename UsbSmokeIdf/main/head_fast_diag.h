#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"
#include "head_command_profile.h"

#include "command_processor.h"

// Variante de perfil compilado usada por la ruta uni_ sin runtime Modular.
esp_err_t app_head_fast_diag_start_testeo_profile(const app_command_env_t *env,
                                                  void *reply_ctx,
                                                  const HeadCommandProfile *profile);

esp_err_t app_head_fast_diag_start_testeo(const app_command_env_t *env,
                                          void *reply_ctx);

esp_err_t app_head_fast_diag_start_init_profile(const app_command_env_t *env,
                                                void *reply_ctx,
                                                const HeadCommandProfile *profile);

esp_err_t app_head_fast_diag_start_init(const app_command_env_t *env,
                                        void *reply_ctx);

void app_head_fast_diag_request_stop(void);

bool app_head_fast_diag_is_busy(void);

bool app_head_fast_diag_testeo_can_start(void);
