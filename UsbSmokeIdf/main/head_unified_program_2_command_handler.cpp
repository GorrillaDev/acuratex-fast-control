#include "head_unified_program_2_command_handler.h"

#include <stdio.h>
#include <string.h>
#include <strings.h>

#include "head_command_frame_builder.h"
#include "head_state_manager.h"

static esp_err_t app_program2_reply_ok(const char *line,
                                       const HeadUnifiedCommandContext *context)
{
    char response[112];
    snprintf(response, sizeof(response), "OK %s", line);
    return context->reply(response, context->reply_ctx);
}

static esp_err_t app_program2_tx(const HeadCanCommand *frame,
                                 const HeadUnifiedCommandContext *context)
{
    if (frame == NULL || context == NULL || context->env == NULL
        || context->env->can_send_standard == NULL
        || frame->dlc > APP_HEAD_PROFILE_MAX_DLC) {
        return ESP_ERR_INVALID_ARG;
    }
    return context->env->can_send_standard(context->can_bus,
                                           frame->can_id,
                                           frame->data,
                                           frame->dlc);
}

static esp_err_t app_program2_all_outputs(
    const char *line,
    bool on,
    const HeadUnifiedCommandContext *context)
{
    const HeadCommandProfile *profile = context->profile;
    app_head_state_manager_stop_all_j_runs();
    app_head_state_manager_stop_all_yarn_runs();
    app_head_state_manager_stop_all_stitch_runs();

    const uint8_t j_value = on
        ? profile->j.on_all_register
        : profile->j.off_all_register;
    for (size_t i = 0; i < profile->j.instance_count; ++i) {
        if (!app_head_profile_instance_is_active(profile->j.active_instance_mask, i)) {
            continue;
        }
        HeadCanCommand frame = {};
        if (!app_head_build_j_frame(&profile->j, i, j_value, &frame)
            || app_program2_tx(&frame, context) != ESP_OK) {
            return context->reply("ERR|UNI|ALL_J_TX", context->reply_ctx);
        }
        (void)app_head_state_manager_commit_j_physical_register(
            (uint8_t)(i + 1U), j_value);
    }

    for (size_t instance = 0; instance < profile->yarn.instance_count; ++instance) {
        for (size_t pin = 0; pin < profile->yarn.addresses_per_instance; ++pin) {
            HeadCanCommand frame = {};
            if (!app_head_build_cascade_frame(&profile->yarn, instance, pin, on, &frame)
                || app_program2_tx(&frame, context) != ESP_OK) {
                return context->reply("ERR|UNI|ALL_YARN_TX", context->reply_ctx);
            }
        }
    }

    for (size_t instance = 0; instance < profile->stitch.instance_count; ++instance) {
        for (size_t pin = 0; pin < profile->stitch.addresses_per_instance; ++pin) {
            HeadCanCommand frame = {};
            if (!app_head_build_cascade_frame(&profile->stitch, instance, pin, on, &frame)
                || app_program2_tx(&frame, context) != ESP_OK) {
                return context->reply("ERR|UNI|ALL_TRANSFER_TX", context->reply_ctx);
            }
        }
    }
    return app_program2_reply_ok(line, context);
}

esp_err_t app_unified_program2_physical_stop(
    const char *line,
    bool emergency,
    const HeadUnifiedCommandContext *context)
{
    if (line == NULL || context == NULL || context->profile == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    // La referencia define STOP como cancelacion de rutinas. El processor ya
    // llamo app_head_state_manager_stop_all_motion(); no se fuerza OFF fisico.
    (void)emergency;
    return app_program2_reply_ok(line, context);
}

esp_err_t app_unified_program2_handle_special_command(
    const char *line,
    const HeadUnifiedCommandContext *context,
    bool *handled)
{
    if (line == NULL || context == NULL || context->profile == NULL
        || context->reply == NULL || handled == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    *handled = true;
    if (strcasecmp(line, "uni_run_all") == 0) {
        // No se autoriza un arranque global automatico mientras DEN, SIC,
        // Feet y Stitch carezcan de una salida de parada fisica verificable.
        return context->reply("ERR|UNI|RUN_ALL_UNSAFE", context->reply_ctx);
    }
    if (strcasecmp(line, "uni_off_all") == 0) {
        return app_program2_all_outputs(line, false, context);
    }
    if (strcasecmp(line, "uni_on_all") == 0) {
        return app_program2_all_outputs(line, true, context);
    }

    *handled = false;
    return ESP_OK;
}
