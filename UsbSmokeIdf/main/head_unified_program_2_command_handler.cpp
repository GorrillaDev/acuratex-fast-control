#include "head_unified_program_2_command_handler.h"

#include <stdio.h>
#include <string.h>
#include <strings.h>

#include "esp_log.h"

#include "head_command_frame_builder.h"
#include "head_fast_diag.h"
#include "head_sequence_executor.h"
#include "head_state_manager.h"
#include "head_unified_program_2_sequence.h"

static const char *TAG = "uni_p2_handler";

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

static esp_err_t app_program2_send_and_reply(
    const HeadCanCommand *frame,
    const char *line,
    const char *error,
    const HeadUnifiedCommandContext *context)
{
    if (app_program2_tx(frame, context) != ESP_OK) {
        return context->reply(error, context->reply_ctx);
    }
    return app_program2_reply_ok(line, context);
}

static esp_err_t app_program2_all_outputs(
    const char *line,
    bool on,
    const HeadUnifiedCommandContext *context)
{
    const HeadCommandProfile *profile = context->profile;
    app_head_sequence_executor_cancel(APP_HEAD_SEQUENCE_CANCEL_STOP);
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

    for (size_t pin = 0; pin < profile->yarn.addresses_per_instance; ++pin) {
        HeadCanCommand frame = {};
        if (!app_head_build_cascade_frame(&profile->yarn, 0U, pin, on, &frame)
            || app_program2_tx(&frame, context) != ESP_OK) {
            return context->reply("ERR|UNI|ALL_YARN_TX", context->reply_ctx);
        }
    }
    return app_program2_reply_ok(line, context);
}

bool app_unified_program2_prepare_j_run(
    uint8_t instance,
    const HeadUnifiedCommandContext *context)
{
    if (context == NULL || context->profile == NULL || instance == 0U) {
        return false;
    }
    HeadCanCommand frame = {};
    const HeadJCommandProfile *commands = &context->profile->j;
    if (!app_head_build_j_frame(commands,
                                (size_t)instance - 1U,
                                commands->initial_register,
                                &frame)
        || app_program2_tx(&frame, context) != ESP_OK) {
        return false;
    }
    return app_head_state_manager_commit_j_physical_register(
        instance, commands->initial_register);
}

esp_err_t app_unified_program2_physical_stop(
    const char *line,
    bool emergency,
    const HeadUnifiedCommandContext *context)
{
    if (line == NULL || context == NULL || context->profile == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    const HeadCommandProfile *profile = context->profile;
    bool j_stop_failed = false;
    bool yarn_stop_failed = false;

    // Fuentes verificadas: can_send_j() con registro FF y yarn_send_state()
    // con estado 00. DEN/SIC/Stitch solo cancelan runtime en el INO; Feet no
    // define STOP. RESET, SYNC y posicion 0 no se reinterpretan como parada.
    for (size_t i = 0; i < profile->j.instance_count; ++i) {
        if (!app_head_profile_instance_is_active(profile->j.active_instance_mask, i)) {
            continue;
        }
        HeadCanCommand frame = {};
        if (!app_head_build_j_frame(&profile->j,
                                    i,
                                    profile->j.off_all_register,
                                    &frame)
            || app_program2_tx(&frame, context) != ESP_OK) {
            ESP_LOGE(TAG, "P2_STOP_TX_FAIL|SUBSYSTEM=J|INSTANCE=%u|EMERGENCY=%u",
                     (unsigned)(i + 1U), emergency ? 1U : 0U);
            j_stop_failed = true;
            continue;
        }
        (void)app_head_state_manager_commit_j_physical_register(
            (uint8_t)(i + 1U), profile->j.off_all_register);
    }

    for (size_t pin = 0; pin < profile->yarn.addresses_per_instance; ++pin) {
        HeadCanCommand frame = {};
        if (!app_head_build_cascade_frame(&profile->yarn, 0U, pin, false, &frame)
            || app_program2_tx(&frame, context) != ESP_OK) {
            ESP_LOGE(TAG, "P2_STOP_TX_FAIL|SUBSYSTEM=YARN|CHANNEL=%u|EMERGENCY=%u",
                     (unsigned)(pin + 1U), emergency ? 1U : 0U);
            yarn_stop_failed = true;
            continue;
        }
    }

    ESP_LOGW(TAG,
             "P2_STOP_PHYSICAL_PARTIAL|KNOWN_OFF=J,YARN|UNCONFIRMED=DEN,SIC,FEET,STITCH,SEQUENCE|EMERGENCY=%u",
             emergency ? 1U : 0U);
    if (j_stop_failed || yarn_stop_failed) {
        const char *subsystem = j_stop_failed && yarn_stop_failed
            ? "J,YARN"
            : (j_stop_failed ? "J" : "YARN");
        char response[80];
        snprintf(response,
                 sizeof(response),
                 "ERR|UNI|STOP_TX|SUBSYSTEM=%s",
                 subsystem);
        return context->reply(response, context->reply_ctx);
    }
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

    int instance = 0;
    int position = 0;
    char extra = '\0';
    HeadCanCommand frame = {};
    *handled = true;

    if (strcasecmp(line, "uni_sequence_status") == 0) {
        const HeadSequenceExecutorStatus status =
            app_head_sequence_executor_get_status();
        char response[112];
        snprintf(response,
                 sizeof(response),
                 "UNI_SEQUENCE_STATE|ACTIVE=%u|STEP=%u|TOTAL=%u",
                 status.state == APP_HEAD_SEQUENCE_STATE_RUNNING ? 1U : 0U,
                 (unsigned)status.current_step,
                 (unsigned)status.total_steps);
        return context->reply(response, context->reply_ctx);
    }
    if (strcasecmp(line, "uni_sequence_stop") == 0) {
        app_head_sequence_executor_cancel(APP_HEAD_SEQUENCE_CANCEL_STOP);
        return app_program2_reply_ok(line, context);
    }
    if (strcasecmp(line, "uni_sequence_start") == 0) {
        if (app_head_fast_diag_is_busy()
            || app_head_state_manager_has_active_motion()
            || !app_head_state_manager_select_motion_profile(context->profile)
            || !app_head_sequence_executor_start(
                &kUnifiedProgram2SequenceDefinition,
                context->can_bus,
                context->now_ms)) {
            return context->reply("ERR|UNI|SEQUENCE_BUSY", context->reply_ctx);
        }
        return app_program2_reply_ok(line, context);
    }
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
    if (app_head_sequence_executor_is_active()) {
        return context->reply("ERR|UNI|SEQUENCE_BUSY", context->reply_ctx);
    }
    if (sscanf(line, "uni_feet_trigger_%d%c", &instance, &extra) == 1) {
        if (instance < 1
            || (size_t)instance > context->profile->feet.instance_count
            || !app_head_build_motion_frame(&context->profile->feet,
                                             (size_t)instance - 1U,
                                             0U,
                                             &frame)) {
            return context->reply("ERR|UNI|FEET_TRIGGER", context->reply_ctx);
        }
        return app_program2_send_and_reply(
            &frame, line, "ERR|UNI|FEET_TRIGGER", context);
    }
    if (sscanf(line, "uni_stitch_reset_%d%c", &instance, &extra) == 1) {
        if (instance < 1
            || (size_t)instance > context->profile->stitch.instance_count
            || !app_head_build_cascade_frame(&context->profile->stitch,
                                              (size_t)instance - 1U,
                                              5U,
                                              true,
                                              &frame)) {
            return context->reply("ERR|UNI|STITCH_RESET", context->reply_ctx);
        }
        (void)app_head_state_manager_stop_stitch_run((uint8_t)instance);
        return app_program2_send_and_reply(
            &frame, line, "ERR|UNI|STITCH_RESET", context);
    }
    if (sscanf(line, "uni_stitch_pos_%d|%d%c", &instance, &position, &extra) == 2) {
        if (instance < 1
            || (size_t)instance > context->profile->stitch.instance_count
            || position < 1 || position > 5
            || !app_head_build_cascade_frame(&context->profile->stitch,
                                              (size_t)instance - 1U,
                                              (size_t)position - 1U,
                                              true,
                                              &frame)) {
            return context->reply("ERR|UNI|STITCH_POSITION", context->reply_ctx);
        }
        (void)app_head_state_manager_stop_stitch_run((uint8_t)instance);
        return app_program2_send_and_reply(
            &frame, line, "ERR|UNI|STITCH_POSITION", context);
    }
    if (sscanf(line, "uni_sic_sync_%d%c", &instance, &extra) == 1) {
        if (instance < 1
            || (size_t)instance > context->profile->sic.instance_count
            || !app_head_build_motion_frame(&context->profile->sic,
                                             (size_t)instance - 1U,
                                             0x7FFFU,
                                             &frame)) {
            return context->reply("ERR|UNI|SIC_SYNC", context->reply_ctx);
        }
        (void)app_head_state_manager_stop_sic_run((uint8_t)instance);
        return app_program2_send_and_reply(
            &frame, line, "ERR|UNI|SIC_SYNC", context);
    }
    if (strncasecmp(line, "uni_feet_select_", 16) == 0
        || strncasecmp(line, "uni_feet_pos_", 13) == 0
        || strncasecmp(line, "uni_stitch_pin_", 15) == 0) {
        return context->reply("ERR|UNI|P2_USE_EXPLICIT_COMMAND",
                              context->reply_ctx);
    }

    *handled = false;
    return ESP_OK;
}
