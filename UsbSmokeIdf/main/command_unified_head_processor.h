#pragma once

#include "command_processor.h"

// Todo el Cabezal Unificado, incluido Programa 2, pertenece al transceiver
// fisico CAN1 (GPIO4/5/STBY6). No debe heredar la seleccion U3/CAN2.
inline constexpr int APP_UNIFIED_HEAD_PHYSICAL_CAN_BUS = APP_CMD_CAN_BUS_1;

// Procesa exclusivamente el namespace uni_. No usa perfiles dinamicos ni
// almacenamiento de archivos: opera con tablas Unificadas compiladas.
esp_err_t app_unified_head_process_line(const char *line,
                                        app_reply_fn_t reply,
                                        void *ctx,
                                        const app_command_env_t *env);
