#pragma once

#include "esp_err.h"

#include "head_command_profile.h"

void app_head_program_runtime_init(void);
app_head_program_id_t app_head_program_get_active_id(void);
const HeadCommandProfile *app_head_program_get_profile(app_head_program_id_t program_id);
const HeadCommandProfile *app_head_program_get_active_profile(void);
esp_err_t app_head_program_select(app_head_program_id_t program_id,
                                 app_head_program_id_t *previous_program_id);
