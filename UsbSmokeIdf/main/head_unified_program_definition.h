#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "command_processor.h"
#include "head_command_profile.h"

typedef struct {
    const HeadCommandProfile *profile;
    app_reply_fn_t reply;
    void *reply_ctx;
    const app_command_env_t *env;
    int can_bus;
    uint32_t now_ms;
} HeadUnifiedCommandContext;

typedef esp_err_t (*app_head_unified_special_command_fn_t)(
    const char *line,
    const HeadUnifiedCommandContext *context,
    bool *handled);
typedef bool (*app_head_unified_prepare_j_run_fn_t)(
    uint8_t instance,
    const HeadUnifiedCommandContext *context);
typedef esp_err_t (*app_head_unified_physical_stop_fn_t)(
    const char *line,
    bool emergency,
    const HeadUnifiedCommandContext *context);

typedef struct {
    const HeadCommandProfile *profile;
    app_head_unified_special_command_fn_t handle_special_command;
    app_head_unified_prepare_j_run_fn_t prepare_j_run;
    app_head_unified_physical_stop_fn_t physical_stop;
} HeadUnifiedProgramDefinition;
