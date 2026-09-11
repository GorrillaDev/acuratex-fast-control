#include "head_unified_program_runtime.h"

#include "head_unified_program_1_commands.h"
#include "head_unified_program_2_commands.h"
#include "head_unified_program_2_command_handler.h"
#include "head_unified_program_3_commands.h"

// El Unificado tiene una seleccion logica propia. No consulta profile_store ni
// las rutas ACX/TXT del runtime Modular.
static app_head_program_id_t s_unified_active_program = APP_HEAD_PROGRAM_1;

static const HeadUnifiedProgramDefinition kProgram1Definition = {
    .profile = &kUnifiedProgram1Commands,
    .handle_special_command = NULL,
    .prepare_j_run = NULL,
    .physical_stop = NULL,
};

static const HeadUnifiedProgramDefinition kProgram2Definition = {
    .profile = &kUnifiedProgram2Commands,
    .handle_special_command = app_unified_program2_handle_special_command,
    .prepare_j_run = NULL,
    .physical_stop = app_unified_program2_physical_stop,
};

static const HeadUnifiedProgramDefinition kProgram3Definition = {
    .profile = &kUnifiedProgram3Commands,
    .handle_special_command = NULL,
    .prepare_j_run = NULL,
    .physical_stop = NULL,
};

void app_unified_head_program_runtime_init(void)
{
    s_unified_active_program = APP_HEAD_PROGRAM_1;
}

app_head_program_id_t app_unified_head_program_get_active_id(void)
{
    return s_unified_active_program;
}

const HeadUnifiedProgramDefinition *app_unified_head_program_get_definition(
    app_head_program_id_t program_id)
{
    switch (program_id) {
    case APP_HEAD_PROGRAM_1:
        return &kProgram1Definition;
    case APP_HEAD_PROGRAM_2:
        return &kProgram2Definition;
    case APP_HEAD_PROGRAM_3:
        return &kProgram3Definition;
    default:
        return NULL;
    }
}

const HeadUnifiedProgramDefinition *app_unified_head_program_get_active_definition(void)
{
    return app_unified_head_program_get_definition(s_unified_active_program);
}

const HeadCommandProfile *app_unified_head_program_get_profile(app_head_program_id_t program_id)
{
    const HeadUnifiedProgramDefinition *definition =
        app_unified_head_program_get_definition(program_id);
    return definition != NULL ? definition->profile : NULL;
}

const HeadCommandProfile *app_unified_head_program_get_active_profile(void)
{
    const HeadUnifiedProgramDefinition *definition =
        app_unified_head_program_get_active_definition();
    return definition != NULL ? definition->profile : NULL;
}

esp_err_t app_unified_head_program_select(app_head_program_id_t program_id,
                                          app_head_program_id_t *previous_program_id)
{
    if (app_unified_head_program_get_definition(program_id) == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    if (previous_program_id != NULL) {
        *previous_program_id = s_unified_active_program;
    }

    s_unified_active_program = program_id;
    return ESP_OK;
}
