#pragma once

#include "head_unified_program_definition.h"

esp_err_t app_unified_program2_handle_special_command(
    const char *line,
    const HeadUnifiedCommandContext *context,
    bool *handled);
esp_err_t app_unified_program2_physical_stop(
    const char *line,
    bool emergency,
    const HeadUnifiedCommandContext *context);
