#include "head_unified_program_runtime.h"

#include "head_unified_program_1_commands.h"
#include "head_unified_program_2_commands.h"
#include "head_unified_program_3_commands.h"

// El Unificado tiene una seleccion logica propia. No consulta profile_store ni
// las rutas ACX/TXT del runtime Modular.
static app_head_program_id_t s_unified_active_program = APP_HEAD_PROGRAM_1;

void app_unified_head_program_runtime_init(void)
{
    s_unified_active_program = APP_HEAD_PROGRAM_1;
}

app_head_program_id_t app_unified_head_program_get_active_id(void)
{
    return s_unified_active_program;
}

const HeadCommandProfile *app_unified_head_program_get_profile(app_head_program_id_t program_id)
{
    switch (program_id) {
    case APP_HEAD_PROGRAM_1:
        return &kUnifiedProgram1Commands;
    case APP_HEAD_PROGRAM_2:
        return &kUnifiedProgram2Commands;
    case APP_HEAD_PROGRAM_3:
        return &kUnifiedProgram3Commands;
    default:
        return NULL;
    }
}

const HeadCommandProfile *app_unified_head_program_get_active_profile(void)
{
    return app_unified_head_program_get_profile(s_unified_active_program);
}

esp_err_t app_unified_head_program_select(app_head_program_id_t program_id,
                                          app_head_program_id_t *previous_program_id)
{
    if (app_unified_head_program_get_profile(program_id) == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    if (previous_program_id != NULL) {
        *previous_program_id = s_unified_active_program;
    }

    s_unified_active_program = program_id;
    return ESP_OK;
}
