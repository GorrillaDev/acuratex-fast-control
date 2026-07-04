#include "profile_store.h"

#include <ctype.h>
#include <dirent.h>
#include <errno.h>
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"

#include "command_processor.h"
#include "head_program_runtime.h"
#include "line_codec.h"

static const char *TAG = "profile_store";

static HeadRuntimeProfile s_profiles[2];
static size_t s_active_index = 0;
static SemaphoreHandle_t s_profile_mutex = NULL;
static bool s_loading = false;
static char s_loading_name[APP_PROFILE_MAX_FILENAME_LEN + 1];
static uint32_t s_generation = 0;

typedef struct {
    bool valid;
    bool is_unified;
    uint16_t program_number;
} app_profile_filename_info_t;

static void app_profile_trim(char *line)
{
    app_trim_line(line);
}

static bool app_profile_has_prefix_token(const char *line, const char *prefix)
{
    size_t len;
    char next;

    if (line == NULL || prefix == NULL) {
        return false;
    }

    len = strlen(prefix);
    if (strncasecmp(line, prefix, len) != 0) {
        return false;
    }

    next = line[len];
    return next == '\0' || next == ' ' || next == '\t' || next == '|';
}

static uint32_t app_profile_crc32_update(uint32_t crc, const void *data, size_t len)
{
    const uint8_t *bytes = (const uint8_t *)data;

    crc = ~crc;
    for (size_t i = 0; i < len; ++i) {
        crc ^= bytes[i];
        for (int bit = 0; bit < 8; ++bit) {
            uint32_t mask = (uint32_t)-(int32_t)(crc & 1U);
            crc = (crc >> 1U) ^ (0xEDB88320U & mask);
        }
    }

    return ~crc;
}

static bool app_profile_parse_u32(const char *text, uint32_t *value)
{
    char *endptr = NULL;
    unsigned long parsed;

    if (text == NULL || value == NULL || text[0] == '\0') {
        return false;
    }

    errno = 0;
    parsed = strtoul(text, &endptr, 0);
    if (errno != 0 || endptr == text || *endptr != '\0' || parsed > UINT32_MAX) {
        return false;
    }

    *value = (uint32_t)parsed;
    return true;
}

static bool app_profile_parse_non_negative_int(const char *text, int *value)
{
    char *endptr = NULL;
    long parsed;

    if (text == NULL || value == NULL || text[0] == '\0') {
        return false;
    }

    errno = 0;
    parsed = strtol(text, &endptr, 10);
    if (errno != 0 || endptr == text || *endptr != '\0' || parsed < 0 || parsed > INT16_MAX) {
        return false;
    }

    *value = (int)parsed;
    return true;
}

static bool app_profile_parse_filename(const char *name, app_profile_filename_info_t *out)
{
    const char *prefix_uni = "cbz.uni.prog";
    const char *prefix_mod = "cbz.mod.prog";
    const char *number_start = NULL;
    const char *dot_txt = NULL;
    unsigned long program = 0;
    char number[8];
    size_t len;
    size_t number_len;

    if (out != NULL) {
        memset(out, 0, sizeof(*out));
    }

    if (name == NULL) {
        return false;
    }

    len = strlen(name);
    if (len == 0 || len > APP_PROFILE_MAX_FILENAME_LEN) {
        return false;
    }

    if (strstr(name, "..") != NULL
        || strchr(name, '/') != NULL
        || strchr(name, '\\') != NULL
        || strchr(name, '|') != NULL
        || strchr(name, '\r') != NULL
        || strchr(name, '\n') != NULL) {
        return false;
    }

    if (strncasecmp(name, prefix_uni, strlen(prefix_uni)) == 0) {
        number_start = name + strlen(prefix_uni);
        if (out != NULL) {
            out->is_unified = true;
        }
    } else if (strncasecmp(name, prefix_mod, strlen(prefix_mod)) == 0) {
        number_start = name + strlen(prefix_mod);
        if (out != NULL) {
            out->is_unified = false;
        }
    } else {
        return false;
    }

    if (number_start == NULL || number_start[0] == '\0' || number_start[0] == '0') {
        return false;
    }

    dot_txt = strstr(number_start, ".txt");
    if (dot_txt == NULL || strcasecmp(dot_txt, ".txt") != 0 || dot_txt == number_start) {
        return false;
    }

    number_len = (size_t)(dot_txt - number_start);
    if (number_len == 0 || number_len >= sizeof(number)) {
        return false;
    }

    for (const char *p = number_start; p < dot_txt; ++p) {
        if (!isdigit((unsigned char)*p)) {
            return false;
        }
    }

    memcpy(number, number_start, number_len);
    number[number_len] = '\0';
    program = strtoul(number, NULL, 10);
    if (program < 1 || program > 3) {
        return false;
    }

    if (out != NULL) {
        out->valid = true;
        out->program_number = (uint16_t)program;
    }

    return true;
}

bool app_profile_is_valid_filename(const char *name)
{
    return app_profile_parse_filename(name, NULL);
}

static bool app_profile_build_path(const char *name, char *path, size_t path_len)
{
    int written;

    if (!app_profile_is_valid_filename(name) || path == NULL || path_len == 0) {
        return false;
    }

    written = snprintf(path, path_len, "%s/%s", APP_PROFILE_FS_BASE, name);
    return written > 0 && (size_t)written < path_len;
}

static app_head_program_id_t app_profile_program_id_from_number(uint16_t program_number)
{
    switch (program_number) {
    case 2:
        return APP_HEAD_PROGRAM_2;
    case 3:
        return APP_HEAD_PROGRAM_3;
    case 1:
    default:
        return APP_HEAD_PROGRAM_1;
    }
}

static void app_profile_rebuild_view_pointers(HeadRuntimeProfile *profile,
                                              const HeadRuntimeProfile *source_for_rebase)
{
    if (profile == NULL) {
        return;
    }

    profile->command_profile.program_name = profile->profile_name;
    profile->command_profile.init_sequence.phase1_steps = profile->init_phase1_ptrs;
    profile->command_profile.init_sequence.phase2_steps = profile->init_phase2_ptrs;
    profile->command_profile.den.run_sequence = profile->den_run_sequence;
    profile->command_profile.den.alternate_run_sequence = profile->den_alternate_run_sequence;
    profile->command_profile.den.positions = profile->den_positions;
    profile->command_profile.sic.run_sequence = profile->sic_run_sequence;
    profile->command_profile.sic.positions = profile->sic_positions;
    profile->command_profile.feet.run_sequence = profile->feet_run_sequence;
    profile->command_profile.feet.positions = profile->feet_positions;
    profile->command_profile.yarn.addresses = profile->yarn_addresses;
    profile->command_profile.stitch.addresses = profile->stitch_addresses;

    if (source_for_rebase != NULL && source_for_rebase != profile) {
        for (size_t i = 0; i < profile->command_profile.init_sequence.phase1_step_count; ++i) {
            const char *src = source_for_rebase->init_phase1_ptrs[i];
            if (src >= source_for_rebase->init_text
                && src < source_for_rebase->init_text + sizeof(source_for_rebase->init_text)) {
                profile->init_phase1_ptrs[i] = profile->init_text + (src - source_for_rebase->init_text);
            }
        }

        for (size_t i = 0; i < profile->command_profile.init_sequence.phase2_step_count; ++i) {
            const char *src = source_for_rebase->init_phase2_ptrs[i];
            if (src >= source_for_rebase->init_text
                && src < source_for_rebase->init_text + sizeof(source_for_rebase->init_text)) {
                profile->init_phase2_ptrs[i] = profile->init_text + (src - source_for_rebase->init_text);
            }
        }
    }
}

static bool app_profile_store_init_line(HeadRuntimeProfile *profile,
                                        const char *line,
                                        const char **out)
{
    size_t len;
    char *target;

    if (profile == NULL || line == NULL || out == NULL) {
        return false;
    }

    len = strlen(line);
    if (len == 0 || profile->init_text_used + len + 1U > sizeof(profile->init_text)) {
        return false;
    }

    target = profile->init_text + profile->init_text_used;
    memcpy(target, line, len + 1U);
    *out = target;
    profile->init_text_used += len + 1U;
    return true;
}

static bool app_profile_copy_init_sequence(HeadRuntimeProfile *dst,
                                           const HeadCommandProfile *base)
{
    if (dst == NULL || base == NULL) {
        return false;
    }

    if (base->init_sequence.phase1_step_count > APP_PROFILE_MAX_INIT_STEPS
        || base->init_sequence.phase2_step_count > APP_PROFILE_MAX_INIT_STEPS) {
        return false;
    }

    dst->command_profile.init_sequence.phase1_step_count = base->init_sequence.phase1_step_count;
    dst->command_profile.init_sequence.phase1_step_delay_ms = base->init_sequence.phase1_step_delay_ms;
    dst->command_profile.init_sequence.phase_gap_ms = base->init_sequence.phase_gap_ms;
    dst->command_profile.init_sequence.phase2_step_count = base->init_sequence.phase2_step_count;
    dst->command_profile.init_sequence.phase2_step_delay_ms = base->init_sequence.phase2_step_delay_ms;

    for (size_t i = 0; i < base->init_sequence.phase1_step_count; ++i) {
        if (!app_profile_store_init_line(dst, base->init_sequence.phase1_steps[i], &dst->init_phase1_ptrs[i])) {
            return false;
        }
    }

    for (size_t i = 0; i < base->init_sequence.phase2_step_count; ++i) {
        if (!app_profile_store_init_line(dst, base->init_sequence.phase2_steps[i], &dst->init_phase2_ptrs[i])) {
            return false;
        }
    }

    return true;
}

static bool app_profile_copy_u8_array(uint8_t *dst,
                                      size_t dst_count,
                                      const uint8_t *src,
                                      size_t src_count)
{
    if (src_count > dst_count) {
        return false;
    }

    if (src_count > 0 && (dst == NULL || src == NULL)) {
        return false;
    }

    if (src_count > 0) {
        memcpy(dst, src, src_count);
    }

    return true;
}

static bool app_profile_copy_u16_array(uint16_t *dst,
                                       size_t dst_count,
                                       const uint16_t *src,
                                       size_t src_count)
{
    if (src_count > dst_count) {
        return false;
    }

    if (src_count > 0 && (dst == NULL || src == NULL)) {
        return false;
    }

    if (src_count > 0) {
        memcpy(dst, src, src_count * sizeof(uint16_t));
    }

    return true;
}

static bool app_profile_copy_compiled_defaults(HeadRuntimeProfile *dst,
                                               app_head_program_id_t program_id)
{
    const HeadCommandProfile *base = app_head_program_get_profile(program_id);

    if (dst == NULL || base == NULL) {
        return false;
    }

    dst->command_profile = *base;
    dst->command_profile.program_id = program_id;

    if (dst->profile_name[0] == '\0') {
        strlcpy(dst->profile_name, base->program_name, sizeof(dst->profile_name));
    }

    if (!app_profile_copy_init_sequence(dst, base)) {
        return false;
    }

    if (!app_profile_copy_u8_array(dst->den_run_sequence,
                                   sizeof(dst->den_run_sequence),
                                   base->den.run_sequence,
                                   base->den.run_sequence_count)
        || !app_profile_copy_u8_array(dst->den_alternate_run_sequence,
                                      sizeof(dst->den_alternate_run_sequence),
                                      base->den.alternate_run_sequence,
                                      base->den.alternate_run_sequence_count)
        || !app_profile_copy_u8_array(dst->sic_run_sequence,
                                      sizeof(dst->sic_run_sequence),
                                      base->sic.run_sequence,
                                      base->sic.run_sequence_count)
        || !app_profile_copy_u8_array(dst->feet_run_sequence,
                                      sizeof(dst->feet_run_sequence),
                                      base->feet.run_sequence,
                                      base->feet.run_sequence_count)
        || !app_profile_copy_u16_array(dst->den_positions,
                                       sizeof(dst->den_positions) / sizeof(dst->den_positions[0]),
                                       base->den.positions,
                                       base->den.position_count)
        || !app_profile_copy_u16_array(dst->sic_positions,
                                       sizeof(dst->sic_positions) / sizeof(dst->sic_positions[0]),
                                       base->sic.positions,
                                       base->sic.position_count)
        || !app_profile_copy_u16_array(dst->feet_positions,
                                       sizeof(dst->feet_positions) / sizeof(dst->feet_positions[0]),
                                       base->feet.positions,
                                       base->feet.position_count)
        || !app_profile_copy_u8_array(dst->yarn_addresses,
                                      sizeof(dst->yarn_addresses),
                                      base->yarn.addresses,
                                      base->yarn.addresses_per_instance * base->yarn.instance_count)
        || !app_profile_copy_u8_array(dst->stitch_addresses,
                                      sizeof(dst->stitch_addresses),
                                      base->stitch.addresses,
                                      base->stitch.addresses_per_instance * base->stitch.instance_count)) {
        return false;
    }

    dst->modules.den_count = (int)base->den.instance_count;
    dst->modules.sic_count = (int)base->sic.instance_count;
    dst->modules.j_count = (int)base->j.instance_count;
    dst->modules.yarn_count = (int)base->yarn.instance_count;
    dst->modules.stitch_count = (int)base->stitch.instance_count;
    dst->modules.feet_count = (int)base->feet.instance_count;

    app_profile_rebuild_view_pointers(dst, dst);
    return true;
}

static void app_profile_apply_module_counts_to_view(HeadRuntimeProfile *profile)
{
    if (profile == NULL) {
        return;
    }

    profile->command_profile.den.instance_count = (size_t)profile->modules.den_count;
    profile->command_profile.sic.instance_count = (size_t)profile->modules.sic_count;
    profile->command_profile.j.instance_count = (size_t)profile->modules.j_count;
    profile->command_profile.yarn.instance_count = (size_t)profile->modules.yarn_count;
    profile->command_profile.stitch.instance_count = (size_t)profile->modules.stitch_count;
    profile->command_profile.feet.instance_count = (size_t)profile->modules.feet_count;
}

static bool app_profile_system_token_matches(bool filename_is_unified, const char *value)
{
    if (value == NULL) {
        return false;
    }

    if (filename_is_unified) {
        return strcasecmp(value, "UNI") == 0 || strcasecmp(value, "UNIFIED") == 0;
    }

    return strcasecmp(value, "MOD") == 0 || strcasecmp(value, "MODULAR") == 0;
}

static bool app_profile_is_valid_script_name(const char *name)
{
    size_t len;

    if (name == NULL) {
        return false;
    }

    len = strlen(name);
    if (len == 0 || len > APP_PROFILE_MAX_FILENAME_LEN) {
        return false;
    }

    return len >= 4
        && strcasecmp(name + len - 4, ".txt") == 0
        && strstr(name, "..") == NULL
        && strchr(name, '/') == NULL
        && strchr(name, '\\') == NULL
        && strchr(name, '|') == NULL
        && strchr(name, '\r') == NULL
        && strchr(name, '\n') == NULL
        && strcasecmp(name, ".selected") != 0
        && strcasecmp(name, ".upload.tmp") != 0;
}

static bool app_profile_parse_wait_ms(const char *line, int *milliseconds)
{
    char buffer[APP_PROFILE_MAX_LINE_LEN];
    char *saveptr = NULL;
    char *token = NULL;
    char *value_text = NULL;
    int parsed = 0;

    if (line == NULL || milliseconds == NULL) {
        return false;
    }

    strlcpy(buffer, line, sizeof(buffer));
    token = strtok_r(buffer, " \t|", &saveptr);
    if (token == NULL
        || (strcasecmp(token, "WAIT") != 0 && strcasecmp(token, "DELAY") != 0)) {
        return false;
    }

    value_text = strtok_r(NULL, " \t|", &saveptr);
    if (value_text == NULL || !app_profile_parse_non_negative_int(value_text, &parsed)) {
        return false;
    }

    if (parsed > APP_PROFILE_MAX_WAIT_MS) {
        return false;
    }

    *milliseconds = parsed;
    return true;
}

static bool app_profile_contains_dynamic_placeholder(const char *line)
{
    char buffer[APP_PROFILE_MAX_LINE_LEN];
    char *saveptr = NULL;
    char *token = NULL;

    if (line == NULL) {
        return false;
    }

    strlcpy(buffer, line, sizeof(buffer));
    token = strtok_r(buffer, " \t|", &saveptr);
    while (token != NULL) {
        if (strcasecmp(token, "XX") == 0) {
            return true;
        }
        token = strtok_r(NULL, " \t|", &saveptr);
    }

    return false;
}

static bool app_profile_is_forbidden_action_line(const char *line)
{
    static const char *const forbidden[] = {
        "FILE_BEGIN",
        "FILE_DATA",
        "FILE_END",
        "FILE_DELETE",
        "FILE_GET",
        "FILE_GET_NEXT",
        "FILE_LIST",
        "FILE_SELECT",
        "PROFILE_LIST",
        "PROFILE_SELECT",
        "PROFILE_INFO",
        "PROFILE_ACTIVE",
        "HEAD_PROGRAM_SELECT",
        "HEAD_ACTION",
        "HEAD_STOP",
        "HEAD_STATUS",
        "SCRIPT_RUN",
        "SCRIPT_STOP",
        "SCRIPT_STATUS",
        "emergency_stop",
    };

    for (size_t i = 0; i < sizeof(forbidden) / sizeof(forbidden[0]); ++i) {
        if (app_profile_has_prefix_token(line, forbidden[i])) {
            return true;
        }
    }

    return false;
}

static bool app_profile_store_command_text(HeadRuntimeProfile *profile,
                                           const char *line,
                                           uint16_t *offset,
                                           uint16_t *length)
{
    size_t len;

    if (profile == NULL || line == NULL || offset == NULL || length == NULL) {
        return false;
    }

    len = strlen(line);
    if (len == 0 || len > UINT16_MAX
        || profile->command_text_used + len + 1U > sizeof(profile->command_text)) {
        return false;
    }

    *offset = (uint16_t)profile->command_text_used;
    *length = (uint16_t)len;
    memcpy(profile->command_text + profile->command_text_used, line, len + 1U);
    profile->command_text_used += len + 1U;
    return true;
}

static bool app_profile_add_action_command(HeadRuntimeProfile *profile,
                                           HeadRuntimeAction *action,
                                           const char *line,
                                           int line_number)
{
    HeadRuntimeCommand *command = NULL;
    int wait_ms = 0;
    const char *payload = NULL;
    bool dynamic_send = false;

    if (profile == NULL || action == NULL || line == NULL) {
        return false;
    }

    if (profile->command_count >= APP_PROFILE_MAX_COMMANDS) {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=MAX_COMMANDS", line_number);
        return false;
    }

    if (app_profile_is_forbidden_action_line(line)) {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=FORBIDDEN_COMMAND|TEXT=%s", line_number, line);
        return false;
    }

    command = &profile->commands[profile->command_count];
    memset(command, 0, sizeof(*command));
    command->line_number = line_number;
    command->can_bus = APP_CMD_CAN_BUS_NONE;

    if (!app_profile_store_command_text(profile, line, &command->text_offset, &command->text_len)) {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=COMMAND_TEXT_LIMIT", line_number);
        return false;
    }

    if (app_profile_parse_wait_ms(line, &wait_ms)) {
        command->type = APP_PROFILE_RUNTIME_COMMAND_WAIT;
        command->wait_ms = wait_ms;
    } else if (strcasecmp(line, "can1") == 0 || strcasecmp(line, "can2") == 0) {
        command->type = APP_PROFILE_RUNTIME_COMMAND_CAN_SELECT;
        command->can_bus = strcasecmp(line, "can2") == 0 ? APP_CMD_CAN_BUS_2 : APP_CMD_CAN_BUS_1;
    } else if (strcasecmp(line, "status") == 0) {
        command->type = APP_PROFILE_RUNTIME_COMMAND_STATUS;
    } else {
        char parse_buffer[APP_PROFILE_MAX_LINE_LEN];
        uint32_t id = 0;
        uint8_t data[APP_HEAD_PROFILE_MAX_DLC] = {0};
        size_t dlc = 0;

        if (app_profile_has_prefix_token(line, "send")) {
            payload = line + 4;
        } else if (app_profile_has_prefix_token(line, "CAN")) {
            payload = line + 3;
        } else {
            payload = line;
        }

        while (*payload == ' ' || *payload == '\t' || *payload == '|') {
            payload++;
        }

        dynamic_send = app_profile_contains_dynamic_placeholder(payload);
        if (dynamic_send) {
            command->type = APP_PROFILE_RUNTIME_COMMAND_SEND_DYNAMIC;
            command->uses_dynamic_j_placeholder = true;
        } else {
            strlcpy(parse_buffer, payload, sizeof(parse_buffer));
            if (!app_parse_frame_line(parse_buffer, &id, data, &dlc, APP_HEAD_PROFILE_MAX_DLC, 0x7FFU)) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=UNSUPPORTED_COMMAND|TEXT=%s", line_number, line);
                return false;
            }

            command->type = APP_PROFILE_RUNTIME_COMMAND_SEND;
            command->can_id = id;
            command->dlc = (uint8_t)dlc;
            memcpy(command->data, data, dlc);
        }
    }

    profile->command_count++;
    action->command_count++;
    return true;
}

static HeadRuntimeAction *app_profile_find_action_mutable(HeadRuntimeProfile *profile,
                                                          const char *action_name)
{
    if (profile == NULL || action_name == NULL) {
        return NULL;
    }

    for (size_t i = 0; i < profile->action_count; ++i) {
        if (strcasecmp(profile->actions[i].name, action_name) == 0) {
            return &profile->actions[i];
        }
    }

    return NULL;
}

const HeadRuntimeAction *app_profile_find_action(const HeadRuntimeProfile *profile,
                                                 const char *action_name)
{
    if (profile == NULL || action_name == NULL) {
        return NULL;
    }

    for (size_t i = 0; i < profile->action_count; ++i) {
        if (strcasecmp(profile->actions[i].name, action_name) == 0) {
            return &profile->actions[i];
        }
    }

    return NULL;
}

const char *app_profile_command_text(const HeadRuntimeProfile *profile,
                                     const HeadRuntimeCommand *command)
{
    if (profile == NULL || command == NULL
        || command->text_offset >= sizeof(profile->command_text)
        || (size_t)command->text_offset + command->text_len >= sizeof(profile->command_text)) {
        return "";
    }

    return profile->command_text + command->text_offset;
}

static bool app_profile_parse_begin_header(const char *line,
                                           char *action,
                                           size_t action_len,
                                           bool *has_value,
                                           int *value)
{
    char buffer[APP_PROFILE_MAX_LINE_LEN];
    char *saveptr = NULL;
    char *token = NULL;
    char *action_token = NULL;

    if (line == NULL || action == NULL || action_len == 0 || has_value == NULL || value == NULL) {
        return false;
    }

    if (strncasecmp(line, "BEGIN|", 6) != 0) {
        return false;
    }

    strlcpy(buffer, line, sizeof(buffer));
    token = strtok_r(buffer, "|", &saveptr);
    if (token == NULL || strcasecmp(token, "BEGIN") != 0) {
        return false;
    }

    action_token = strtok_r(NULL, "|", &saveptr);
    if (action_token == NULL) {
        return false;
    }

    app_profile_trim(action_token);
    if (action_token[0] == '\0' || strlen(action_token) >= action_len || strchr(action_token, '|') != NULL) {
        return false;
    }

    strlcpy(action, action_token, action_len);
    *has_value = false;
    *value = 0;

    while ((token = strtok_r(NULL, "|", &saveptr)) != NULL) {
        app_profile_trim(token);
        if (strncasecmp(token, "VALUE=", 6) == 0) {
            int parsed = 0;
            if (app_profile_parse_non_negative_int(token + 6, &parsed)) {
                *has_value = true;
                *value = parsed;
            }
        }
    }

    return true;
}

static bool app_profile_parse_module_line(HeadRuntimeProfile *profile,
                                          const char *line,
                                          int line_number)
{
    char buffer[APP_PROFILE_MAX_LINE_LEN];
    char *saveptr = NULL;
    char *token = NULL;
    char *module_name = NULL;
    char *key = NULL;
    char *value_text = NULL;
    int parsed_count = 0;
    int max_count = 0;
    int *target = NULL;

    if (profile == NULL || line == NULL) {
        return false;
    }

    strlcpy(buffer, line, sizeof(buffer));
    token = strtok_r(buffer, "|", &saveptr);
    module_name = strtok_r(NULL, "|", &saveptr);
    key = strtok_r(NULL, "|", &saveptr);
    value_text = strtok_r(NULL, "|", &saveptr);

    if (token == NULL || strcasecmp(token, "MODULE") != 0
        || module_name == NULL || key == NULL || value_text == NULL) {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=MODULE_FORMAT", line_number);
        return false;
    }

    app_profile_trim(module_name);
    app_profile_trim(key);
    app_profile_trim(value_text);

    if (strcasecmp(key, "COUNT") != 0 || !app_profile_parse_non_negative_int(value_text, &parsed_count)) {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=MODULE_COUNT", line_number);
        return false;
    }

    if (strcasecmp(module_name, "DEN") == 0) {
        target = &profile->modules.den_count;
        max_count = 8;
    } else if (strcasecmp(module_name, "SIC") == 0) {
        target = &profile->modules.sic_count;
        max_count = 2;
    } else if (strcasecmp(module_name, "J") == 0) {
        target = &profile->modules.j_count;
        max_count = 8;
    } else if (strcasecmp(module_name, "YARN") == 0) {
        target = &profile->modules.yarn_count;
        max_count = 2;
    } else if (strcasecmp(module_name, "STITCH") == 0) {
        target = &profile->modules.stitch_count;
        max_count = 4;
    } else if (strcasecmp(module_name, "FEET") == 0 || strcasecmp(module_name, "FOOT") == 0) {
        target = &profile->modules.feet_count;
        max_count = 2;
    } else {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=MODULE_UNSUPPORTED|MODULE=%s", line_number, module_name);
        return false;
    }

    if (parsed_count > max_count) {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=MODULE_RANGE|MODULE=%s|COUNT=%d|MAX=%d",
                 line_number,
                 module_name,
                 parsed_count,
                 max_count);
        return false;
    }

    *target = parsed_count;
    profile->modules.has_explicit_configuration = true;
    return true;
}

static bool app_profile_parse_button_line(const char *line, int line_number)
{
    char buffer[APP_PROFILE_MAX_LINE_LEN];
    char *saveptr = NULL;
    char *token = NULL;
    char *instance = NULL;
    char *action = NULL;
    char *script = NULL;

    strlcpy(buffer, line, sizeof(buffer));
    token = strtok_r(buffer, "|", &saveptr);
    instance = strtok_r(NULL, "|", &saveptr);
    action = strtok_r(NULL, "|", &saveptr);
    script = strtok_r(NULL, "|", &saveptr);

    if (token == NULL || strcasecmp(token, "BUTTON") != 0
        || instance == NULL || action == NULL || script == NULL) {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=BUTTON_FORMAT", line_number);
        return false;
    }

    app_profile_trim(instance);
    app_profile_trim(action);
    app_profile_trim(script);

    if (instance[0] == '\0' || action[0] == '\0' || !app_profile_is_valid_script_name(script)) {
        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=BUTTON_VALUE", line_number);
        return false;
    }

    return true;
}

static bool app_profile_line_is_crc_declaration(const char *line)
{
    return line != NULL
        && (strncasecmp(line, "CRC32=", 6) == 0
            || strncasecmp(line, "CRC=", 4) == 0
            || strncasecmp(line, "SIGNATURE=", 10) == 0);
}

bool app_profile_load_from_file(const char *name, HeadRuntimeProfile *out)
{
    app_profile_filename_info_t filename_info = {};
    char path[96];
    FILE *file = NULL;
    char raw_line[APP_PROFILE_MAX_LINE_LEN + 4];
    int line_number = 0;
    bool ok = true;
    bool in_action = false;
    HeadRuntimeAction *current_action = NULL;
    uint32_t body_crc = 0;
    int64_t start_us = esp_timer_get_time();
    int64_t open_done_us = 0;
    int64_t parse_done_us = 0;

    if (out == NULL || !app_profile_parse_filename(name, &filename_info)) {
        return false;
    }

    if (!app_profile_build_path(name, path, sizeof(path))) {
        return false;
    }

    memset(out, 0, sizeof(*out));
    out->origin = APP_PROFILE_ORIGIN_FILE;
    strlcpy(out->filename, name, sizeof(out->filename));
    strlcpy(out->system, filename_info.is_unified ? "UNI" : "MOD", sizeof(out->system));
    out->program_number = filename_info.program_number;
    snprintf(out->profile_name,
             sizeof(out->profile_name),
             "Programa %u - %s",
             (unsigned)out->program_number,
             filename_info.is_unified ? "Sistema Unificado" : "Sistema Modular");

    if (!app_profile_copy_compiled_defaults(out, app_profile_program_id_from_number(out->program_number))) {
        return false;
    }

    file = fopen(path, "rb");
    if (file == NULL) {
        ESP_LOGW(TAG, "PROFILE_LOAD|FILE=%s|ERR=OPEN", path);
        return false;
    }

    open_done_us = esp_timer_get_time();

    while (fgets(raw_line, sizeof(raw_line), file) != NULL) {
        char line[APP_PROFILE_MAX_LINE_LEN + 4];
        size_t raw_len = strlen(raw_line);

        line_number++;
        out->source_size += raw_len;

        if (raw_len > 0
            && raw_line[raw_len - 1] != '\n'
            && !feof(file)
            && raw_len >= APP_PROFILE_MAX_LINE_LEN) {
            ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=LINE_TOO_LONG", line_number);
            ok = false;
            break;
        }

        strlcpy(line, raw_line, sizeof(line));
        app_profile_trim(line);

        if (!app_profile_line_is_crc_declaration(line)) {
            body_crc = app_profile_crc32_update(body_crc, raw_line, raw_len);
        }

        if (line[0] == '\0' || line[0] == '#' || (line[0] == '/' && line[1] == '/')) {
            continue;
        }

        if (!in_action && strncasecmp(line, "PROFILE_NAME=", 13) == 0) {
            const char *value = line + 13;
            if (value[0] == '\0' || strlen(value) > APP_PROFILE_MAX_PROFILE_NAME_LEN) {
                ok = false;
                break;
            }
            strlcpy(out->profile_name, value, sizeof(out->profile_name));
            out->has_profile_name = true;
            continue;
        }

        if (!in_action && strncasecmp(line, "SYSTEM=", 7) == 0) {
            char value[APP_PROFILE_MAX_SYSTEM_LEN + 1];
            strlcpy(value, line + 7, sizeof(value));
            app_profile_trim(value);
            if (!app_profile_system_token_matches(filename_info.is_unified, value)) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=SYSTEM_MISMATCH|VALUE=%s", line_number, value);
                ok = false;
                break;
            }
            strlcpy(out->system, filename_info.is_unified ? "UNI" : "MOD", sizeof(out->system));
            out->has_declared_system = true;
            continue;
        }

        if (!in_action && strncasecmp(line, "PROGRAM=", 8) == 0) {
            uint32_t value = 0;
            if (!app_profile_parse_u32(line + 8, &value) || value != out->program_number) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=PROGRAM_MISMATCH|VALUE=%s", line_number, line + 8);
                ok = false;
                break;
            }
            out->has_declared_program = true;
            continue;
        }

        if (!in_action
            && (strncasecmp(line, "VERSION=", 8) == 0
                || strncasecmp(line, "PROFILE_VERSION=", 16) == 0)) {
            const char *value_text = strchr(line, '=');
            uint32_t value = 0;
            if (value_text == NULL || !app_profile_parse_u32(value_text + 1, &value)) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=VERSION", line_number);
                ok = false;
                break;
            }
            out->has_version = true;
            out->version = value;
            continue;
        }

        if (!in_action && app_profile_line_is_crc_declaration(line)) {
            const char *value_text = strchr(line, '=');
            uint32_t value = 0;
            if (value_text == NULL || !app_profile_parse_u32(value_text + 1, &value)) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=CRC", line_number);
                ok = false;
                break;
            }
            out->has_declared_crc = true;
            out->declared_crc32 = value;
            continue;
        }

        if (!in_action && strncasecmp(line, "INIT_SCRIPT=", 12) == 0) {
            const char *value = line + 12;
            if (!app_profile_is_valid_script_name(value)) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=INIT_SCRIPT", line_number);
                ok = false;
                break;
            }
            strlcpy(out->init_script, value, sizeof(out->init_script));
            continue;
        }

        if (!in_action && strncasecmp(line, "MODULE|", 7) == 0) {
            if (!app_profile_parse_module_line(out, line, line_number)) {
                ok = false;
                break;
            }
            continue;
        }

        if (!in_action && strncasecmp(line, "BUTTON|", 7) == 0) {
            if (!app_profile_parse_button_line(line, line_number)) {
                ok = false;
                break;
            }
            continue;
        }

        if (strncasecmp(line, "BEGIN|", 6) == 0) {
            char action_name[APP_PROFILE_MAX_ACTION_NAME_LEN + 1];
            bool has_value = false;
            int value = 0;

            if (in_action || !app_profile_parse_begin_header(line,
                                                             action_name,
                                                             sizeof(action_name),
                                                             &has_value,
                                                             &value)) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=BEGIN", line_number);
                ok = false;
                break;
            }

            if (app_profile_find_action_mutable(out, action_name) != NULL
                || out->action_count >= APP_PROFILE_MAX_ACTIONS) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=ACTION_DUP_OR_LIMIT|ACTION=%s",
                         line_number,
                         action_name);
                ok = false;
                break;
            }

            current_action = &out->actions[out->action_count++];
            memset(current_action, 0, sizeof(*current_action));
            strlcpy(current_action->name, action_name, sizeof(current_action->name));
            current_action->begin_line = line_number;
            current_action->has_value = has_value;
            current_action->value = value;
            current_action->first_command = (uint16_t)out->command_count;
            in_action = true;
            continue;
        }

        if (strcasecmp(line, "END") == 0) {
            if (!in_action || current_action == NULL) {
                ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=END_WITHOUT_BEGIN", line_number);
                ok = false;
                break;
            }
            in_action = false;
            current_action = NULL;
            continue;
        }

        if (in_action) {
            if (!app_profile_add_action_command(out, current_action, line, line_number)) {
                ok = false;
                break;
            }
            continue;
        }

        ESP_LOGW(TAG, "PROFILE_PARSE|LINE=%d|ERR=UNKNOWN_TOP_LEVEL|TEXT=%s", line_number, line);
        ok = false;
        break;
    }

    fclose(file);
    parse_done_us = esp_timer_get_time();
    out->computed_crc32 = body_crc;
    out->crc_ok = !out->has_declared_crc || out->declared_crc32 == out->computed_crc32;

    if (in_action) {
        ESP_LOGW(TAG, "PROFILE_PARSE|ERR=MISSING_END|ACTION=%s",
                 current_action != NULL ? current_action->name : "UNKNOWN");
        ok = false;
    }

    app_profile_apply_module_counts_to_view(out);
    app_profile_rebuild_view_pointers(out, out);

    ESP_LOGI(TAG,
             "PROFILE_TIMING|LOAD_MS=%lld|PARSE_MS=%lld|FILE=%s",
             (long long)((open_done_us - start_us) / 1000),
             (long long)((parse_done_us - open_done_us) / 1000),
             name);

    return ok && app_profile_validate(out);
}

bool app_profile_validate(const HeadRuntimeProfile *profile)
{
    if (profile == NULL) {
        return false;
    }

    if (profile->origin == APP_PROFILE_ORIGIN_FILE && !app_profile_is_valid_filename(profile->filename)) {
        return false;
    }

    if (profile->profile_name[0] == '\0'
        || strlen(profile->profile_name) > APP_PROFILE_MAX_PROFILE_NAME_LEN
        || profile->program_number < 1
        || profile->program_number > 3) {
        return false;
    }

    if (profile->has_declared_crc && !profile->crc_ok) {
        ESP_LOGW(TAG,
                 "PROFILE_VALIDATE|FILE=%s|ERR=CRC|DECLARED=0x%08" PRIX32 "|COMPUTED=0x%08" PRIX32,
                 profile->filename,
                 profile->declared_crc32,
                 profile->computed_crc32);
        return false;
    }

    if (profile->action_count > APP_PROFILE_MAX_ACTIONS
        || profile->command_count > APP_PROFILE_MAX_COMMANDS
        || profile->command_text_used > sizeof(profile->command_text)) {
        return false;
    }

    if (profile->modules.den_count < 0 || profile->modules.den_count > 8
        || profile->modules.sic_count < 0 || profile->modules.sic_count > 2
        || profile->modules.j_count < 0 || profile->modules.j_count > 8
        || profile->modules.yarn_count < 0 || profile->modules.yarn_count > 2
        || profile->modules.stitch_count < 0 || profile->modules.stitch_count > 4
        || profile->modules.feet_count < 0 || profile->modules.feet_count > 2) {
        return false;
    }

    for (size_t i = 0; i < profile->action_count; ++i) {
        const HeadRuntimeAction *action = &profile->actions[i];
        size_t end = (size_t)action->first_command + action->command_count;
        if (action->name[0] == '\0'
            || action->first_command > profile->command_count
            || end > profile->command_count) {
            return false;
        }
    }

    for (size_t i = 0; i < profile->command_count; ++i) {
        const HeadRuntimeCommand *command = &profile->commands[i];
        if ((size_t)command->text_offset + command->text_len >= sizeof(profile->command_text)) {
            return false;
        }
        if (command->type == APP_PROFILE_RUNTIME_COMMAND_WAIT
            && (command->wait_ms < 0 || command->wait_ms > APP_PROFILE_MAX_WAIT_MS)) {
            return false;
        }
        if (command->type == APP_PROFILE_RUNTIME_COMMAND_SEND
            && command->dlc > APP_HEAD_PROFILE_MAX_DLC) {
            return false;
        }
    }

    return true;
}

static void app_profile_set_loading(const char *name, bool loading)
{
    if (s_profile_mutex != NULL) {
        xSemaphoreTake(s_profile_mutex, portMAX_DELAY);
    }

    s_loading = loading;
    if (loading && name != NULL) {
        strlcpy(s_loading_name, name, sizeof(s_loading_name));
    } else {
        s_loading_name[0] = '\0';
    }

    if (s_profile_mutex != NULL) {
        xSemaphoreGive(s_profile_mutex);
    }
}

esp_err_t app_profile_apply(const HeadRuntimeProfile *profile)
{
    size_t inactive_index;
    HeadRuntimeProfile *target;

    if (!app_profile_validate(profile)) {
        return ESP_ERR_INVALID_ARG;
    }

    if (s_profile_mutex == NULL) {
        return ESP_ERR_INVALID_STATE;
    }

    inactive_index = s_active_index == 0 ? 1 : 0;
    target = &s_profiles[inactive_index];

    if (target != profile) {
        *target = *profile;
        app_profile_rebuild_view_pointers(target, profile);
    } else {
        app_profile_rebuild_view_pointers(target, target);
    }

    if (xSemaphoreTake(s_profile_mutex, portMAX_DELAY) != pdTRUE) {
        return ESP_ERR_TIMEOUT;
    }

    s_generation++;
    target->generation = s_generation;
    s_active_index = inactive_index;
    xSemaphoreGive(s_profile_mutex);

    ESP_LOGI(TAG,
             "PROFILE_ACTIVE|FILE=%s|PROFILE=%s|GEN=%" PRIu32 "|ORIGIN=%s",
             target->filename[0] != '\0' ? target->filename : "COMPILED",
             target->profile_name,
             target->generation,
             target->origin == APP_PROFILE_ORIGIN_FILE ? "FILE" : "COMPILED_FALLBACK");
    return ESP_OK;
}

static esp_err_t app_profile_save_selected(const char *name)
{
    FILE *file = NULL;

    if (!app_profile_is_valid_filename(name)) {
        return ESP_ERR_INVALID_ARG;
    }

    file = fopen(APP_PROFILE_SELECTED_PATH, "wb");
    if (file == NULL) {
        return ESP_FAIL;
    }

    if (fprintf(file, "%s\n", name) < 0) {
        fclose(file);
        return ESP_FAIL;
    }

    if (fclose(file) != 0) {
        return ESP_FAIL;
    }

    return ESP_OK;
}

static esp_err_t app_profile_select_internal(const char *name, bool persist_selection)
{
    size_t candidate_index = s_active_index == 0 ? 1 : 0;
    HeadRuntimeProfile *candidate = &s_profiles[candidate_index];
    int64_t start_us = esp_timer_get_time();
    int64_t loaded_us;
    int64_t applied_us;
    esp_err_t err;

    if (!app_profile_is_valid_filename(name)) {
        return ESP_ERR_INVALID_ARG;
    }

    app_profile_set_loading(name, true);
    bool loaded = app_profile_load_from_file(name, candidate);
    loaded_us = esp_timer_get_time();
    app_profile_set_loading(NULL, false);
    if (!loaded) {
        return ESP_ERR_INVALID_RESPONSE;
    }

    err = app_profile_apply(candidate);
    applied_us = esp_timer_get_time();
    if (err != ESP_OK) {
        return err;
    }

    if (persist_selection) {
        esp_err_t save_err = app_profile_save_selected(name);
        if (save_err != ESP_OK) {
            ESP_LOGE(TAG, "PROFILE_SELECT|FILE=%s|ERR=SAVE_SELECTED|RAM_ACTIVE=1", name);
            return save_err;
        }
    }

    ESP_LOGI(TAG,
             "PROFILE_TIMING|VALIDATE_MS=%lld|APPLY_MS=%lld|TOTAL_MS=%lld|FILE=%s",
             (long long)((loaded_us - start_us) / 1000),
             (long long)((applied_us - loaded_us) / 1000),
             (long long)((esp_timer_get_time() - start_us) / 1000),
             name);
    return ESP_OK;
}

esp_err_t app_profile_select(const char *name)
{
    return app_profile_select_internal(name, true);
}

const HeadRuntimeProfile *app_profile_get_active(void)
{
    const HeadRuntimeProfile *profile = NULL;

    if (s_profile_mutex != NULL && xSemaphoreTake(s_profile_mutex, portMAX_DELAY) == pdTRUE) {
        profile = &s_profiles[s_active_index];
        xSemaphoreGive(s_profile_mutex);
        return profile;
    }

    return &s_profiles[s_active_index];
}

const char *app_profile_get_active_filename(void)
{
    const HeadRuntimeProfile *profile = app_profile_get_active();
    return profile != NULL ? profile->filename : "";
}

const HeadCommandProfile *app_profile_get_active_command_profile(void)
{
    const HeadRuntimeProfile *profile = app_profile_get_active();
    if (profile == NULL) {
        return app_head_program_get_profile(APP_HEAD_PROGRAM_1);
    }

    return &profile->command_profile;
}

app_profile_origin_t app_profile_get_active_origin(void)
{
    const HeadRuntimeProfile *profile = app_profile_get_active();
    return profile != NULL ? profile->origin : APP_PROFILE_ORIGIN_COMPILED_FALLBACK;
}

uint32_t app_profile_get_active_generation(void)
{
    const HeadRuntimeProfile *profile = app_profile_get_active();
    return profile != NULL ? profile->generation : 0;
}

bool app_profile_is_active_filename(const char *name)
{
    const HeadRuntimeProfile *profile;

    if (name == NULL || name[0] == '\0') {
        return false;
    }

    profile = app_profile_get_active();
    return profile != NULL
        && profile->origin == APP_PROFILE_ORIGIN_FILE
        && strcasecmp(profile->filename, name) == 0;
}

bool app_profile_is_loading_filename(const char *name)
{
    bool result = false;

    if (name == NULL || name[0] == '\0') {
        return false;
    }

    if (s_profile_mutex != NULL && xSemaphoreTake(s_profile_mutex, portMAX_DELAY) == pdTRUE) {
        result = s_loading && strcasecmp(s_loading_name, name) == 0;
        xSemaphoreGive(s_profile_mutex);
        return result;
    }

    return s_loading && strcasecmp(s_loading_name, name) == 0;
}

bool app_profile_is_loading(void)
{
    bool result = false;

    if (s_profile_mutex != NULL && xSemaphoreTake(s_profile_mutex, portMAX_DELAY) == pdTRUE) {
        result = s_loading;
        xSemaphoreGive(s_profile_mutex);
        return result;
    }

    return s_loading;
}

esp_err_t app_profile_apply_compiled(app_head_program_id_t program_id)
{
    size_t inactive_index;
    HeadRuntimeProfile *candidate;
    const HeadCommandProfile *base = app_head_program_get_profile(program_id);

    if (base == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    inactive_index = s_active_index == 0 ? 1 : 0;
    candidate = &s_profiles[inactive_index];
    memset(candidate, 0, sizeof(*candidate));
    candidate->origin = APP_PROFILE_ORIGIN_COMPILED_FALLBACK;
    candidate->program_number = (uint16_t)program_id;
    candidate->has_profile_name = true;
    strlcpy(candidate->profile_name, base->program_name, sizeof(candidate->profile_name));
    snprintf(candidate->filename, sizeof(candidate->filename), "COMPILED_P%u", (unsigned)program_id);
    strlcpy(candidate->system, "COMPILED", sizeof(candidate->system));

    if (!app_profile_copy_compiled_defaults(candidate, program_id)) {
        return ESP_ERR_INVALID_SIZE;
    }

    return app_profile_apply(candidate);
}

esp_err_t app_profile_load_selected(void)
{
    FILE *file = fopen(APP_PROFILE_SELECTED_PATH, "rb");
    char name[APP_PROFILE_MAX_FILENAME_LEN + 4];

    if (file == NULL) {
        ESP_LOGW(TAG, "PROFILE_BOOT|SELECTED=NONE|ORIGIN=COMPILED_FALLBACK");
        return app_profile_apply_compiled(APP_HEAD_PROGRAM_1);
    }

    if (fgets(name, sizeof(name), file) == NULL) {
        fclose(file);
        ESP_LOGW(TAG, "PROFILE_BOOT|SELECTED=EMPTY|ORIGIN=COMPILED_FALLBACK");
        return app_profile_apply_compiled(APP_HEAD_PROGRAM_1);
    }

    fclose(file);
    app_profile_trim(name);

    if (!app_profile_is_valid_filename(name)) {
        ESP_LOGW(TAG, "PROFILE_BOOT|SELECTED=%s|ERR=INVALID_NAME|ORIGIN=COMPILED_FALLBACK", name);
        return app_profile_apply_compiled(APP_HEAD_PROGRAM_1);
    }

    esp_err_t err = app_profile_select_internal(name, false);
    if (err != ESP_OK) {
        ESP_LOGW(TAG,
                 "PROFILE_BOOT|SELECTED=%s|ERR=%s|ORIGIN=COMPILED_FALLBACK",
                 name,
                 esp_err_to_name(err));
        return app_profile_apply_compiled(APP_HEAD_PROGRAM_1);
    }

    ESP_LOGI(TAG, "PROFILE_BOOT|SELECTED=%s|ORIGIN=FILE", name);
    return ESP_OK;
}

esp_err_t app_profile_store_init(void)
{
    if (s_profile_mutex == NULL) {
        s_profile_mutex = xSemaphoreCreateMutex();
        if (s_profile_mutex == NULL) {
            return ESP_ERR_NO_MEM;
        }
    }

    if (s_generation == 0) {
        esp_err_t err = app_profile_apply_compiled(APP_HEAD_PROGRAM_1);
        if (err != ESP_OK) {
            return err;
        }
    }

    return ESP_OK;
}


HeadRuntimeProfile *app_profile_get_inactive_scratch(void)
{
    size_t inactive_index = s_active_index == 0 ? 1 : 0;
    return &s_profiles[inactive_index];
}