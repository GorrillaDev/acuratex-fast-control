#pragma once

#include <stdbool.h>
#include <stddef.h>

#include "esp_err.h"

esp_err_t app_u3_motor_init(void);
bool app_u3_motor_is_command(const char *line);
esp_err_t app_u3_motor_process_line(const char *line, char *response, size_t response_size);
void app_u3_motor_emergency_stop(void);
