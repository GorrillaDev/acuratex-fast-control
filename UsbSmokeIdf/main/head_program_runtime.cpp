#include "head_program_runtime.h"

#include "head_program_1_commands.h"
#include "head_program_2_commands.h"
#include "head_program_3_commands.h"
#include "profile_store.h"

static app_head_program_id_t s_active_program = APP_HEAD_PROGRAM_1;

void app_head_program_runtime_init(void)
{
    s_active_program = APP_HEAD_PROGRAM_1;
    if (app_profile_get_active_origin() != APP_PROFILE_ORIGIN_FILE) {
        (void)app_profile_apply_compiled(APP_HEAD_PROGRAM_1);
    }
}

app_head_program_id_t app_head_program_get_active_id(void)
{
    const HeadCommandProfile *profile = app_profile_get_active_command_profile();
    if (profile != NULL) {
        return profile->program_id;
    }

    return s_active_program;
}

const HeadCommandProfile *app_head_program_get_profile(app_head_program_id_t program_id)
{
    switch (program_id) {
    case APP_HEAD_PROGRAM_1:
        return &kProgram1Commands;

    case APP_HEAD_PROGRAM_2:
        return &kProgram2Commands;

    case APP_HEAD_PROGRAM_3:
        return &kProgram3Commands;

    default:
        return &kProgram1Commands;
    }
}

const HeadCommandProfile *app_head_program_get_active_profile(void)
{
    const HeadCommandProfile *profile = app_profile_get_active_command_profile();
    return profile != NULL ? profile : &kProgram1Commands;
}

esp_err_t app_head_program_select(app_head_program_id_t program_id,
                                  app_head_program_id_t *previous_program_id)
{
    if (program_id != APP_HEAD_PROGRAM_1 &&
        program_id != APP_HEAD_PROGRAM_2 &&
        program_id != APP_HEAD_PROGRAM_3) {
        return ESP_ERR_INVALID_ARG;
    }

    if (previous_program_id != NULL) {
        *previous_program_id = app_head_program_get_active_id();
    }

    esp_err_t err = app_profile_apply_compiled(program_id);
    if (err != ESP_OK) {
        return err;
    }

    s_active_program = program_id;
    return ESP_OK;
}