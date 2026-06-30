#include "head_program_runtime.h"

#include "head_program_1_commands.h"
#include "head_program_2_commands.h"

static app_head_program_id_t s_active_program = APP_HEAD_PROGRAM_1;

void app_head_program_runtime_init(void)
{
    s_active_program = APP_HEAD_PROGRAM_1;
}

app_head_program_id_t app_head_program_get_active_id(void)
{
    return s_active_program;
}

const HeadCommandProfile *app_head_program_get_profile(app_head_program_id_t program_id)
{
    switch (program_id) {
    case APP_HEAD_PROGRAM_1:
        return &kProgram1Commands;
    case APP_HEAD_PROGRAM_2:
        return &kProgram2Commands;
    default:
        return &kProgram1Commands;
    }
}

const HeadCommandProfile *app_head_program_get_active_profile(void)
{
    return app_head_program_get_profile(s_active_program);
}

esp_err_t app_head_program_select(app_head_program_id_t program_id,
                                  app_head_program_id_t *previous_program_id)
{
    if (program_id != APP_HEAD_PROGRAM_1 && program_id != APP_HEAD_PROGRAM_2) {
        return ESP_ERR_INVALID_ARG;
    }

    if (previous_program_id != NULL) {
        *previous_program_id = s_active_program;
    }

    s_active_program = program_id;
    return ESP_OK;
}
