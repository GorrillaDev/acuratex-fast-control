#pragma once

#include <stddef.h>
#include "esp_err.h"

#ifdef __cplusplus
extern "C" {
#endif

esp_err_t app_line_drive_init(void);
bool app_line_drive_is_command(const char *line);
esp_err_t app_line_drive_process_line(const char *line, char *response, size_t response_size);
void app_line_drive_safe_stop(void);
int64_t app_line_drive_operator_units_to_steps(int64_t operator_units);

#ifdef __cplusplus
}
#endif