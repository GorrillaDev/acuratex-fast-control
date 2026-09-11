#include "command_unified_head_processor.h"

#include <inttypes.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>

#include "esp_log.h"
#include "esp_timer.h"

#include "head_fast_diag.h"
#include "head_command_frame_builder.h"
#include "head_sequence_executor.h"
#include "head_state_manager.h"
#include "head_unified_program_runtime.h"

static const char *TAG = "uni_head_cmd";

static int app_unified_active_bus(const app_command_env_t *env)
{
    (void)env;
    return APP_UNIFIED_HEAD_PHYSICAL_CAN_BUS;
}

static app_command_env_t app_unified_env_on_can1(const app_command_env_t *env)
{
    app_command_env_t fixed = *env;
    fixed.active_bus = APP_UNIFIED_HEAD_PHYSICAL_CAN_BUS;
    fixed.active_bus_name = "CAN1";
    return fixed;
}

static esp_err_t app_unified_reply_for_line(app_reply_fn_t reply,
                                            void *ctx,
                                            const char *line)
{
    char response[112];
    snprintf(response, sizeof(response), "OK %s", line);
    return reply(response, ctx);
}

static esp_err_t app_unified_send(const HeadCanCommand *frame,
                                  const char *line,
                                  app_reply_fn_t reply,
                                  void *ctx,
                                  const app_command_env_t *env)
{
    if (frame == NULL || env == NULL || env->can_send_standard == NULL
        || frame->dlc > APP_HEAD_PROFILE_MAX_DLC) {
        return reply("ERR|UNI|CAN_TX", ctx);
    }

    esp_err_t err = env->can_send_standard(app_unified_active_bus(env),
                                           frame->can_id,
                                           frame->data,
                                           frame->dlc);
    if (err != ESP_OK) {
        return reply("ERR|UNI|CAN_TX", ctx);
    }

    ESP_LOGI(TAG, "UNI_PROFILE_COMMAND|PROGRAM=%u|COMMAND=%s",
             (unsigned)app_unified_head_program_get_active_id(), line);
    return app_unified_reply_for_line(reply, ctx, line);
}

static bool app_unified_parse_index_value(const char *line,
                                          const char *prefix,
                                          int *index,
                                          int *value)
{
    int parsed_index = 0;
    int parsed_value = 0;
    char extra = '\0';
    size_t prefix_length = strlen(prefix);

    return line != NULL && prefix != NULL
        && strncasecmp(line, prefix, prefix_length) == 0
        && sscanf(line + prefix_length, "%d|%d%c", &parsed_index, &parsed_value, &extra) == 2
        && parsed_index > 0 && parsed_value >= 0 && parsed_value <= 0xFFFF
        && ((*index = parsed_index), true)
        && ((*value = parsed_value), true);
}

static esp_err_t app_unified_position(const char *line,
                                      const char *prefix,
                                      const HeadMotionCommandProfile *commands,
                                      bool selected_position,
                                      app_reply_fn_t reply,
                                      void *ctx,
                                      const app_command_env_t *env)
{
    int index = 0;
    int value = 0;
    HeadCanCommand frame = {};

    if (commands == NULL || !app_unified_parse_index_value(line, prefix, &index, &value)
        || (size_t)index > commands->instance_count) {
        return reply("ERR|UNI|POSITION", ctx);
    }

    if (selected_position) {
        if (commands->positions == NULL || value < 1 || (size_t)value > commands->position_count) {
            return reply("ERR|UNI|POSITION", ctx);
        }
        value = commands->positions[value - 1];
    }

    if (!app_head_build_motion_frame(commands, (size_t)index - 1U, (uint16_t)value, &frame)) {
        return reply("ERR|UNI|POSITION", ctx);
    }
    return app_unified_send(&frame, line, reply, ctx, env);
}

static esp_err_t app_unified_cascade_pin(const char *line,
                                         const char *prefix,
                                         const HeadCascadeCommandProfile *commands,
                                         app_reply_fn_t reply,
                                         void *ctx,
                                         const app_command_env_t *env)
{
    int instance = 0;
    int pin = 0;
    int on = 0;
    char extra = '\0';
    HeadCanCommand frame = {};

    if (commands == NULL || commands->addresses == NULL || commands->addresses_per_instance == 0
        || sscanf(line + strlen(prefix), "%d|%d|%d%c", &instance, &pin, &on, &extra) != 3
        || instance < 1 || (size_t)instance > commands->instance_count
        || pin < 1 || (size_t)pin > commands->addresses_per_instance
        || (on != 0 && on != 1)) {
        return reply("ERR|UNI|BLOCK_PIN", ctx);
    }

    if (!app_head_build_cascade_frame(commands,
                                      (size_t)instance - 1U,
                                      (size_t)pin - 1U,
                                      on != 0,
                                      &frame)) {
        return reply("ERR|UNI|BLOCK_PIN", ctx);
    }
    return app_unified_send(&frame, line, reply, ctx, env);
}

static esp_err_t app_unified_j_output(const char *line,
                                      const HeadCommandProfile *profile,
                                      app_reply_fn_t reply,
                                      void *ctx,
                                      const app_command_env_t *env)
{
    int instance = 0;
    int value = 0;
    int channel = 0;
    char extra = '\0';
    HeadCanCommand frame = {};

    if (profile == NULL) {
        return reply("ERR|UNI|J_PROFILE", ctx);
    }

    if (strncasecmp(line, "uni_j_set_", 10) == 0) {
        if (sscanf(line + 10, "%d|%d%c", &instance, &value, &extra) != 2
            || instance < 1 || (size_t)instance > profile->j.instance_count
            || value < 0 || value > 0xFF) {
            return reply("ERR|UNI|J_SET", ctx);
        }
    } else if (strncasecmp(line, "uni_j_ch_", 9) == 0) {
        uint8_t current = 0;
        if (sscanf(line + 9, "%d_%d%c", &instance, &channel, &extra) != 2
            || instance < 1 || (size_t)instance > profile->j.instance_count
            || channel < 1 || channel > profile->j.channel_count
            || !app_head_state_manager_get_j_physical_register((uint8_t)instance, &current)) {
            return reply("ERR|UNI|J_CH", ctx);
        }
        value = current ^ (1U << (channel - 1));
    } else {
        return reply("ERR|UNI|J_CMD", ctx);
    }

    if (!app_head_build_j_frame(&profile->j, (size_t)instance - 1U, (uint8_t)value, &frame)) {
        return reply("ERR|UNI|J_SET", ctx);
    }
    esp_err_t err = app_unified_send(&frame, line, reply, ctx, env);
    if (err == ESP_OK) {
        (void)app_head_state_manager_commit_j_physical_register((uint8_t)instance, (uint8_t)value);
    }
    return err;
}

static esp_err_t app_unified_select_program(const char *line,
                                            app_reply_fn_t reply,
                                            void *ctx)
{
    app_head_program_id_t requested = APP_HEAD_PROGRAM_1;
    app_head_program_id_t previous = APP_HEAD_PROGRAM_1;

    if (strcasecmp(line, "uni_program_select_1") == 0) {
        requested = APP_HEAD_PROGRAM_1;
    } else if (strcasecmp(line, "uni_program_select_2") == 0) {
        requested = APP_HEAD_PROGRAM_2;
    } else if (strcasecmp(line, "uni_program_select_3") == 0) {
        requested = APP_HEAD_PROGRAM_3;
    } else {
        return reply("ERR UNI_PROGRAM_CMD", ctx);
    }

    if (app_head_state_manager_has_active_motion() || app_head_fast_diag_is_busy()
        || app_unified_head_program_select(requested, &previous) != ESP_OK) {
        return reply("ERR UNI_PROGRAM_BUSY", ctx);
    }

    ESP_LOGI(TAG, "UNI_PROGRAM_SELECT|OLD=%u|NEW=%u", (unsigned)previous, (unsigned)requested);
    return app_unified_reply_for_line(reply, ctx, line);
}

static bool app_unified_prepare_j_run(
    const HeadUnifiedProgramDefinition *definition,
    uint8_t instance,
    const HeadUnifiedCommandContext *context)
{
    return definition->prepare_j_run == NULL
        || definition->prepare_j_run(instance, context);
}

static esp_err_t app_unified_run_command(const char *line,
                                         const HeadUnifiedProgramDefinition *definition,
                                         const HeadCommandProfile *profile,
                                         app_reply_fn_t reply,
                                         void *ctx,
                                         const app_command_env_t *env)
{
    const int bus = app_unified_active_bus(env);
    const uint32_t now_ms = (uint32_t)(esp_timer_get_time() / 1000LL);
    const HeadUnifiedCommandContext command_context = {
        .profile = profile,
        .reply = reply,
        .reply_ctx = ctx,
        .env = env,
        .can_bus = bus,
        .now_ms = now_ms,
    };
    int instance = 0;
    char response[112];

    if (!app_head_state_manager_select_motion_profile(profile)) {
        return reply("ERR|UNI|HEAD_BUSY", ctx);
    }

    if (strcasecmp(line, "uni_den_run_all") == 0) {
        for (uint8_t i = 1; i <= profile->den.instance_count; ++i) {
            if (app_head_profile_instance_is_active(profile->den.active_instance_mask, i - 1U)
                && !app_head_state_manager_start_den_run(i, bus, now_ms)) {
                app_head_state_manager_stop_all_den_runs();
                return reply("ERR|UNI|DEN_RUN_ALL", ctx);
            }
        }
    } else if (strcasecmp(line, "uni_den_stop_all") == 0) {
        app_head_state_manager_stop_all_den_runs();
    } else if (strcasecmp(line, "uni_sic_run_all") == 0) {
        for (uint8_t i = 1; i <= profile->sic.instance_count; ++i) {
            if (app_head_profile_instance_is_active(profile->sic.active_instance_mask, i - 1U)
                && !app_head_state_manager_start_sic_run(i, bus, now_ms)) {
                app_head_state_manager_stop_all_sic_runs();
                return reply("ERR|UNI|SIC_RUN_ALL", ctx);
            }
        }
    } else if (strcasecmp(line, "uni_sic_stop_all") == 0) {
        app_head_state_manager_stop_all_sic_runs();
    } else if (strcasecmp(line, "uni_j_run_all") == 0) {
        for (uint8_t i = 1; i <= profile->j.instance_count; ++i) {
            if (app_head_profile_instance_is_active(profile->j.active_instance_mask, i - 1U)
                && (!app_unified_prepare_j_run(definition, i, &command_context)
                    || !app_head_state_manager_start_j_run(i, bus, now_ms))) return reply("ERR|UNI|J_RUN_ALL", ctx);
        }
    } else if (strcasecmp(line, "uni_j_stop_all") == 0) {
        app_head_state_manager_stop_all_j_runs();
    } else if (strcasecmp(line, "uni_y_run_all") == 0) {
        for (uint8_t i = 1; i <= profile->yarn.instance_count; ++i) {
            if (app_head_profile_instance_is_active(profile->yarn.active_instance_mask, i - 1U)
                && !app_head_state_manager_start_yarn_run(i, bus, now_ms)) return reply("ERR|UNI|Y_RUN_ALL", ctx);
        }
    } else if (strcasecmp(line, "uni_y_stop_all") == 0) {
        app_head_state_manager_stop_all_yarn_runs();
    } else if (strcasecmp(line, "uni_s_run_all") == 0) {
        for (uint8_t i = 1; i <= profile->stitch.instance_count; ++i) {
            if (app_head_profile_instance_is_active(profile->stitch.active_instance_mask, i - 1U)
                && !app_head_state_manager_start_stitch_run(i, bus, now_ms)) return reply("ERR|UNI|S_RUN_ALL", ctx);
        }
    } else if (strcasecmp(line, "uni_s_stop_all") == 0) {
        app_head_state_manager_stop_all_stitch_runs();
    } else if (sscanf(line, "uni_j_run_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->j.instance_count
            || !app_head_profile_instance_is_active(profile->j.active_instance_mask, (size_t)instance - 1U)
            || !app_unified_prepare_j_run(definition,
                                           (uint8_t)instance,
                                           &command_context)
            || !app_head_state_manager_start_j_run((uint8_t)instance, bus, now_ms)) return reply("ERR|UNI|J_RUN", ctx);
    } else if (sscanf(line, "uni_j_stop_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->j.instance_count || !app_head_state_manager_stop_j_run((uint8_t)instance)) return reply("ERR|UNI|J_STOP", ctx);
    } else if (strcasecmp(line, "uni_y1_run") == 0) {
        if (profile->yarn.instance_count < 1 || !app_head_state_manager_start_yarn_run(1, bus, now_ms)) return reply("ERR|UNI|Y1_RUN", ctx);
    } else if (strcasecmp(line, "uni_y1_stop") == 0) {
        if (!app_head_state_manager_stop_yarn_run(1)) return reply("ERR|UNI|Y1_STOP", ctx);
    } else if (strcasecmp(line, "uni_y2_run") == 0) {
        if (profile->yarn.instance_count < 2 || !app_head_state_manager_start_yarn_run(2, bus, now_ms)) return reply("ERR|UNI|Y2_RUN", ctx);
    } else if (strcasecmp(line, "uni_y2_stop") == 0) {
        if (!app_head_state_manager_stop_yarn_run(2)) return reply("ERR|UNI|Y2_STOP", ctx);
    } else if (sscanf(line, "uni_s_run_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->stitch.instance_count
            || !app_head_profile_instance_is_active(profile->stitch.active_instance_mask, (size_t)instance - 1U)
            || !app_head_state_manager_start_stitch_run((uint8_t)instance, bus, now_ms)) return reply("ERR|UNI|S_RUN", ctx);
    } else if (sscanf(line, "uni_s_stop_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->stitch.instance_count || !app_head_state_manager_stop_stitch_run((uint8_t)instance)) return reply("ERR|UNI|S_STOP", ctx);
    } else if (sscanf(line, "uni_den_run1_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->den.instance_count || !app_head_state_manager_start_den_run1((uint8_t)instance, bus, now_ms)) return reply("ERR|UNI|DEN_RUN1", ctx);
    } else if (sscanf(line, "uni_den_stop1_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->den.instance_count || !app_head_state_manager_stop_den_run((uint8_t)instance)) return reply("ERR|UNI|DEN_STOP1", ctx);
    } else if (sscanf(line, "uni_den_run_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->den.instance_count
            || !app_head_profile_instance_is_active(profile->den.active_instance_mask, (size_t)instance - 1U)
            || !app_head_state_manager_start_den_run((uint8_t)instance, bus, now_ms)) return reply("ERR|UNI|DEN_RUN", ctx);
    } else if (sscanf(line, "uni_den_stop_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->den.instance_count || !app_head_state_manager_stop_den_run((uint8_t)instance)) return reply("ERR|UNI|DEN_STOP", ctx);
    } else if (sscanf(line, "uni_sic_run_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->sic.instance_count
            || !app_head_profile_instance_is_active(profile->sic.active_instance_mask, (size_t)instance - 1U)
            || !app_head_state_manager_start_sic_run((uint8_t)instance, bus, now_ms)) return reply("ERR|UNI|SIC_RUN", ctx);
    } else if (sscanf(line, "uni_sic_stop_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->sic.instance_count || !app_head_state_manager_stop_sic_run((uint8_t)instance)) return reply("ERR|UNI|SIC_STOP", ctx);
    } else if (sscanf(line, "uni_feet_run_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->feet.instance_count || !app_head_state_manager_start_feet_run((uint8_t)instance, bus, now_ms)) return reply("ERR|UNI|FEET_RUN", ctx);
    } else if (sscanf(line, "uni_feet_stop_%d", &instance) == 1) {
        if (instance < 1 || (size_t)instance > profile->feet.instance_count || !app_head_state_manager_stop_feet_run((uint8_t)instance)) return reply("ERR|UNI|FEET_STOP", ctx);
    } else {
        return reply("ERR|UNI|RUN_CMD", ctx);
    }

    snprintf(response, sizeof(response), "OK %s", line);
    return reply(response, ctx);
}

esp_err_t app_unified_head_process_line(const char *line,
                                        app_reply_fn_t reply,
                                        void *ctx,
                                        const app_command_env_t *env)
{
    const HeadUnifiedProgramDefinition *definition =
        app_unified_head_program_get_active_definition();
    const HeadCommandProfile *profile =
        definition != NULL ? definition->profile : NULL;

    if (line == NULL || reply == NULL || env == NULL
        || definition == NULL || profile == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    if (strcasecmp(line, "uni_program_select_1") == 0 || strcasecmp(line, "uni_program_select_2") == 0 || strcasecmp(line, "uni_program_select_3") == 0) return app_unified_select_program(line, reply, ctx);
    if (strcasecmp(line, "uni_program_status") == 0) {
        char response[48];
        snprintf(response, sizeof(response), "UNI_PROGRAM_STATE|ACTIVE=%u", (unsigned)app_unified_head_program_get_active_id());
        return reply(response, ctx);
    }
    if (strcasecmp(line, "uni_status") == 0) return reply("OK uni_status", ctx);
    if (strcasecmp(line, "uni_init") == 0) {
        if (app_head_state_manager_has_active_motion() || app_head_fast_diag_is_busy()) return reply("ERR|UNI|INIT_BUSY", ctx);
        (void)app_head_state_manager_init_with_profile(profile);
        const app_command_env_t can1_env = app_unified_env_on_can1(env);
        return app_head_fast_diag_start_init_profile(&can1_env, ctx, profile) == ESP_OK ? reply("OK uni_init", ctx) : reply("ERR|UNI|INIT", ctx);
    }
    if (strcasecmp(line, "uni_testeo") == 0) {
        if (app_head_state_manager_has_active_motion() || app_head_fast_diag_is_busy() || !app_head_fast_diag_testeo_can_start()) return reply("ERR|UNI|TESTEO_BUSY", ctx);
        const app_command_env_t can1_env = app_unified_env_on_can1(env);
        return app_head_fast_diag_start_testeo_profile(&can1_env, ctx, profile) == ESP_OK ? reply("OK uni_testeo", ctx) : reply("ERR|UNI|TESTEO", ctx);
    }
    if (strcasecmp(line, "uni_stop") == 0 || strcasecmp(line, "uni_emergency_stop") == 0) {
        const bool emergency =
            strcasecmp(line, "uni_emergency_stop") == 0;
        if (emergency) {
            app_head_sequence_executor_cancel(
                APP_HEAD_SEQUENCE_CANCEL_EMERGENCY);
        }
        app_head_state_manager_stop_all_motion();
        app_head_fast_diag_request_stop();
        const HeadUnifiedCommandContext command_context = {
            .profile = profile,
            .reply = reply,
            .reply_ctx = ctx,
            .env = env,
            .can_bus = app_unified_active_bus(env),
            .now_ms = (uint32_t)(esp_timer_get_time() / 1000LL),
        };
        if (definition->physical_stop != NULL) {
            return definition->physical_stop(
                line, emergency, &command_context);
        }
        if (!profile->stop.sends_can_frame) return app_unified_reply_for_line(reply, ctx, line);
        return app_unified_send(&profile->stop.frame, line, reply, ctx, env);
    }

    if (!app_head_state_manager_can_use_motion_profile(profile)) return reply("ERR|UNI|HEAD_BUSY", ctx);

    if (definition->handle_special_command != NULL) {
        const HeadUnifiedCommandContext command_context = {
            .profile = profile,
            .reply = reply,
            .reply_ctx = ctx,
            .env = env,
            .can_bus = app_unified_active_bus(env),
            .now_ms = (uint32_t)(esp_timer_get_time() / 1000LL),
        };
        bool handled = false;
        const esp_err_t direct_err = definition->handle_special_command(
            line, &command_context, &handled);
        if (handled) return direct_err;
    }

    if (strncasecmp(line, "uni_den_select_", 15) == 0) return app_unified_position(line, "uni_den_select_", &profile->den, true, reply, ctx, env);
    if (strncasecmp(line, "uni_den_pos_", 12) == 0) return app_unified_position(line, "uni_den_pos_", &profile->den, false, reply, ctx, env);
    if (strncasecmp(line, "uni_sic_select_", 15) == 0) return app_unified_position(line, "uni_sic_select_", &profile->sic, true, reply, ctx, env);
    if (strncasecmp(line, "uni_sic_pos_", 12) == 0) return app_unified_position(line, "uni_sic_pos_", &profile->sic, false, reply, ctx, env);
    if (strncasecmp(line, "uni_feet_select_", 16) == 0) return app_unified_position(line, "uni_feet_select_", &profile->feet, true, reply, ctx, env);
    if (strncasecmp(line, "uni_feet_pos_", 13) == 0) return app_unified_position(line, "uni_feet_pos_", &profile->feet, false, reply, ctx, env);
    if (strncasecmp(line, "uni_j_set_", 10) == 0 || strncasecmp(line, "uni_j_ch_", 9) == 0) return app_unified_j_output(line, profile, reply, ctx, env);
    if (strncasecmp(line, "uni_yarn_pin_", 13) == 0) return app_unified_cascade_pin(line, "uni_yarn_pin_", &profile->yarn, reply, ctx, env);
    if (strncasecmp(line, "uni_stitch_pin_", 15) == 0) return app_unified_cascade_pin(line, "uni_stitch_pin_", &profile->stitch, reply, ctx, env);

    return app_unified_run_command(
        line, definition, profile, reply, ctx, env);
}
