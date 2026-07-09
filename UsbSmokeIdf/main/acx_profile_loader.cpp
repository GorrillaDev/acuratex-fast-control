#include "acx_profile_loader.h"

#include <cstdarg>
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "command_processor.h"
#include "head_state_manager.h"

namespace {

typedef enum {
    ACX_INPUT_FILE = 0,
    ACX_INPUT_MEMORY = 1,
} acx_input_kind_t;

typedef struct {
    acx_input_kind_t kind;
    FILE *file;
    const uint8_t *bytes;
    size_t size;
    size_t position;
} acx_input_t;

typedef struct {
    uint16_t section_id;
    uint16_t section_version;
    uint32_t offset;
    uint32_t size;
    uint32_t record_count;
    uint32_t crc32;
} acx_directory_entry_t;

typedef struct {
    uint8_t remaining;
    uint8_t lead;
} acx_utf8_validator_t;

typedef struct {
    uint32_t total_actions;
    uint32_t enabled_actions;
    uint32_t materialized_actions;
    uint32_t materialized_commands;
    uint32_t command_text_bytes;
    uint32_t init_text_bytes;
    uint32_t temp_buffer_max;
    bool profile_enabled;
    bool profile_published;
    bool has_source_crc32;
    uint8_t source_kind;
    uint32_t source_crc32;
    uint32_t profile_id;
    uint32_t profile_version_id;
    int32_t program_number;
    int32_t version_number;
    uint32_t schema_version;
    uint32_t section_directory_size;
    uint16_t section_count;
    uint32_t file_size;
} acx_plan_t;

typedef struct {
    acx_input_t *input;
    uint32_t remaining;
    uint32_t section_crc32;
    uint32_t *payload_crc32;
} acx_section_reader_t;

static uint32_t acx_crc32_update(uint32_t crc, const void *data, size_t len)
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

static void acx_result_reset(AcxProfileLoadResult *result)
{
    if (result == NULL) {
        return;
    }

    memset(result, 0, sizeof(*result));
    result->error = ACX_PROFILE_LOAD_OK;
    result->temp_buffer_max = ACX_PROFILE_TEMP_BUFFER_MAX_BYTES;
}

static void acx_result_set_error(AcxProfileLoadResult *result,
                                 AcxProfileLoadError error,
                                 const char *fmt,
                                 ...)
{
    va_list args;

    if (result == NULL) {
        return;
    }

    result->error = error;
    if (fmt != NULL) {
        va_start(args, fmt);
        vsnprintf(result->message, sizeof(result->message), fmt, args);
        va_end(args);
    }
}

static const char *acx_section_name(uint16_t section_id)
{
    switch (section_id) {
    case ACX_PROFILE_SECTION_METADATA:
        return "Metadata";
    case ACX_PROFILE_SECTION_INIT:
        return "Init";
    case ACX_PROFILE_SECTION_TESTEO:
        return "Testeo";
    case ACX_PROFILE_SECTION_MOTION:
        return "Motion";
    case ACX_PROFILE_SECTION_J:
        return "J";
    case ACX_PROFILE_SECTION_CASCADE:
        return "Cascade";
    case ACX_PROFILE_SECTION_STOP:
        return "Stop";
    case ACX_PROFILE_SECTION_ACTIONS:
        return "Actions";
    default:
        return "Unknown";
    }
}

static bool acx_is_reserved_name(const char *name)
{
    return name != NULL
        && (strcmp(name, ".selected") == 0
            || strcmp(name, ".upload.tmp") == 0);
}

static bool acx_path_matches_root(const char *file_path)
{
    return file_path != NULL
        && strncmp(file_path, APP_PROFILE_FS_BASE "/", 4) == 0
        && file_path[4] != '\0';
}

static bool acx_is_valid_acx_basename(const char *name)
{
    size_t len;

    if (name == NULL || name[0] == '\0') {
        return false;
    }

    len = strlen(name);
    if (len == 0 || len > ACX_PROFILE_MAX_FILENAME_LEN) {
        return false;
    }

    if (acx_is_reserved_name(name) || strstr(name, "..") != NULL) {
        return false;
    }

    if (len < 4 || strcasecmp(name + len - 4, ACX_PROFILE_FILE_EXTENSION) != 0) {
        return false;
    }

    for (size_t i = 0; i < len; ++i) {
        char c = name[i];
        if (c == '/' || c == '\\' || c == '|' || c == '\r' || c == '\n') {
            return false;
        }
    }

    return true;
}

static bool acx_extract_basename(const char *file_path, char *out, size_t out_size)
{
    const char *basename = NULL;

    if (file_path == NULL || out == NULL || out_size == 0) {
        return false;
    }

    if (!acx_path_matches_root(file_path)) {
        return false;
    }

    basename = file_path + strlen(APP_PROFILE_FS_BASE) + 1;
    if (!acx_is_valid_acx_basename(basename)) {
        return false;
    }

    strlcpy(out, basename, out_size);
    return true;
}

static bool acx_validate_utf8_byte(acx_utf8_validator_t *state, uint8_t byte)
{
    if (state == NULL) {
        return false;
    }

    if (state->remaining == 0) {
        if (byte == 0x00) {
            return false;
        }

        if (byte < 0x80) {
            return true;
        }

        if (byte >= 0xC2 && byte <= 0xDF) {
            state->remaining = 1;
            state->lead = byte;
            return true;
        }

        if (byte >= 0xE0 && byte <= 0xEF) {
            state->remaining = 2;
            state->lead = byte;
            return true;
        }

        if (byte >= 0xF0 && byte <= 0xF4) {
            state->remaining = 3;
            state->lead = byte;
            return true;
        }

        return false;
    }

    if ((byte & 0xC0) != 0x80) {
        return false;
    }

    if (state->remaining == 2) {
        if (state->lead == 0xE0 && byte < 0xA0) {
            return false;
        }
        if (state->lead == 0xED && byte > 0x9F) {
            return false;
        }
    } else if (state->remaining == 3) {
        if (state->lead == 0xF0 && byte < 0x90) {
            return false;
        }
        if (state->lead == 0xF4 && byte > 0x8F) {
            return false;
        }
    }

    state->remaining--;
    return true;
}

static bool acx_validate_utf8_bytes(acx_utf8_validator_t *state, const uint8_t *bytes, size_t len)
{
    if (state == NULL || (bytes == NULL && len > 0)) {
        return false;
    }

    for (size_t i = 0; i < len; ++i) {
        if (!acx_validate_utf8_byte(state, bytes[i])) {
            return false;
        }
    }

    return true;
}

static bool acx_validate_utf8_finish(const acx_utf8_validator_t *state)
{
    return state != NULL && state->remaining == 0;
}

static bool acx_input_seek(acx_input_t *input, size_t offset)
{
    if (input == NULL) {
        return false;
    }

    if (input->kind == ACX_INPUT_MEMORY) {
        if (offset > input->size) {
            return false;
        }
        input->position = offset;
        return true;
    }

    if (input->file == NULL) {
        return false;
    }

    return fseek(input->file, (long)offset, SEEK_SET) == 0;
}

static bool acx_input_read(acx_input_t *input, void *dst, size_t len)
{
    if (input == NULL || dst == NULL || len == 0) {
        return len == 0;
    }

    if (input->kind == ACX_INPUT_MEMORY) {
        if (input->position + len > input->size) {
            return false;
        }
        memcpy(dst, input->bytes + input->position, len);
        input->position += len;
        return true;
    }

    if (input->file == NULL) {
        return false;
    }

    return fread(dst, 1, len, input->file) == len;
}


static bool acx_input_read_exact(acx_input_t *input, void *dst, size_t len)
{
    return acx_input_read(input, dst, len);
}

static bool acx_section_reader_begin(acx_section_reader_t *reader,
                                     acx_input_t *input,
                                     uint32_t offset,
                                     uint32_t size,
                                     uint32_t *payload_crc32)
{
    if (reader == NULL || input == NULL) {
        return false;
    }

    if (!acx_input_seek(input, offset)) {
        return false;
    }

    reader->input = input;
    reader->remaining = size;
    reader->section_crc32 = 0;
    reader->payload_crc32 = payload_crc32;
    return true;
}

static bool acx_section_reader_read(acx_section_reader_t *reader, void *dst, size_t len)
{
    if (reader == NULL || dst == NULL || len == 0) {
        return reader != NULL && len == 0;
    }

    if (len > reader->remaining) {
        return false;
    }

    if (!acx_input_read_exact(reader->input, dst, len)) {
        return false;
    }

    reader->remaining -= (uint32_t)len;
    reader->section_crc32 = acx_crc32_update(reader->section_crc32, dst, len);
    if (reader->payload_crc32 != NULL) {
        *reader->payload_crc32 = acx_crc32_update(*reader->payload_crc32, dst, len);
    }
    return true;
}

static bool acx_section_reader_read_u8(acx_section_reader_t *reader, uint8_t *value)
{
    uint8_t byte = 0;

    if (value == NULL) {
        return false;
    }

    if (!acx_section_reader_read(reader, &byte, sizeof(byte))) {
        return false;
    }

    *value = byte;
    return true;
}

static bool acx_section_reader_read_u16(acx_section_reader_t *reader, uint16_t *value)
{
    uint8_t bytes[2];

    if (value == NULL) {
        return false;
    }

    if (!acx_section_reader_read(reader, bytes, sizeof(bytes))) {
        return false;
    }

    *value = (uint16_t)((uint16_t)bytes[0] | ((uint16_t)bytes[1] << 8));
    return true;
}

static bool acx_section_reader_read_u32(acx_section_reader_t *reader, uint32_t *value)
{
    uint8_t bytes[4];

    if (value == NULL) {
        return false;
    }

    if (!acx_section_reader_read(reader, bytes, sizeof(bytes))) {
        return false;
    }

    *value = (uint32_t)bytes[0]
        | ((uint32_t)bytes[1] << 8)
        | ((uint32_t)bytes[2] << 16)
        | ((uint32_t)bytes[3] << 24);
    return true;
}

static bool acx_section_reader_read_i32(acx_section_reader_t *reader, int32_t *value)
{
    uint32_t raw = 0;

    if (value == NULL) {
        return false;
    }

    if (!acx_section_reader_read_u32(reader, &raw)) {
        return false;
    }

    *value = (int32_t)raw;
    return true;
}


static bool acx_section_reader_read_validated_utf8(acx_section_reader_t *reader,
                                                   uint32_t len,
                                                   char *out,
                                                   size_t out_size,
                                                   bool require_non_empty,
                                                   AcxProfileLoadResult *result)
{
    acx_utf8_validator_t utf8 = {};
    uint8_t buffer[ACX_PROFILE_WORK_BUFFER_BYTES];
    size_t used = 0;
    bool store = out != NULL && out_size > 0;

    if (require_non_empty && len == 0) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_STRING,
                             "empty UTF-8 string is not allowed");
        return false;
    }

    if (store && len + 1 > out_size) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "string does not fit into destination buffer");
        return false;
    }

    while (len > 0) {
        size_t chunk = len > sizeof(buffer) ? sizeof(buffer) : (size_t)len;
        if (!acx_section_reader_read(reader, buffer, chunk)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                 "truncated while reading UTF-8 string");
            return false;
        }

        if (!acx_validate_utf8_bytes(&utf8, buffer, chunk)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_STRING,
                                 "invalid UTF-8 string");
            return false;
        }

        if (store) {
            memcpy(out + used, buffer, chunk);
            used += chunk;
        }

        len -= (uint32_t)chunk;
    }

    if (!acx_validate_utf8_finish(&utf8)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_STRING,
                             "truncated UTF-8 sequence");
        return false;
    }

    if (store) {
        out[used] = '\0';
    }

    return true;
}

static bool acx_section_reader_read_string_small(acx_section_reader_t *reader,
                                                 uint32_t len,
                                                 char *out,
                                                 size_t out_size,
                                                 bool require_non_empty,
                                                 AcxProfileLoadResult *result)
{
    if (out == NULL || out_size == 0) {
        return false;
    }

    if (len + 1 > out_size) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "string does not fit into destination buffer");
        return false;
    }

    return acx_section_reader_read_validated_utf8(reader, len, out, out_size, require_non_empty, result);
}

static bool acx_store_profile_text(HeadRuntimeProfile *profile,
                                   const char *text,
                                   uint16_t *offset,
                                   uint16_t *length,
                                   AcxProfileLoadResult *result)
{
    size_t len;

    if (profile == NULL || text == NULL || offset == NULL || length == NULL) {
        return false;
    }

    len = strlen(text);
    if (len == 0 || len > UINT16_MAX) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_STRING,
                             "runtime text cannot be empty or exceed uint16");
        return false;
    }

    if (profile->command_text_used + len + 1U > sizeof(profile->command_text)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "command text buffer is full");
        return false;
    }

    *offset = (uint16_t)profile->command_text_used;
    *length = (uint16_t)len;
    memcpy(profile->command_text + profile->command_text_used, text, len + 1U);
    profile->command_text_used += len + 1U;
    return true;
}

static bool acx_store_init_line(HeadRuntimeProfile *profile,
                                const char *line,
                                const char **out,
                                AcxProfileLoadResult *result)
{
    size_t len;
    char *dst;

    if (profile == NULL || line == NULL || out == NULL) {
        return false;
    }

    len = strlen(line);
    if (len == 0 || len + 1U > sizeof(profile->init_text)
        || profile->init_text_used + len + 1U > sizeof(profile->init_text)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "init text buffer is full");
        return false;
    }

    dst = profile->init_text + profile->init_text_used;
    memcpy(dst, line, len + 1U);
    *out = dst;
    profile->init_text_used += len + 1U;
    return true;
}

static bool acx_format_hex_bytes(const uint8_t *data,
                                 uint8_t dlc,
                                 char *out,
                                 size_t out_size,
                                 uint32_t can_id)
{
    size_t used = 0;
    int written;

    if (out == NULL || out_size == 0) {
        return false;
    }

    written = snprintf(out, out_size, "send 0x%03" PRIX32, can_id);
    if (written < 0 || (size_t)written >= out_size) {
        return false;
    }

    used = (size_t)written;
    for (uint8_t i = 0; i < dlc; ++i) {
        written = snprintf(out + used, out_size - used, " %02X", data[i]);
        if (written < 0 || (size_t)written >= out_size - used) {
            return false;
        }
        used += (size_t)written;
    }

    return true;
}

static bool acx_format_can_select_text(int bus, char *out, size_t out_size)
{
    return out != NULL && out_size > 0
        && snprintf(out, out_size, "%s", bus == APP_CMD_CAN_BUS_2 ? "can2" : "can1") >= 0;
}

static bool acx_format_wait_text(uint32_t wait_ms, char *out, size_t out_size)
{
    return out != NULL && out_size > 0
        && snprintf(out, out_size, "WAIT %" PRIu32, wait_ms) >= 0;
}

static bool acx_format_status_text(char *out, size_t out_size)
{
    return out != NULL && out_size > 0
        && snprintf(out, out_size, "status") >= 0;
}

static bool acx_append_runtime_command(HeadRuntimeProfile *profile,
                                       app_profile_runtime_command_type_t type,
                                       const char *text,
                                       int line_number,
                                       int can_bus,
                                       uint32_t can_id,
                                       const uint8_t *data,
                                       uint8_t dlc,
                                       int wait_ms,
                                       bool uses_dynamic_j_placeholder,
                                       AcxProfileLoadResult *result)
{
    HeadRuntimeCommand *command = NULL;

    if (profile == NULL || text == NULL) {
        return false;
    }

    if (profile->command_count >= APP_PROFILE_MAX_COMMANDS) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "command capacity exceeded");
        return false;
    }

    command = &profile->commands[profile->command_count];
    memset(command, 0, sizeof(*command));
    command->type = type;
    command->line_number = line_number;
    command->can_bus = can_bus;
    command->can_id = can_id;
    command->dlc = dlc;
    command->wait_ms = wait_ms;
    command->uses_dynamic_j_placeholder = uses_dynamic_j_placeholder;
    if (dlc > 0 && data != NULL) {
        memcpy(command->data, data, dlc);
    }

    if (!acx_store_profile_text(profile, text, &command->text_offset, &command->text_len, result)) {
        return false;
    }

    profile->command_count++;
    return true;
}


static bool acx_account_runtime_command(acx_plan_t *plan,
                                       HeadRuntimeProfile *candidate,
                                       bool materialize,
                                       app_profile_runtime_command_type_t type,
                                       const char *text,
                                       int line_number,
                                       int can_bus,
                                       uint32_t can_id,
                                       const uint8_t *data,
                                       uint8_t dlc,
                                       int wait_ms,
                                       bool uses_dynamic_j_placeholder,
                                       AcxProfileLoadResult *result)
{
    size_t text_len;

    if (plan == NULL || text == NULL) {
        return false;
    }

    text_len = strlen(text) + 1U;
    if (text_len == 0
        || plan->materialized_commands >= ACX_PROFILE_MAX_COMMANDS
        || plan->command_text_bytes + text_len > ACX_PROFILE_COMMAND_TEXT_BYTES) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "runtime command capacity exceeded");
        return false;
    }

    if (materialize && candidate != NULL) {
        if (!acx_append_runtime_command(candidate,
                                        type,
                                        text,
                                        line_number,
                                        can_bus,
                                        can_id,
                                        data,
                                        dlc,
                                        wait_ms,
                                        uses_dynamic_j_placeholder,
                                        result)) {
            return false;
        }
    }

    plan->materialized_commands++;
    plan->command_text_bytes += (uint32_t)text_len;
    return true;
}

static bool acx_finalize_candidate_view(HeadRuntimeProfile *profile)
{
    if (profile == NULL) {
        return false;
    }

    profile->command_profile.program_name = profile->profile_name;
    profile->command_profile.init_sequence.phase1_steps = profile->init_phase1_ptrs;
    profile->command_profile.init_sequence.phase2_steps = profile->init_phase2_ptrs;
    profile->command_profile.den.run_sequence = profile->den_run_sequence;
    profile->command_profile.den.alternate_run_sequence = profile->den_alternate_run_sequence;
    profile->command_profile.den.positions = profile->den_positions;
    profile->command_profile.sic.run_sequence = profile->sic_run_sequence;
    profile->command_profile.sic.alternate_run_sequence = NULL;
    profile->command_profile.sic.positions = profile->sic_positions;
    profile->command_profile.feet.run_sequence = profile->feet_run_sequence;
    profile->command_profile.feet.alternate_run_sequence = NULL;
    profile->command_profile.feet.positions = profile->feet_positions;
    profile->command_profile.yarn.addresses = profile->yarn_addresses;
    profile->command_profile.stitch.addresses = profile->stitch_addresses;

    profile->command_profile.den.instance_count = (size_t)profile->modules.den_count;
    profile->command_profile.sic.instance_count = (size_t)profile->modules.sic_count;
    profile->command_profile.j.instance_count = (size_t)profile->modules.j_count;
    profile->command_profile.yarn.instance_count = (size_t)profile->modules.yarn_count;
    profile->command_profile.stitch.instance_count = (size_t)profile->modules.stitch_count;
    profile->command_profile.feet.instance_count = (size_t)profile->modules.feet_count;

    return true;
}

static bool acx_validate_required_directory(const acx_directory_entry_t entries[ACX_PROFILE_MAX_SECTION_COUNT],
                                            const uint8_t *seen,
                                            AcxProfileLoadResult *result)
{
    static const uint16_t required_ids[ACX_PROFILE_MAX_SECTION_COUNT] = {
        ACX_PROFILE_SECTION_METADATA,
        ACX_PROFILE_SECTION_INIT,
        ACX_PROFILE_SECTION_TESTEO,
        ACX_PROFILE_SECTION_MOTION,
        ACX_PROFILE_SECTION_J,
        ACX_PROFILE_SECTION_CASCADE,
        ACX_PROFILE_SECTION_STOP,
        ACX_PROFILE_SECTION_ACTIONS,
    };

    for (size_t i = 0; i < ACX_PROFILE_MAX_SECTION_COUNT; ++i) {
        if (!seen[required_ids[i]]) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_MISSING_SECTION,
                                 "missing ACX section %s",
                                 acx_section_name(required_ids[i]));
            result->failing_section_id = required_ids[i];
            return false;
        }
    }

    (void)entries;
    return true;
}


static void acx_sort_indices_by_offset(const acx_directory_entry_t entries[ACX_PROFILE_MAX_SECTION_COUNT],
                                       size_t indices[ACX_PROFILE_MAX_SECTION_COUNT])
{
    for (size_t i = 0; i < ACX_PROFILE_MAX_SECTION_COUNT; ++i) {
        indices[i] = i;
    }

    for (size_t i = 1; i < ACX_PROFILE_MAX_SECTION_COUNT; ++i) {
        size_t key = indices[i];
        size_t j = i;
        while (j > 0 && entries[indices[j - 1]].offset > entries[key].offset) {
            indices[j] = indices[j - 1];
            --j;
        }
        indices[j] = key;
    }
}

static bool acx_read_header(acx_input_t *input,
                            AcxProfileLoadResult *result,
                            uint32_t *file_size,
                            uint32_t *section_directory_offset,
                            uint32_t *section_directory_size,
                            uint32_t *payload_offset,
                            uint32_t *payload_size,
                            uint32_t *payload_crc32,
                            uint16_t *section_count,
                            acx_plan_t *plan)
{
    uint8_t header[ACX_PROFILE_HEADER_SIZE];
    size_t offset = 0;
    uint16_t header_size = 0;
    uint16_t reserved16 = 0;
    uint32_t reserved32_2 = 0;
    uint32_t reserved32_3 = 0;
    uint16_t format_version = 0;
    const uint8_t magic[4] = {'A', 'C', 'X', '1'};

    if (!acx_input_read_exact(input, header, sizeof(header))) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "ACX header is truncated");
        return false;
    }

    if (memcmp(header, magic, sizeof(magic)) != 0) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_MAGIC,
                             "invalid ACX magic");
        return false;
    }

    offset = 4;
    format_version = (uint16_t)(header[offset] | ((uint16_t)header[offset + 1] << 8));
    offset += 2;
    header_size = (uint16_t)(header[offset] | ((uint16_t)header[offset + 1] << 8));
    offset += 2;
    *file_size = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    *section_directory_offset = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    *section_directory_size = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    *payload_offset = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    *payload_size = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    *payload_crc32 = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    plan->profile_id = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    plan->profile_version_id = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    plan->program_number = (int32_t)((uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24));
    offset += 4;
    plan->version_number = (int32_t)((uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24));
    offset += 4;
    plan->schema_version = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    *section_count = (uint16_t)(header[offset] | ((uint16_t)header[offset + 1] << 8));
    offset += 2;
    reserved16 = (uint16_t)(header[offset] | ((uint16_t)header[offset + 1] << 8));
    offset += 2;
    reserved32_2 = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;
    reserved32_3 = (uint32_t)header[offset]
        | ((uint32_t)header[offset + 1] << 8)
        | ((uint32_t)header[offset + 2] << 16)
        | ((uint32_t)header[offset + 3] << 24);
    offset += 4;

    if (format_version != ACX_PROFILE_FORMAT_VERSION) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_UNSUPPORTED_VERSION,
                             "unsupported ACX format version %" PRIu16,
                             format_version);
        return false;
    }

    if (header_size != ACX_PROFILE_HEADER_SIZE) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_HEADER,
                             "unexpected ACX header size %" PRIu16,
                             header_size);
        return false;
    }

    if (reserved16 != 0 || reserved32_2 != 0 || reserved32_3 != 0) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO,
                             "ACX header reserved fields must be zero");
        return false;
    }

    if (*file_size < ACX_PROFILE_HEADER_SIZE) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "ACX file is smaller than the fixed header");
        return false;
    }

    if (*file_size > ACX_PROFILE_TRANSPORT_MAX_BYTES) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_LARGE,
                             "ACX file exceeds the 64 KiB transport limit");
        return false;
    }

    if ((size_t)*file_size != input->size) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_HEADER,
                             "declared ACX file size does not match source size");
        return false;
    }

    if (*section_directory_offset != ACX_PROFILE_PAYLOAD_OFFSET
        || *payload_offset != ACX_PROFILE_PAYLOAD_OFFSET) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_HEADER,
                             "ACX v1 expects the directory and payload to start at offset 64");
        return false;
    }

    if (*payload_size + *payload_offset != *file_size) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_HEADER,
                             "ACX payload size does not match file size");
        return false;
    }

    if (*section_count != ACX_PROFILE_REQUIRED_SECTION_COUNT) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                             "ACX package must contain exactly %u sections",
                             (unsigned)ACX_PROFILE_REQUIRED_SECTION_COUNT);
        return false;
    }

    if (*section_directory_size != (uint32_t)(*section_count) * ACX_PROFILE_SECTION_DIRECTORY_ENTRY_SIZE) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_DIRECTORY,
                             "ACX directory size is inconsistent with section count");
        return false;
    }

    if (*section_directory_size > *payload_size) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_DIRECTORY,
                             "ACX directory is larger than payload");
        return false;
    }

    if (*section_directory_offset + *section_directory_size > *file_size) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_DIRECTORY,
                             "ACX directory exceeds file length");
        return false;
    }

    result->file_size = *file_size;
    result->header_size = ACX_PROFILE_HEADER_SIZE;
    result->payload_offset = *payload_offset;
    result->payload_size = *payload_size;
    result->section_directory_size = *section_directory_size;
    result->section_count = *section_count;
    plan->file_size = *file_size;
    plan->section_directory_size = *section_directory_size;
    plan->section_count = *section_count;
    result->declared_payload_crc32 = *payload_crc32;
    plan->temp_buffer_max = ACX_PROFILE_TEMP_BUFFER_MAX_BYTES;
    result->temp_buffer_max = ACX_PROFILE_TEMP_BUFFER_MAX_BYTES;
    return true;
}


static bool acx_read_directory(acx_input_t *input,
                               const acx_plan_t *plan,
                               acx_directory_entry_t entries[ACX_PROFILE_MAX_SECTION_COUNT],
                               uint8_t seen[ACX_PROFILE_MAX_SECTION_COUNT + 1],
                               AcxProfileLoadResult *result,
                               uint32_t *payload_crc32)
{
    uint8_t directory[ACX_PROFILE_SECTION_DIRECTORY_ENTRY_SIZE * ACX_PROFILE_MAX_SECTION_COUNT];
    size_t offset = 0;
    size_t order[ACX_PROFILE_MAX_SECTION_COUNT];

    if (plan == NULL || entries == NULL || seen == NULL) {
        return false;
    }

    if (!acx_input_read_exact(input, directory, plan->section_directory_size)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "ACX directory is truncated");
        return false;
    }

    if (payload_crc32 != NULL) {
        *payload_crc32 = acx_crc32_update(*payload_crc32, directory, plan->section_directory_size);
    }

    for (uint16_t i = 0; i < plan->section_count; ++i) {
        acx_directory_entry_t *entry = &entries[i];
        entry->section_id = (uint16_t)(directory[offset] | ((uint16_t)directory[offset + 1] << 8));
        offset += 2;
        entry->section_version = (uint16_t)(directory[offset] | ((uint16_t)directory[offset + 1] << 8));
        offset += 2;
        entry->offset = (uint32_t)directory[offset]
            | ((uint32_t)directory[offset + 1] << 8)
            | ((uint32_t)directory[offset + 2] << 16)
            | ((uint32_t)directory[offset + 3] << 24);
        offset += 4;
        entry->size = (uint32_t)directory[offset]
            | ((uint32_t)directory[offset + 1] << 8)
            | ((uint32_t)directory[offset + 2] << 16)
            | ((uint32_t)directory[offset + 3] << 24);
        offset += 4;
        entry->record_count = (uint32_t)directory[offset]
            | ((uint32_t)directory[offset + 1] << 8)
            | ((uint32_t)directory[offset + 2] << 16)
            | ((uint32_t)directory[offset + 3] << 24);
        offset += 4;
        entry->crc32 = (uint32_t)directory[offset]
            | ((uint32_t)directory[offset + 1] << 8)
            | ((uint32_t)directory[offset + 2] << 16)
            | ((uint32_t)directory[offset + 3] << 24);
        offset += 4;
        uint32_t reserved = (uint32_t)directory[offset]
            | ((uint32_t)directory[offset + 1] << 8)
            | ((uint32_t)directory[offset + 2] << 16)
            | ((uint32_t)directory[offset + 3] << 24);
        offset += 4;

        if (reserved != 0) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO,
                                 "ACX directory reserved field must be zero");
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (entry->section_version != ACX_PROFILE_SECTION_VERSION_SUPPORTED) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_UNSUPPORTED_SECTION_VERSION,
                                 "section %s uses unsupported version %u",
                                 acx_section_name(entry->section_id),
                                 (unsigned)entry->section_version);
            result->failing_section_id = entry->section_id;
            result->failing_section_version = entry->section_version;
            return false;
        }

        if (entry->section_id < ACX_PROFILE_SECTION_METADATA
            || entry->section_id > ACX_PROFILE_SECTION_ACTIONS) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_DIRECTORY,
                                 "directory contains unsupported section id %u",
                                 (unsigned)entry->section_id);
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (seen[entry->section_id]) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_DUPLICATE_SECTION,
                                 "section %s is duplicated",
                                 acx_section_name(entry->section_id));
            result->failing_section_id = entry->section_id;
            return false;
        }

        seen[entry->section_id] = 1;

        if (entry->size == 0) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "section %s is empty",
                                 acx_section_name(entry->section_id));
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (entry->offset < ACX_PROFILE_HEADER_SIZE + plan->section_directory_size) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_OFFSET,
                                 "section %s overlaps the header or directory",
                                 acx_section_name(entry->section_id));
            result->failing_section_id = entry->section_id;
            result->failing_offset = entry->offset;
            result->failing_size = entry->size;
            return false;
        }

        if ((uint64_t)entry->offset + (uint64_t)entry->size > plan->file_size) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_OFFSET,
                                 "section %s exceeds the file length",
                                 acx_section_name(entry->section_id));
            result->failing_section_id = entry->section_id;
            result->failing_offset = entry->offset;
            result->failing_size = entry->size;
            return false;
        }
    }

    if (!acx_validate_required_directory(entries, seen, result)) {
        return false;
    }

    acx_sort_indices_by_offset(entries, order);
    {
        const uint64_t expected_first_offset = (uint64_t)ACX_PROFILE_HEADER_SIZE
                                             + (uint64_t)plan->section_directory_size;
        const acx_directory_entry_t *first = &entries[order[0]];

        if ((uint64_t)first->offset != expected_first_offset) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_OFFSET,
                                 "sections must start immediately after the directory");
            result->failing_section_id = first->section_id;
            result->failing_offset = first->offset;
            result->failing_size = first->size;
            return false;
        }
    }

    for (size_t i = 0; i + 1 < (size_t)plan->section_count; ++i) {
        const acx_directory_entry_t *current = &entries[order[i]];
        const acx_directory_entry_t *next = &entries[order[i + 1]];
        uint64_t current_end = (uint64_t)current->offset + (uint64_t)current->size;

        if ((uint64_t)next->offset != current_end) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_OFFSET,
                                 "sections %s and %s are not contiguous",
                                 acx_section_name(current->section_id),
                                 acx_section_name(next->section_id));
            result->failing_section_id = next->section_id;
            result->failing_offset = next->offset;
            result->failing_size = next->size;
            return false;
        }
    }

    {
        const acx_directory_entry_t *last = &entries[order[(size_t)plan->section_count - 1]];
        uint64_t last_end = (uint64_t)last->offset + (uint64_t)last->size;

        if (last_end != (uint64_t)plan->file_size) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_OFFSET,
                                 "last section must end at the file size");
            result->failing_section_id = last->section_id;
            result->failing_offset = last->offset;
            result->failing_size = last->size;
            return false;
        }
    }

    return true;
}


static bool acx_validate_section_span(const acx_directory_entry_t *entry,
                                      uint32_t expected_records,
                                      AcxProfileLoadResult *result)
{
    if (entry == NULL) {
        return false;
    }

    if (entry->record_count != expected_records) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "section %s has an unexpected record count",
                             acx_section_name(entry->section_id));
        result->failing_section_id = entry->section_id;
        result->failing_record_count = entry->record_count;
        return false;
    }

    return true;
}

static bool acx_validate_bus(uint8_t bus)
{
    return bus == 0 || bus == APP_CMD_CAN_BUS_1 || bus == APP_CMD_CAN_BUS_2;
}

static bool acx_validate_can_id(uint32_t can_id)
{
    return can_id <= ACX_PROFILE_MAX_CAN_ID;
}

static bool acx_validate_wait(uint32_t wait_ms)
{
    return wait_ms <= ACX_PROFILE_MAX_WAIT_MS;
}

static bool acx_validate_dlc(uint8_t dlc)
{
    return dlc <= ACX_PROFILE_MAX_CAN_DLC;
}

static bool acx_map_program_number(int32_t program_number, app_head_program_id_t *program_id)
{
    if (program_id == NULL) {
        return false;
    }

    switch (program_number) {
    case 1:
        *program_id = APP_HEAD_PROGRAM_1;
        return true;
    case 2:
        *program_id = APP_HEAD_PROGRAM_2;
        return true;
    case 3:
        *program_id = APP_HEAD_PROGRAM_3;
        return true;
    default:
        return false;
    }
}

static bool acx_copy_runtime_name(const char *src, char *dst, size_t dst_size)
{
    if (src == NULL || dst == NULL || dst_size == 0) {
        return false;
    }

    strlcpy(dst, src, dst_size);
    return true;
}



static bool acx_parse_metadata_section(acx_section_reader_t *reader,
                                       const acx_directory_entry_t *entry,
                                       acx_plan_t *plan,
                                       HeadRuntimeProfile *candidate,
                                       bool materialize,
                                       AcxProfileLoadResult *result)
{
    uint32_t profile_id = 0;
    uint32_t profile_version_id = 0;
    uint8_t enabled = 0;
    uint8_t is_published = 0;
    uint8_t source_kind = 0;
    uint8_t has_source_crc32 = 0;
    int32_t program_number = 0;
    int32_t version_number = 0;
    uint32_t schema_version = 0;
    uint16_t profile_key_len = 0;
    uint16_t display_name_len = 0;
    uint16_t description_len = 0;
    uint16_t notes_len = 0;
    uint32_t source_crc32 = 0;
    char profile_key[ACX_PROFILE_MAX_FILENAME_LEN + 1];
    char display_name[ACX_PROFILE_MAX_PROFILE_NAME_LEN + 1];

    if (!acx_validate_section_span(entry, 1, result)) {
        return false;
    }

    if (!acx_section_reader_read_u32(reader, &profile_id)
        || !acx_section_reader_read_u32(reader, &profile_version_id)
        || !acx_section_reader_read_u8(reader, &enabled)
        || !acx_section_reader_read_u8(reader, &is_published)
        || !acx_section_reader_read_u8(reader, &source_kind)
        || !acx_section_reader_read_u8(reader, &has_source_crc32)
        || !acx_section_reader_read_i32(reader, &program_number)
        || !acx_section_reader_read_i32(reader, &version_number)
        || !acx_section_reader_read_u32(reader, &schema_version)
        || !acx_section_reader_read_u16(reader, &profile_key_len)
        || !acx_section_reader_read_u16(reader, &display_name_len)
        || !acx_section_reader_read_u16(reader, &description_len)
        || !acx_section_reader_read_u16(reader, &notes_len)
        || !acx_section_reader_read_u32(reader, &source_crc32)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "metadata section is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (source_kind < 1 || source_kind > 3) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "metadata source kind is not supported");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (has_source_crc32 > 1 || enabled > 1 || is_published > 1) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO,
                             "metadata boolean fields must be 0 or 1");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (program_number < 1 || program_number > 3) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                             "program number %" PRId32 " is out of range",
                             program_number);
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (version_number < 0) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                             "version number cannot be negative");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (profile_key_len == 0 || profile_key_len > ACX_PROFILE_MAX_FILENAME_LEN) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "profile key is too long");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (display_name_len == 0 || display_name_len > ACX_PROFILE_MAX_PROFILE_NAME_LEN) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "display name is too long");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (has_source_crc32 == 0 && source_crc32 != 0) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO,
                             "source CRC must be zero when absent");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (plan != NULL
        && (profile_id != plan->profile_id
            || profile_version_id != plan->profile_version_id
            || program_number != plan->program_number
            || version_number != plan->version_number
            || schema_version != plan->schema_version)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "metadata does not match the header");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (!acx_section_reader_read_string_small(reader,
                                              profile_key_len,
                                              profile_key,
                                              sizeof(profile_key),
                                              true,
                                              result)
        || !acx_section_reader_read_string_small(reader,
                                                 display_name_len,
                                                 display_name,
                                                 sizeof(display_name),
                                                 true,
                                                 result)
        || !acx_section_reader_read_validated_utf8(reader,
                                                   description_len,
                                                   NULL,
                                                   0,
                                                   false,
                                                   result)
        || !acx_section_reader_read_validated_utf8(reader,
                                                   notes_len,
                                                   NULL,
                                                   0,
                                                   false,
                                                   result)) {
        result->failing_section_id = entry->section_id;
        return false;
    }

    plan->profile_id = profile_id;
    plan->profile_version_id = profile_version_id;
    plan->profile_enabled = enabled != 0;
    plan->profile_published = is_published != 0;
    plan->source_kind = source_kind;
    plan->has_source_crc32 = has_source_crc32 != 0;
    plan->source_crc32 = source_crc32;
    plan->program_number = program_number;
    plan->version_number = version_number;
    plan->schema_version = schema_version;

    result->profile_id = profile_id;
    result->profile_version_id = profile_version_id;
    result->profile_enabled = enabled != 0;
    result->profile_published = is_published != 0;
    result->has_source_crc32 = has_source_crc32 != 0;
    result->source_kind = source_kind;
    result->source_crc32 = source_crc32;
    result->program_number = program_number;
    result->version_number = version_number;
    result->schema_version = schema_version;
    acx_copy_runtime_name(profile_key, result->profile_key, sizeof(result->profile_key));
    acx_copy_runtime_name(display_name, result->display_name, sizeof(result->display_name));

    if (materialize && candidate != NULL) {
        candidate->origin = APP_PROFILE_ORIGIN_FILE;
        candidate->generation = 0;
        candidate->has_profile_name = true;
        candidate->has_declared_program = true;
        candidate->has_version = true;
        candidate->has_declared_crc = true;
        candidate->has_declared_system = true;
        candidate->profile_name[0] = '\0';
        candidate->system[0] = '\0';
        candidate->init_script[0] = '\0';
        candidate->source_size = result->file_size;
        candidate->program_number = (uint16_t)program_number;
        candidate->version = (uint32_t)version_number;
        candidate->declared_crc32 = result->declared_payload_crc32;
        candidate->computed_crc32 = result->computed_payload_crc32;
        candidate->crc_ok = true;
        strlcpy(candidate->filename, result->file_name, sizeof(candidate->filename));
        strlcpy(candidate->profile_name, display_name, sizeof(candidate->profile_name));
        strlcpy(candidate->system, "ACX", sizeof(candidate->system));
    }

    return true;
}


static bool acx_parse_init_section(acx_section_reader_t *reader,
                                   const acx_directory_entry_t *entry,
                                   acx_plan_t *plan,
                                   HeadRuntimeProfile *candidate,
                                   bool materialize,
                                   AcxProfileLoadResult *result)
{
    uint32_t phase1_delay = 0;
    uint32_t phase_gap = 0;
    uint32_t phase2_delay = 0;
    uint16_t phase1_count = 0;
    uint16_t phase2_count = 0;
    uint32_t total_steps = 0;
    uint32_t running_order = 0;

    if (!acx_section_reader_read_u32(reader, &phase1_delay)
        || !acx_section_reader_read_u32(reader, &phase_gap)
        || !acx_section_reader_read_u32(reader, &phase2_delay)
        || !acx_section_reader_read_u16(reader, &phase1_count)
        || !acx_section_reader_read_u16(reader, &phase2_count)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "INIT section is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (phase1_count > ACX_PROFILE_MAX_INIT_STEPS || phase2_count > ACX_PROFILE_MAX_INIT_STEPS) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "INIT phase count exceeds runtime capacity");
        result->failing_section_id = entry->section_id;
        return false;
    }

    total_steps = (uint32_t)phase1_count + (uint32_t)phase2_count;
    if (entry->record_count != total_steps) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                             "INIT record count does not match step count");
        result->failing_section_id = entry->section_id;
        result->failing_record_count = entry->record_count;
        return false;
    }

    if (materialize && candidate != NULL) {
        candidate->command_profile.init_sequence.phase1_step_delay_ms = phase1_delay;
        candidate->command_profile.init_sequence.phase_gap_ms = phase_gap;
        candidate->command_profile.init_sequence.phase2_step_delay_ms = phase2_delay;
        candidate->command_profile.init_sequence.phase1_step_count = phase1_count;
        candidate->command_profile.init_sequence.phase2_step_count = phase2_count;
    }

    for (uint32_t i = 0; i < total_steps; ++i) {
        uint16_t step_order = 0;
        uint8_t phase = 0;
        uint8_t step_kind = 0;
        uint8_t bus = 0;
        uint8_t dlc = 0;
        uint16_t raw_text_len = 0;
        uint32_t can_id = 0;
        uint32_t wait_ms = 0;
        uint8_t data[ACX_PROFILE_MAX_CAN_DLC];
        char raw_text[ACX_PROFILE_MAX_LINE_LEN + 1];
        bool is_phase1 = i < (uint32_t)phase1_count;

        memset(data, 0, sizeof(data));
        memset(raw_text, 0, sizeof(raw_text));

        if (!acx_section_reader_read_u16(reader, &step_order)
            || !acx_section_reader_read_u8(reader, &phase)
            || !acx_section_reader_read_u8(reader, &step_kind)
            || !acx_section_reader_read_u8(reader, &bus)
            || !acx_section_reader_read_u8(reader, &dlc)
            || !acx_section_reader_read_u16(reader, &raw_text_len)
            || !acx_section_reader_read_u32(reader, &can_id)
            || !acx_section_reader_read_u32(reader, &wait_ms)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                 "INIT step is truncated");
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (step_order != running_order || (is_phase1 && phase != 1) || (!is_phase1 && phase != 2)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "INIT step order or phase is invalid");
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (step_kind < 1 || step_kind > 3) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "INIT step kind is invalid");
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (!acx_validate_bus(bus)
            || !acx_validate_dlc(dlc)
            || !acx_validate_can_id(can_id)
            || !acx_validate_wait(wait_ms)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "INIT step has invalid bus, DLC, CAN id or wait");
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (step_kind == 1) {
            if (bus == 0 || wait_ms != 0) {
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                     "INIT CAN step is invalid");
                result->failing_section_id = entry->section_id;
                return false;
            }
        } else if (step_kind == 2) {
            if (bus != 0 || can_id != 0 || dlc != 0) {
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                     "INIT WAIT step must not carry CAN data");
                result->failing_section_id = entry->section_id;
                return false;
            }
        } else if (step_kind == 3) {
            if (bus != 0 || can_id != 0 || dlc != 0 || wait_ms != 0) {
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                     "INIT STATUS step must not carry CAN or wait data");
                result->failing_section_id = entry->section_id;
                return false;
            }
        }

        if (raw_text_len == 0 || raw_text_len > ACX_PROFILE_MAX_LINE_LEN) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                 "INIT raw_text does not fit runtime buffer");
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (!acx_section_reader_read_string_small(reader,
                                                  raw_text_len,
                                                  raw_text,
                                                  sizeof(raw_text),
                                                  true,
                                                  result)) {
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (dlc > 0) {
            if (!acx_section_reader_read(reader, data, dlc)) {
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                     "INIT CAN payload is truncated");
                result->failing_section_id = entry->section_id;
                return false;
            }
        }

        if (materialize && candidate != NULL) {
            const char **line_out = NULL;
            if (candidate->command_profile.init_sequence.phase1_steps == NULL
                || candidate->command_profile.init_sequence.phase2_steps == NULL) {
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_INVALID_HEADER,
                                     "INIT pointer view is not available");
                result->failing_section_id = entry->section_id;
                return false;
            }

            if (candidate->init_text_used + strlen(raw_text) + 1U > sizeof(candidate->init_text)) {
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                     "INIT text buffer is full");
                result->failing_section_id = entry->section_id;
                return false;
            }

            if (is_phase1) {
                line_out = &candidate->init_phase1_ptrs[running_order];
            } else {
                line_out = &candidate->init_phase2_ptrs[running_order - phase1_count];
            }

            if (!acx_store_init_line(candidate, raw_text, line_out, result)) {
                result->failing_section_id = entry->section_id;
                return false;
            }
        }

        plan->init_text_bytes += (uint32_t)strlen(raw_text) + 1U;
        if (plan->init_text_bytes > ACX_PROFILE_INIT_TEXT_BYTES) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                 "INIT text exceeds runtime buffer capacity");
            result->failing_section_id = entry->section_id;
            return false;
        }

        ++running_order;
    }

    if (materialize && candidate != NULL) {
        candidate->command_profile.init_sequence.phase1_steps = candidate->init_phase1_ptrs;
        candidate->command_profile.init_sequence.phase2_steps = candidate->init_phase2_ptrs;
    }

    result->init_text_bytes = plan->init_text_bytes;
    return true;
}


static bool acx_parse_testeo_section(acx_section_reader_t *reader,
                                     const acx_directory_entry_t *entry,
                                     HeadRuntimeProfile *candidate,
                                     bool materialize,
                                     AcxProfileLoadResult *result)
{
    uint32_t ping_can_id = 0;
    uint8_t ping_dlc = 0;
    uint32_t response_can_id = 0;
    uint32_t reset_can_id = 0;
    uint8_t reset_dlc = 0;
    uint8_t success_code = 0;
    uint8_t missing_expansion_code = 0;
    uint8_t missing_force_code = 0;
    uint8_t force_board_1_code = 0;
    uint8_t force_board_2_code = 0;
    uint16_t max_tries = 0;
    uint32_t response_timeout_ms = 0;
    uint32_t retry_delay_ms = 0;
    uint32_t reset_debounce_ms = 0;
    uint8_t ping_data[ACX_PROFILE_MAX_CAN_DLC];
    uint8_t reset_data[ACX_PROFILE_MAX_CAN_DLC];

    if (!acx_validate_section_span(entry, 1, result)) {
        return false;
    }

    memset(ping_data, 0, sizeof(ping_data));
    memset(reset_data, 0, sizeof(reset_data));

    if (!acx_section_reader_read_u32(reader, &ping_can_id)
        || !acx_section_reader_read_u8(reader, &ping_dlc)
        || !acx_section_reader_read_u32(reader, &response_can_id)
        || !acx_section_reader_read_u32(reader, &reset_can_id)
        || !acx_section_reader_read_u8(reader, &reset_dlc)
        || !acx_section_reader_read_u8(reader, &success_code)
        || !acx_section_reader_read_u8(reader, &missing_expansion_code)
        || !acx_section_reader_read_u8(reader, &missing_force_code)
        || !acx_section_reader_read_u8(reader, &force_board_1_code)
        || !acx_section_reader_read_u8(reader, &force_board_2_code)
        || !acx_section_reader_read_u16(reader, &max_tries)
        || !acx_section_reader_read_u32(reader, &response_timeout_ms)
        || !acx_section_reader_read_u32(reader, &retry_delay_ms)
        || !acx_section_reader_read_u32(reader, &reset_debounce_ms)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "TESTEO section is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (!acx_validate_can_id(ping_can_id)
        || !acx_validate_can_id(response_can_id)
        || !acx_validate_can_id(reset_can_id)
        || !acx_validate_dlc(ping_dlc)
        || !acx_validate_dlc(reset_dlc)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "TESTEO contains invalid CAN configuration");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (ping_dlc > 0 && !acx_section_reader_read(reader, ping_data, ping_dlc)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "TESTEO ping payload is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (reset_dlc > 0 && !acx_section_reader_read(reader, reset_data, reset_dlc)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "TESTEO reset payload is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (materialize && candidate != NULL) {
        candidate->command_profile.testeo.ping.can_id = ping_can_id;
        candidate->command_profile.testeo.ping.dlc = ping_dlc;
        memcpy(candidate->command_profile.testeo.ping.data, ping_data, ping_dlc);
        candidate->command_profile.testeo.response_can_id = response_can_id;
        candidate->command_profile.testeo.reset_can_id = reset_can_id;
        candidate->command_profile.testeo.reset_dlc = reset_dlc;
        memcpy(candidate->command_profile.testeo.reset_data, reset_data, reset_dlc);
        candidate->command_profile.testeo.success_code = success_code;
        candidate->command_profile.testeo.missing_expansion_code = missing_expansion_code;
        candidate->command_profile.testeo.missing_force_code = missing_force_code;
        candidate->command_profile.testeo.force_board_1_code = force_board_1_code;
        candidate->command_profile.testeo.force_board_2_code = force_board_2_code;
        candidate->command_profile.testeo.max_tries = max_tries;
        candidate->command_profile.testeo.response_timeout_ms = response_timeout_ms;
        candidate->command_profile.testeo.retry_delay_ms = retry_delay_ms;
        candidate->command_profile.testeo.reset_debounce_ms = reset_debounce_ms;
    }

    return true;
}

static bool acx_parse_motion_module(acx_section_reader_t *reader,
                                    const acx_directory_entry_t *entry,
                                    uint16_t module_index,
                                    acx_plan_t *plan,
                                    HeadRuntimeProfile *candidate,
                                    bool materialize,
                                    AcxProfileLoadResult *result)
{
    uint8_t module_kind = 0;
    uint8_t opcode = 0;
    uint8_t motor_index_base = 0;
    uint8_t reserved = 0;
    uint16_t instance_count = 0;
    uint16_t position_count = 0;
    uint16_t run_sequence_count = 0;
    uint16_t alternate_run_sequence_count = 0;
    uint32_t can_id = 0;
    uint32_t run_period_ms = 0;
    uint32_t alternate_run_period_ms = 0;
    uint16_t max_instances = 0;
    HeadMotionCommandProfile *motion = NULL;
    uint8_t *motion_run = NULL;
    uint8_t *motion_alt = NULL;
    uint16_t *motion_positions = NULL;

    (void)plan;

    if (!acx_section_reader_read_u8(reader, &module_kind)
        || !acx_section_reader_read_u8(reader, &opcode)
        || !acx_section_reader_read_u8(reader, &motor_index_base)
        || !acx_section_reader_read_u8(reader, &reserved)
        || !acx_section_reader_read_u16(reader, &instance_count)
        || !acx_section_reader_read_u16(reader, &position_count)
        || !acx_section_reader_read_u16(reader, &run_sequence_count)
        || !acx_section_reader_read_u16(reader, &alternate_run_sequence_count)
        || !acx_section_reader_read_u32(reader, &can_id)
        || !acx_section_reader_read_u32(reader, &run_period_ms)
        || !acx_section_reader_read_u32(reader, &alternate_run_period_ms)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "MOTION module is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (reserved != 0 || !acx_validate_can_id(can_id)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO,
                             "MOTION module contains reserved or invalid CAN fields");
        result->failing_section_id = entry->section_id;
        return false;
    }

    switch (module_kind) {
    case 1:
        if (module_index != 0) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "MOTION section order does not match DEN/SIC/FEET");
            result->failing_section_id = entry->section_id;
            return false;
        }
        max_instances = 8U;
        motion = materialize && candidate != NULL ? &candidate->command_profile.den : NULL;
        motion_run = materialize && candidate != NULL ? candidate->den_run_sequence : NULL;
        motion_alt = materialize && candidate != NULL ? candidate->den_alternate_run_sequence : NULL;
        motion_positions = materialize && candidate != NULL ? candidate->den_positions : NULL;
        break;
    case 2:
        if (module_index != 1) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "MOTION section order does not match DEN/SIC/FEET");
            result->failing_section_id = entry->section_id;
            return false;
        }
        max_instances = APP_HEAD_STATE_MAX_SIC;
        motion = materialize && candidate != NULL ? &candidate->command_profile.sic : NULL;
        motion_run = materialize && candidate != NULL ? candidate->sic_run_sequence : NULL;
        motion_positions = materialize && candidate != NULL ? candidate->sic_positions : NULL;
        break;
    case 3:
        if (module_index != 2) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "MOTION section order does not match DEN/SIC/FEET");
            result->failing_section_id = entry->section_id;
            return false;
        }
        max_instances = APP_HEAD_STATE_MAX_FEET;
        motion = materialize && candidate != NULL ? &candidate->command_profile.feet : NULL;
        motion_run = materialize && candidate != NULL ? candidate->feet_run_sequence : NULL;
        motion_positions = materialize && candidate != NULL ? candidate->feet_positions : NULL;
        break;
    default:
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "unsupported MOTION module kind");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (instance_count > max_instances
        || position_count > APP_PROFILE_MAX_POSITIONS
        || run_sequence_count > APP_PROFILE_MAX_MOTION_SEQUENCE
        || alternate_run_sequence_count > APP_PROFILE_MAX_MOTION_SEQUENCE) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "MOTION module exceeds runtime capacity");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (module_kind != 1 && alternate_run_sequence_count != 0) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "MOTION SIC/FEET modules cannot carry alternate run sequence data");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (materialize && motion != NULL) {
        motion->can_id = can_id;
        motion->opcode = opcode;
        motion->motor_index_base = motor_index_base;
        motion->instance_count = instance_count;
        motion->run_period_ms = run_period_ms;
        motion->alternate_run_period_ms = alternate_run_period_ms;
        motion->run_sequence = motion_run;
        motion->alternate_run_sequence = motion_alt;
        motion->positions = motion_positions;
        motion->run_sequence_count = run_sequence_count;
        motion->alternate_run_sequence_count = module_kind == 1 ? alternate_run_sequence_count : 0;
        motion->position_count = position_count;
    }

    if (candidate != NULL) {
        if (module_kind == 1) {
            candidate->modules.den_count = (int)instance_count;
        } else if (module_kind == 2) {
            candidate->modules.sic_count = (int)instance_count;
        } else {
            candidate->modules.feet_count = (int)instance_count;
        }
    }

    for (uint16_t i = 0; i < position_count; ++i) {
        uint16_t value = 0;
        if (!acx_section_reader_read_u16(reader, &value)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                 "MOTION positions are truncated");
            result->failing_section_id = entry->section_id;
            return false;
        }
        if (materialize && motion_positions != NULL) {
            motion_positions[i] = value;
        }
    }

    for (uint16_t i = 0; i < run_sequence_count; ++i) {
        uint8_t value = 0;
        if (!acx_section_reader_read_u8(reader, &value)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                 "MOTION run sequence is truncated");
            result->failing_section_id = entry->section_id;
            return false;
        }
        if (position_count > 0 && value >= position_count) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "MOTION run sequence references an invalid position");
            result->failing_section_id = entry->section_id;
            return false;
        }
        if (materialize && motion_run != NULL) {
            motion_run[i] = value;
        }
    }

    if (module_kind == 1) {
        for (uint16_t i = 0; i < alternate_run_sequence_count; ++i) {
            uint8_t value = 0;
            if (!acx_section_reader_read_u8(reader, &value)) {
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                     "MOTION alternate sequence is truncated");
                result->failing_section_id = entry->section_id;
                return false;
            }
            if (position_count > 0 && value >= position_count) {
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                     "MOTION alternate sequence references an invalid position");
                result->failing_section_id = entry->section_id;
                return false;
            }
            if (materialize && motion_alt != NULL) {
                motion_alt[i] = value;
            }
        }
    }

    return true;
}


static bool acx_parse_motion_section(acx_section_reader_t *reader,
                                     const acx_directory_entry_t *entry,
                                     acx_plan_t *plan,
                                     HeadRuntimeProfile *candidate,
                                     bool materialize,
                                     AcxProfileLoadResult *result)
{
    uint16_t module_count = 0;

    (void)plan;

    if (!acx_validate_section_span(entry, 3, result)) {
        return false;
    }

    if (!acx_section_reader_read_u16(reader, &module_count) || module_count != 3) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                             "MOTION section must contain exactly 3 modules");
        result->failing_section_id = entry->section_id;
        result->failing_record_count = module_count;
        return false;
    }

    for (uint16_t i = 0; i < module_count; ++i) {
        if (!acx_parse_motion_module(reader, entry, i, plan, candidate, materialize, result)) {
            return false;
        }
    }

    return true;
}


static bool acx_parse_j_section(acx_section_reader_t *reader,
                                const acx_directory_entry_t *entry,
                                HeadRuntimeProfile *candidate,
                                bool materialize,
                                AcxProfileLoadResult *result)
{
    uint32_t can_id = 0;
    uint8_t opcode = 0;
    uint8_t instance_index_base = 0;
    uint16_t instance_count = 0;
    uint8_t channel_count = 0;
    uint8_t initial_register = 0;
    uint8_t on_all_register = 0;
    uint8_t off_all_register = 0;
    uint32_t run_period_ms = 0;

    if (!acx_validate_section_span(entry, 1, result)) {
        return false;
    }

    if (!acx_section_reader_read_u32(reader, &can_id)
        || !acx_section_reader_read_u8(reader, &opcode)
        || !acx_section_reader_read_u8(reader, &instance_index_base)
        || !acx_section_reader_read_u16(reader, &instance_count)
        || !acx_section_reader_read_u8(reader, &channel_count)
        || !acx_section_reader_read_u8(reader, &initial_register)
        || !acx_section_reader_read_u8(reader, &on_all_register)
        || !acx_section_reader_read_u8(reader, &off_all_register)
        || !acx_section_reader_read_u32(reader, &run_period_ms)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "J section is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (!acx_validate_can_id(can_id) || channel_count > 8 || instance_count > APP_HEAD_STATE_MAX_J) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "J section has invalid CAN id, channel count or instance count");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (materialize && candidate != NULL) {
        candidate->command_profile.j.can_id = can_id;
        candidate->command_profile.j.opcode = opcode;
        candidate->command_profile.j.instance_index_base = instance_index_base;
        candidate->command_profile.j.instance_count = instance_count;
        candidate->command_profile.j.channel_count = channel_count;
        candidate->command_profile.j.initial_register = initial_register;
        candidate->command_profile.j.on_all_register = on_all_register;
        candidate->command_profile.j.off_all_register = off_all_register;
        candidate->command_profile.j.run_period_ms = run_period_ms;
        candidate->modules.j_count = (int)instance_count;
    }

    return true;
}


static bool acx_parse_cascade_module(acx_section_reader_t *reader,
                                     const acx_directory_entry_t *entry,
                                     uint16_t module_index,
                                     HeadRuntimeProfile *candidate,
                                     bool materialize,
                                     AcxProfileLoadResult *result)
{
    uint8_t module_kind = 0;
    uint8_t opcode = 0;
    uint16_t addresses_per_instance = 0;
    uint16_t instance_count = 0;
    uint16_t address_count = 0;
    uint32_t can_id = 0;
    uint32_t run_period_ms = 0;
    uint8_t on_value = 0;
    uint8_t off_value = 0;
    uint16_t reserved = 0;
    uint16_t max_instances = 0;
    size_t address_capacity = 0;
    uint64_t expected_address_count = 0;
    HeadCascadeCommandProfile *cascade = NULL;

    if (!acx_section_reader_read_u8(reader, &module_kind)
        || !acx_section_reader_read_u8(reader, &opcode)
        || !acx_section_reader_read_u16(reader, &addresses_per_instance)
        || !acx_section_reader_read_u16(reader, &instance_count)
        || !acx_section_reader_read_u16(reader, &address_count)
        || !acx_section_reader_read_u32(reader, &can_id)
        || !acx_section_reader_read_u32(reader, &run_period_ms)
        || !acx_section_reader_read_u8(reader, &on_value)
        || !acx_section_reader_read_u8(reader, &off_value)
        || !acx_section_reader_read_u16(reader, &reserved)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "CASCADE module is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (reserved != 0 || !acx_validate_can_id(can_id)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO,
                             "CASCADE module contains reserved or invalid CAN fields");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (module_index == 0) {
        if (module_kind != 1) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "CASCADE module order is invalid");
            result->failing_section_id = entry->section_id;
            return false;
        }
        max_instances = APP_HEAD_STATE_MAX_YARN;
        address_capacity = ACX_PROFILE_MAX_YARN_ADDRESSES;
        cascade = materialize && candidate != NULL ? &candidate->command_profile.yarn : NULL;
    } else if (module_index == 1) {
        if (module_kind != 2) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "CASCADE module order is invalid");
            result->failing_section_id = entry->section_id;
            return false;
        }
        max_instances = APP_HEAD_STATE_MAX_STITCH;
        address_capacity = ACX_PROFILE_MAX_STITCH_ADDRESSES;
        cascade = materialize && candidate != NULL ? &candidate->command_profile.stitch : NULL;
    } else {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "CASCADE module index is invalid");
        result->failing_section_id = entry->section_id;
        return false;
    }

    expected_address_count = (uint64_t)addresses_per_instance * (uint64_t)instance_count;
    if (addresses_per_instance == 0
        || instance_count > max_instances
        || expected_address_count > address_capacity
        || expected_address_count != (uint64_t)address_count) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                             "CASCADE module has inconsistent counts");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (materialize && cascade != NULL) {
        cascade->can_id = can_id;
        cascade->opcode = opcode;
        cascade->addresses_per_instance = addresses_per_instance;
        cascade->instance_count = instance_count;
        cascade->run_period_ms = run_period_ms;
        cascade->on_value = on_value;
        cascade->off_value = off_value;
    }

    for (uint16_t i = 0; i < address_count; ++i) {
        uint8_t value = 0;
        if (!acx_section_reader_read_u8(reader, &value)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                 "CASCADE addresses are truncated");
            result->failing_section_id = entry->section_id;
            return false;
        }
        if (materialize && cascade != NULL) {
            if (module_index == 0) {
                candidate->yarn_addresses[i] = value;
            } else {
                candidate->stitch_addresses[i] = value;
            }
        }
    }

    if (materialize && candidate != NULL) {
        if (module_index == 0) {
            candidate->modules.yarn_count = (int)instance_count;
        } else {
            candidate->modules.stitch_count = (int)instance_count;
        }
    }

    return true;
}


static bool acx_parse_cascade_section(acx_section_reader_t *reader,
                                      const acx_directory_entry_t *entry,
                                      HeadRuntimeProfile *candidate,
                                      bool materialize,
                                      AcxProfileLoadResult *result)
{
    uint16_t module_count = 0;

    if (!acx_validate_section_span(entry, 2, result)) {
        return false;
    }

    if (!acx_section_reader_read_u16(reader, &module_count) || module_count != 2) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                             "CASCADE section must contain exactly 2 modules");
        result->failing_section_id = entry->section_id;
        result->failing_record_count = module_count;
        return false;
    }

    for (uint16_t i = 0; i < module_count; ++i) {
        if (!acx_parse_cascade_module(reader, entry, i, candidate, materialize, result)) {
            return false;
        }
    }

    return true;
}

static bool acx_parse_stop_section(acx_section_reader_t *reader,
                                   const acx_directory_entry_t *entry,
                                   HeadRuntimeProfile *candidate,
                                   bool materialize,
                                   AcxProfileLoadResult *result)
{
    uint8_t sends_can_frame = 0;
    uint8_t frame_present = 0;
    uint16_t reserved = 0;
    uint32_t can_id = 0;
    uint8_t dlc = 0;
    uint8_t reserved2 = 0;
    uint8_t data[ACX_PROFILE_MAX_CAN_DLC];

    memset(data, 0, sizeof(data));

    if (!acx_validate_section_span(entry, 1, result)) {
        return false;
    }

    if (!acx_section_reader_read_u8(reader, &sends_can_frame)
        || !acx_section_reader_read_u8(reader, &frame_present)
        || !acx_section_reader_read_u16(reader, &reserved)
        || !acx_section_reader_read_u32(reader, &can_id)
        || !acx_section_reader_read_u8(reader, &dlc)
        || !acx_section_reader_read_u8(reader, &reserved2)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "STOP section is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (sends_can_frame > 1 || frame_present > 1) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "STOP boolean fields must be 0 or 1");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (reserved != 0 || reserved2 != 0 || !acx_validate_dlc(dlc) || !acx_validate_can_id(can_id)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO,
                             "STOP section contains invalid reserved or CAN fields");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (dlc > 0 && !acx_section_reader_read(reader, data, dlc)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "STOP payload is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (sends_can_frame == 0) {
        if (frame_present != 0 || can_id != 0 || dlc != 0) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "STOP section is inconsistent with sends_can_frame = 0");
            result->failing_section_id = entry->section_id;
            return false;
        }
    } else if (frame_present != 1) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                             "STOP section is missing its frame");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (materialize && candidate != NULL) {
        candidate->command_profile.stop.sends_can_frame = sends_can_frame != 0;
        candidate->command_profile.stop.frame.can_id = can_id;
        candidate->command_profile.stop.frame.dlc = dlc;
        memcpy(candidate->command_profile.stop.frame.data, data, dlc);
    }

    return true;
}


static bool acx_parse_actions_section(acx_section_reader_t *reader,
                                      const acx_directory_entry_t *entry,
                                      acx_plan_t *plan,
                                      HeadRuntimeProfile *candidate,
                                      bool materialize,
                                      AcxProfileLoadResult *result)
{
    uint16_t action_count = 0;
    char *seen_names = NULL;

    if (!acx_section_reader_read_u16(reader, &action_count)) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                             "ACTIONS section is truncated");
        result->failing_section_id = entry->section_id;
        return false;
    }

    if (action_count > ACX_PROFILE_MAX_ACTIONS) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "ACTIONS count exceeds runtime capacity");
        result->failing_section_id = entry->section_id;
        result->failing_record_count = action_count;
        return false;
    }

    if (entry->record_count != action_count) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                             "ACTIONS record count does not match action count");
        result->failing_section_id = entry->section_id;
        result->failing_record_count = entry->record_count;
        return false;
    }

    seen_names = (char *)calloc(ACX_PROFILE_MAX_ACTIONS, ACX_PROFILE_MAX_ACTION_NAME_LEN + 1U);
    if (seen_names == NULL) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                             "unable to reserve duplicate action tracking");
        result->failing_section_id = entry->section_id;
        return false;
    }

    for (uint16_t action_index = 0; action_index < action_count; ++action_index) {
        uint32_t action_id = 0;
        uint16_t step_count = 0;
        uint8_t enabled = 0;
        uint8_t reserved = 0;
        uint16_t name_len = 0;
        uint16_t category_len = 0;
        uint32_t reserved2 = 0;
        char action_name[ACX_PROFILE_MAX_ACTION_NAME_LEN + 1];
        bool action_enabled = false;
        HeadRuntimeAction *runtime_action = NULL;
        uint16_t runtime_commands_for_action = 0;
        int current_bus = APP_CMD_CAN_BUS_NONE;

        memset(action_name, 0, sizeof(action_name));

        if (!acx_section_reader_read_u32(reader, &action_id)
            || !acx_section_reader_read_u16(reader, &step_count)
            || !acx_section_reader_read_u8(reader, &enabled)
            || !acx_section_reader_read_u8(reader, &reserved)
            || !acx_section_reader_read_u16(reader, &name_len)
            || !acx_section_reader_read_u16(reader, &category_len)
            || !acx_section_reader_read_u32(reader, &reserved2)) {
            free(seen_names);
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                 "ACTION record is truncated");
            result->failing_section_id = entry->section_id;
            return false;
        }

        (void)action_id;

        if (reserved != 0 || reserved2 != 0 || enabled > 1) {
            free(seen_names);
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO,
                                 "ACTION record contains invalid reserved fields");
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (name_len == 0 || name_len > ACX_PROFILE_MAX_ACTION_NAME_LEN) {
            free(seen_names);
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                 "ACTION name does not fit runtime buffer");
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (!acx_section_reader_read_string_small(reader,
                                                  name_len,
                                                  action_name,
                                                  sizeof(action_name),
                                                  true,
                                                  result)) {
            free(seen_names);
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (category_len > 0
            && !acx_section_reader_read_validated_utf8(reader,
                                                       category_len,
                                                       NULL,
                                                       0,
                                                       false,
                                                       result)) {
            free(seen_names);
            result->failing_section_id = entry->section_id;
            return false;
        }

        for (uint16_t seen_index = 0; seen_index < action_index; ++seen_index) {
            if (strcasecmp(seen_names + ((size_t)seen_index * (ACX_PROFILE_MAX_ACTION_NAME_LEN + 1U)), action_name) == 0) {
                free(seen_names);
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_DUPLICATE_SECTION,
                                     "ACTION names must be unique");
                result->failing_section_id = entry->section_id;
                return false;
            }
        }

        memcpy(seen_names + ((size_t)action_index * (ACX_PROFILE_MAX_ACTION_NAME_LEN + 1U)),
               action_name,
               strlen(action_name) + 1U);

        action_enabled = enabled != 0;
        if (action_enabled) {

            if (plan->enabled_actions >= ACX_PROFILE_MAX_ACTIONS) {
                free(seen_names);
                acx_result_set_error(result,
                                     ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                     "enabled action count exceeds runtime capacity");
                result->failing_section_id = entry->section_id;
                return false;
            }

            if (materialize && candidate != NULL) {
                runtime_action = &candidate->actions[candidate->action_count];
                memset(runtime_action, 0, sizeof(*runtime_action));
                strlcpy(runtime_action->name, action_name, sizeof(runtime_action->name));
                runtime_action->first_command = (uint16_t)candidate->command_count;
                runtime_action->begin_line = (int)action_index + 1;
                runtime_action->has_value = false;
                runtime_action->value = 0;
            }

        }
            for (uint16_t step_index = 0; step_index < step_count; ++step_index) {
                uint16_t step_order = 0;
                uint8_t step_kind = 0;
                uint8_t bus = 0;
                uint8_t dlc = 0;
                uint8_t reserved_step = 0;
                uint32_t can_id = 0;
                uint32_t wait_ms = 0;
                uint16_t reserved2_step = 0;
                uint8_t data[ACX_PROFILE_MAX_CAN_DLC];
                char text[ACX_PROFILE_MAX_LINE_LEN + 1];

                memset(data, 0, sizeof(data));
                memset(text, 0, sizeof(text));

                if (!acx_section_reader_read_u16(reader, &step_order)
                    || !acx_section_reader_read_u8(reader, &step_kind)
                    || !acx_section_reader_read_u8(reader, &bus)
                    || !acx_section_reader_read_u8(reader, &dlc)
                    || !acx_section_reader_read_u8(reader, &reserved_step)
                    || !acx_section_reader_read_u32(reader, &can_id)
                    || !acx_section_reader_read_u32(reader, &wait_ms)
                    || !acx_section_reader_read_u16(reader, &reserved2_step)) {
                    free(seen_names);
                    acx_result_set_error(result,
                                         ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                         "ACTION step is truncated");
                    result->failing_section_id = entry->section_id;
                    return false;
                }

                if (step_order != step_index || reserved_step != 0 || reserved2_step != 0) {
                    free(seen_names);
                    acx_result_set_error(result,
                                         ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                         "ACTION step order or reserved fields are invalid");
                    result->failing_section_id = entry->section_id;
                    return false;
                }

                if (step_kind < 1 || step_kind > 3 || !acx_validate_bus(bus) || !acx_validate_dlc(dlc) || !acx_validate_can_id(can_id) || !acx_validate_wait(wait_ms)) {
                    free(seen_names);
                    acx_result_set_error(result,
                                         ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                         "ACTION step has invalid timing or CAN fields");
                    result->failing_section_id = entry->section_id;
                    return false;
                }

                if (step_kind == 1) {
                    char bus_text[8];

                    if (bus == 0 || wait_ms != 0) {
                        free(seen_names);
                        acx_result_set_error(result,
                                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                             "ACTION CAN step is inconsistent");
                        result->failing_section_id = entry->section_id;
                        return false;
                    }

                    if (dlc > 0 && !acx_section_reader_read(reader, data, dlc)) {
                        free(seen_names);
                        acx_result_set_error(result,
                                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL,
                                             "ACTION CAN payload is truncated");
                        result->failing_section_id = entry->section_id;
                        return false;
                    }

                    if (action_enabled) {
                        if (current_bus != bus) {
                            if (!acx_format_can_select_text(bus, bus_text, sizeof(bus_text))) {
                                free(seen_names);
                                acx_result_set_error(result,
                                                     ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                                     "unable to format CAN bus selection step");
                                result->failing_section_id = entry->section_id;
                                return false;
                            }

                            if (!acx_account_runtime_command(plan,
                                                             candidate,
                                                             materialize,
                                                             APP_PROFILE_RUNTIME_COMMAND_CAN_SELECT,
                                                             bus_text,
                                                             (int)step_order + 1,
                                                             bus,
                                                             0,
                                                             NULL,
                                                             0,
                                                             0,
                                                             false,
                                                             result)) {
                                free(seen_names);
                                result->failing_section_id = entry->section_id;
                                return false;
                            }

                            ++runtime_commands_for_action;
                            current_bus = bus;
                        }

                        if (!acx_format_hex_bytes(data, dlc, text, sizeof(text), can_id)) {
                            free(seen_names);
                            acx_result_set_error(result,
                                                 ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                                 "unable to format CAN step");
                            result->failing_section_id = entry->section_id;
                            return false;
                        }

                        if (!acx_account_runtime_command(plan,
                                                         candidate,
                                                         materialize,
                                                         APP_PROFILE_RUNTIME_COMMAND_SEND,
                                                         text,
                                                         (int)step_order + 1,
                                                         bus,
                                                         can_id,
                                                         data,
                                                         dlc,
                                                         0,
                                                         false,
                                                         result)) {
                            free(seen_names);
                            result->failing_section_id = entry->section_id;
                            return false;
                        }

                        ++runtime_commands_for_action;
                    }
                } else if (step_kind == 2) {
                    if (bus != 0 || can_id != 0 || dlc != 0) {
                        free(seen_names);
                        acx_result_set_error(result,
                                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                             "ACTION WAIT step is inconsistent");
                        result->failing_section_id = entry->section_id;
                        return false;
                    }

                    if (!acx_format_wait_text(wait_ms, text, sizeof(text))) {
                        free(seen_names);
                        acx_result_set_error(result,
                                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                             "unable to format WAIT step");
                        result->failing_section_id = entry->section_id;
                        return false;
                    }

                    if (action_enabled) {
                        if (!acx_account_runtime_command(plan,
                                                         candidate,
                                                         materialize,
                                                         APP_PROFILE_RUNTIME_COMMAND_WAIT,
                                                         text,
                                                         (int)step_order + 1,
                                                         APP_CMD_CAN_BUS_NONE,
                                                         0,
                                                         NULL,
                                                         0,
                                                         (int)wait_ms,
                                                         false,
                                                         result)) {
                            free(seen_names);
                            result->failing_section_id = entry->section_id;
                            return false;
                        }
                        ++runtime_commands_for_action;
                    }
                } else {
                    if (bus != 0 || can_id != 0 || dlc != 0 || wait_ms != 0) {
                        free(seen_names);
                        acx_result_set_error(result,
                                             ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                             "ACTION STATUS step is inconsistent");
                        result->failing_section_id = entry->section_id;
                        return false;
                    }

                    if (!acx_format_status_text(text, sizeof(text))) {
                        free(seen_names);
                        acx_result_set_error(result,
                                             ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED,
                                             "unable to format STATUS step");
                        result->failing_section_id = entry->section_id;
                        return false;
                    }

                    if (action_enabled) {
                        if (!acx_account_runtime_command(plan,
                                                         candidate,
                                                         materialize,
                                                         APP_PROFILE_RUNTIME_COMMAND_STATUS,
                                                         text,
                                                         (int)step_order + 1,
                                                         APP_CMD_CAN_BUS_NONE,
                                                         0,
                                                         NULL,
                                                         0,
                                                         0,
                                                         false,
                                                         result)) {
                            free(seen_names);
                            result->failing_section_id = entry->section_id;
                            return false;
                        }
                        ++runtime_commands_for_action;
                    }
                }
            }

        if (action_enabled) {
            plan->enabled_actions++;
            plan->materialized_actions++;

            if (materialize && candidate != NULL) {
                runtime_action->command_count = runtime_commands_for_action;
                candidate->action_count++;
            }
        }
    }

    free(seen_names);
    result->total_actions = action_count;
    result->enabled_actions = (uint16_t)plan->enabled_actions;
    result->materialized_actions = (uint16_t)plan->materialized_actions;
    result->materialized_commands = plan->materialized_commands;
    result->command_text_bytes = plan->command_text_bytes;
    return true;
}


static bool acx_parse_sections(acx_input_t *input,
                               const acx_directory_entry_t entries[ACX_PROFILE_MAX_SECTION_COUNT],
                               acx_plan_t *plan,
                               HeadRuntimeProfile *candidate,
                               bool materialize,
                               AcxProfileLoadResult *result,
                               uint32_t *payload_crc32)
{
    size_t order[ACX_PROFILE_MAX_SECTION_COUNT];

    acx_sort_indices_by_offset(entries, order);

    for (size_t i = 0; i < ACX_PROFILE_MAX_SECTION_COUNT; ++i) {
        const acx_directory_entry_t *entry = &entries[order[i]];
        acx_section_reader_t reader = {};

        if (!acx_section_reader_begin(&reader, input, entry->offset, entry->size, payload_crc32)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_OFFSET,
                                 "unable to seek to section %s",
                                 acx_section_name(entry->section_id));
            result->failing_section_id = entry->section_id;
            result->failing_offset = entry->offset;
            result->failing_size = entry->size;
            return false;
        }

        switch (entry->section_id) {
        case ACX_PROFILE_SECTION_METADATA:
            if (!acx_parse_metadata_section(&reader, entry, plan, candidate, materialize, result)) {
                return false;
            }
            break;
        case ACX_PROFILE_SECTION_INIT:
            if (!acx_parse_init_section(&reader, entry, plan, candidate, materialize, result)) {
                return false;
            }
            break;
        case ACX_PROFILE_SECTION_TESTEO:
            if (!acx_parse_testeo_section(&reader, entry, candidate, materialize, result)) {
                return false;
            }
            break;
        case ACX_PROFILE_SECTION_MOTION:
            if (!acx_parse_motion_section(&reader, entry, plan, candidate, materialize, result)) {
                return false;
            }
            break;
        case ACX_PROFILE_SECTION_J:
            if (!acx_parse_j_section(&reader, entry, candidate, materialize, result)) {
                return false;
            }
            break;
        case ACX_PROFILE_SECTION_CASCADE:
            if (!acx_parse_cascade_section(&reader, entry, candidate, materialize, result)) {
                return false;
            }
            break;
        case ACX_PROFILE_SECTION_STOP:
            if (!acx_parse_stop_section(&reader, entry, candidate, materialize, result)) {
                return false;
            }
            break;
        case ACX_PROFILE_SECTION_ACTIONS:
            if (!acx_parse_actions_section(&reader, entry, plan, candidate, materialize, result)) {
                return false;
            }
            break;
        default:
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_DIRECTORY,
                                 "unsupported ACX section id %u",
                                 (unsigned)entry->section_id);
            result->failing_section_id = entry->section_id;
            return false;
        }

        if (reader.remaining != 0) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_RECORD,
                                 "section %s has trailing bytes",
                                 acx_section_name(entry->section_id));
            result->failing_section_id = entry->section_id;
            result->failing_offset = entry->offset;
            result->failing_size = entry->size;
            return false;
        }

        if (reader.section_crc32 != entry->crc32) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_SECTION_CRC_MISMATCH,
                                 "section %s CRC mismatch",
                                 acx_section_name(entry->section_id));
            result->failing_section_id = entry->section_id;
            result->failing_offset = entry->offset;
            result->failing_size = entry->size;
            return false;
        }
    }

    return true;
}

static bool acx_load_candidate_from_input(acx_input_t *input,
                                          const char *logical_name,
                                          HeadRuntimeProfile *candidate,
                                          AcxProfileLoadResult *result)
{
    acx_directory_entry_t entries[ACX_PROFILE_MAX_SECTION_COUNT];
    uint8_t seen[ACX_PROFILE_MAX_SECTION_COUNT + 1];
    acx_plan_t plan = {};
    acx_plan_t materialize_plan = {};
    uint32_t file_size = 0;
    uint32_t section_directory_offset = 0;
    uint32_t section_directory_size = 0;
    uint32_t payload_offset = 0;
    uint32_t payload_size = 0;
    uint32_t declared_payload_crc32 = 0;
    uint16_t section_count = 0;
    uint32_t computed_payload_crc32 = 0;
    uint32_t payload_crc32 = 0;
    bool ok = false;

    if (result == NULL) {
        return false;
    }

    acx_result_reset(result);
    memset(entries, 0, sizeof(entries));
    memset(seen, 0, sizeof(seen));

    if (candidate == NULL) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_ARGUMENT,
                             "candidate cannot be NULL");
        return false;
    }

    if (!acx_read_header(input,
                         result,
                         &file_size,
                         &section_directory_offset,
                         &section_directory_size,
                         &payload_offset,
                         &payload_size,
                         &declared_payload_crc32,
                         &section_count,
                         &plan)) {
        return false;
    }

    result->file_size = file_size;
    result->header_size = ACX_PROFILE_HEADER_SIZE;
    result->payload_offset = payload_offset;
    result->payload_size = payload_size;
    result->section_directory_size = section_directory_size;
    result->section_count = section_count;
    result->declared_payload_crc32 = declared_payload_crc32;
    result->temp_buffer_max = ACX_PROFILE_TEMP_BUFFER_MAX_BYTES;
    strlcpy(result->file_name, logical_name != NULL ? logical_name : "<memory>", sizeof(result->file_name));

    payload_crc32 = 0;
    if (!acx_read_directory(input, &plan, entries, seen, result, &payload_crc32)) {
        return false;
    }

    if (!acx_parse_sections(input, entries, &plan, NULL, false, result, &payload_crc32)) {
        return false;
    }

    computed_payload_crc32 = payload_crc32;
    result->computed_payload_crc32 = computed_payload_crc32;

    if (computed_payload_crc32 != declared_payload_crc32) {
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_PAYLOAD_CRC_MISMATCH,
                             "payload CRC mismatch");
        return false;
    }

    materialize_plan = plan;
    materialize_plan.enabled_actions = 0;
    materialize_plan.materialized_actions = 0;
    materialize_plan.materialized_commands = 0;
    materialize_plan.command_text_bytes = 0;
    materialize_plan.init_text_bytes = 0;
    materialize_plan.temp_buffer_max = ACX_PROFILE_WORK_BUFFER_BYTES;

    if (candidate != NULL) {
        app_head_program_id_t program_id;

        if (!acx_map_program_number(plan.program_number, &program_id)) {
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_COUNT,
                                 "program number %" PRId32 " is out of range",
                                 plan.program_number);
            return false;
        }

        memset(candidate, 0, sizeof(*candidate));
        candidate->origin = APP_PROFILE_ORIGIN_FILE;
        candidate->generation = 0;
        candidate->source_size = file_size;
        candidate->program_number = (uint16_t)plan.program_number;
        candidate->version = (uint32_t)plan.version_number;
        candidate->has_profile_name = true;
        candidate->has_declared_program = true;
        candidate->has_version = true;
        candidate->has_declared_crc = true;
        candidate->has_declared_system = true;
        candidate->crc_ok = true;
        candidate->declared_crc32 = declared_payload_crc32;
        candidate->computed_crc32 = computed_payload_crc32;
        strlcpy(candidate->filename, result->file_name, sizeof(candidate->filename));
        strlcpy(candidate->system, "ACX", sizeof(candidate->system));
        candidate->command_profile.program_id = program_id;
        acx_finalize_candidate_view(candidate);
    }

    acx_input_seek(input, ACX_PROFILE_HEADER_SIZE + section_directory_size);

    ok = acx_parse_sections(input, entries, &materialize_plan, candidate, true, result, NULL);
    if (!ok) {
        return false;
    }

    result->materialized_actions = candidate != NULL ? (uint16_t)candidate->action_count : (uint16_t)materialize_plan.materialized_actions;
    result->materialized_commands = candidate != NULL ? (uint32_t)candidate->command_count : materialize_plan.materialized_commands;
    result->command_text_bytes = candidate != NULL ? (uint32_t)candidate->command_text_used : materialize_plan.command_text_bytes;
    result->init_text_bytes = candidate != NULL ? (uint32_t)candidate->init_text_used : materialize_plan.init_text_bytes;
    result->declared_payload_crc32 = declared_payload_crc32;
    result->computed_payload_crc32 = computed_payload_crc32;
    result->error = ACX_PROFILE_LOAD_OK;
    strlcpy(result->message, "OK", sizeof(result->message));
    return true;
}


}  // namespace

extern "C" const char *app_acx_profile_load_error_text(AcxProfileLoadError error)
{
    switch (error) {
    case ACX_PROFILE_LOAD_OK:
        return "OK";
    case ACX_PROFILE_LOAD_ERROR_INVALID_ARGUMENT:
        return "INVALID_ARGUMENT";
    case ACX_PROFILE_LOAD_ERROR_INVALID_PATH:
        return "INVALID_PATH";
    case ACX_PROFILE_LOAD_ERROR_FILE_NOT_FOUND:
        return "FILE_NOT_FOUND";
    case ACX_PROFILE_LOAD_ERROR_FILE_TOO_SMALL:
        return "FILE_TOO_SMALL";
    case ACX_PROFILE_LOAD_ERROR_FILE_TOO_LARGE:
        return "FILE_TOO_LARGE";
    case ACX_PROFILE_LOAD_ERROR_INVALID_MAGIC:
        return "INVALID_MAGIC";
    case ACX_PROFILE_LOAD_ERROR_UNSUPPORTED_VERSION:
        return "UNSUPPORTED_VERSION";
    case ACX_PROFILE_LOAD_ERROR_INVALID_HEADER:
        return "INVALID_HEADER";
    case ACX_PROFILE_LOAD_ERROR_INVALID_DIRECTORY:
        return "INVALID_DIRECTORY";
    case ACX_PROFILE_LOAD_ERROR_UNSUPPORTED_SECTION_VERSION:
        return "UNSUPPORTED_SECTION_VERSION";
    case ACX_PROFILE_LOAD_ERROR_DUPLICATE_SECTION:
        return "DUPLICATE_SECTION";
    case ACX_PROFILE_LOAD_ERROR_MISSING_SECTION:
        return "MISSING_SECTION";
    case ACX_PROFILE_LOAD_ERROR_INVALID_OFFSET:
        return "INVALID_OFFSET";
    case ACX_PROFILE_LOAD_ERROR_OVERLAPPING_SECTIONS:
        return "OVERLAPPING_SECTIONS";
    case ACX_PROFILE_LOAD_ERROR_INVALID_RECORD:
        return "INVALID_RECORD";
    case ACX_PROFILE_LOAD_ERROR_INVALID_COUNT:
        return "INVALID_COUNT";
    case ACX_PROFILE_LOAD_ERROR_INVALID_STRING:
        return "INVALID_STRING";
    case ACX_PROFILE_LOAD_ERROR_RESERVED_NONZERO:
        return "RESERVED_NONZERO";
    case ACX_PROFILE_LOAD_ERROR_PAYLOAD_CRC_MISMATCH:
        return "PAYLOAD_CRC_MISMATCH";
    case ACX_PROFILE_LOAD_ERROR_SECTION_CRC_MISMATCH:
        return "SECTION_CRC_MISMATCH";
    case ACX_PROFILE_LOAD_ERROR_RUNTIME_CAPACITY_EXCEEDED:
        return "RUNTIME_CAPACITY_EXCEEDED";
    case ACX_PROFILE_LOAD_ERROR_IO_ERROR:
        return "IO_ERROR";
    case ACX_PROFILE_LOAD_ERROR_INVALID_ENDIANNESS:
        return "INVALID_ENDIANNESS";
    default:
        return "UNKNOWN";
    }
}

extern "C" bool app_acx_profile_load_candidate_from_bytes(const uint8_t *bytes,
                                                           size_t size,
                                                           const char *logical_name,
                                                           HeadRuntimeProfile *candidate,
                                                           AcxProfileLoadResult *result)
{
    acx_input_t input = {};
    char file_name[ACX_PROFILE_MAX_FILENAME_LEN + 1];

    if (candidate == NULL) {
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_ARGUMENT,
                             "candidate cannot be NULL");
        return false;
    }

    if (bytes == NULL || size == 0) {
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_ARGUMENT,
                             "memory source is empty");
        return false;
    }

    if (size > ACX_PROFILE_TRANSPORT_MAX_BYTES) {
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_LARGE,
                             "ACX payload exceeds the 64 KiB limit");
        return false;
    }

    if (logical_name != NULL && logical_name[0] != '\0') {
        if (strchr(logical_name, '/') != NULL || strchr(logical_name, '\\') != NULL) {
            acx_result_reset(result);
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_PATH,
                                 "logical name must be a basename");
            return false;
        }
        if (!acx_is_valid_acx_basename(logical_name)) {
            acx_result_reset(result);
            acx_result_set_error(result,
                                 ACX_PROFILE_LOAD_ERROR_INVALID_PATH,
                                 "logical name is not a valid .acx file name");
            return false;
        }
        strlcpy(file_name, logical_name, sizeof(file_name));
    } else {
        strlcpy(file_name, "memory.acx", sizeof(file_name));
    }

    input.kind = ACX_INPUT_MEMORY;
    input.bytes = bytes;
    input.size = size;
    input.position = 0;

    return acx_load_candidate_from_input(&input, file_name, candidate, result);
}

extern "C" bool app_acx_profile_load_candidate(const char *file_path,
                                                HeadRuntimeProfile *candidate,
                                                AcxProfileLoadResult *result)
{
    acx_input_t input = {};
    char file_name[ACX_PROFILE_MAX_FILENAME_LEN + 1];
    FILE *file = NULL;
    long file_size = 0;
    bool ok = false;

    if (candidate == NULL) {
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_ARGUMENT,
                             "candidate cannot be NULL");
        return false;
    }

    if (!acx_extract_basename(file_path, file_name, sizeof(file_name))) {
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_INVALID_PATH,
                             "file path must be an absolute .acx path inside /fs");
        return false;
    }

    file = fopen(file_path, "rb");
    if (file == NULL) {
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_NOT_FOUND,
                             "file not found");
        strlcpy(result->file_name, file_name, sizeof(result->file_name));
        return false;
    }

    if (fseek(file, 0, SEEK_END) != 0) {
        fclose(file);
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_IO_ERROR,
                             "unable to determine ACX file size");
        strlcpy(result->file_name, file_name, sizeof(result->file_name));
        return false;
    }

    file_size = ftell(file);
    if (file_size < 0) {
        fclose(file);
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_IO_ERROR,
                             "unable to determine ACX file size");
        strlcpy(result->file_name, file_name, sizeof(result->file_name));
        return false;
    }

    if (file_size > (long)ACX_PROFILE_TRANSPORT_MAX_BYTES) {
        fclose(file);
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_FILE_TOO_LARGE,
                             "ACX file exceeds the 64 KiB limit");
        strlcpy(result->file_name, file_name, sizeof(result->file_name));
        return false;
    }

    if (fseek(file, 0, SEEK_SET) != 0) {
        fclose(file);
        acx_result_reset(result);
        acx_result_set_error(result,
                             ACX_PROFILE_LOAD_ERROR_IO_ERROR,
                             "unable to rewind ACX file");
        strlcpy(result->file_name, file_name, sizeof(result->file_name));
        return false;
    }

    input.kind = ACX_INPUT_FILE;
    input.file = file;
    input.size = (size_t)file_size;

    acx_result_reset(result);
    strlcpy(result->file_name, file_name, sizeof(result->file_name));

    ok = acx_load_candidate_from_input(&input, file_name, candidate, result);
    fclose(file);
    return ok;
}
