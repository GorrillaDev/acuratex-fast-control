#pragma once

#include "command_processor.h"

// Procesa exclusivamente el namespace uni_. No usa perfiles dinamicos ni
// almacenamiento de archivos: opera con tablas Unificadas compiladas.
esp_err_t app_unified_head_process_line(const char *line,
                                        app_reply_fn_t reply,
                                        void *ctx,
                                        const app_command_env_t *env);
