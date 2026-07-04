#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "esp_err.h"

#include "head_command_profile.h"

#define APP_PROFILE_FS_BASE "/fs"
#define APP_PROFILE_SELECTED_PATH APP_PROFILE_FS_BASE "/.selected"

#define APP_PROFILE_MAX_FILENAME_LEN 48
#define APP_PROFILE_MAX_PROFILE_NAME_LEN 64
#define APP_PROFILE_MAX_SYSTEM_LEN 8
#define APP_PROFILE_MAX_INIT_SCRIPT_LEN 48
#define APP_PROFILE_MAX_ACTION_NAME_LEN 48
#define APP_PROFILE_MAX_LINE_LEN 224
#define APP_PROFILE_MAX_ACTIONS 128
#define APP_PROFILE_MAX_COMMANDS 768
#define APP_PROFILE_COMMAND_TEXT_BYTES 32768
#define APP_PROFILE_MAX_WAIT_MS 10000

#define APP_PROFILE_MAX_INIT_STEPS 192
#define APP_PROFILE_INIT_TEXT_BYTES 12288
#define APP_PROFILE_MAX_MOTION_SEQUENCE 16
#define APP_PROFILE_MAX_POSITIONS 16
#define APP_PROFILE_MAX_YARN_ADDRESSES 16
#define APP_PROFILE_MAX_STITCH_ADDRESSES 32

typedef enum {
    APP_PROFILE_ORIGIN_COMPILED_FALLBACK = 0,
    APP_PROFILE_ORIGIN_FILE = 1,
} app_profile_origin_t;

typedef enum {
    APP_PROFILE_RUNTIME_COMMAND_WAIT = 0,
    APP_PROFILE_RUNTIME_COMMAND_CAN_SELECT,
    APP_PROFILE_RUNTIME_COMMAND_STATUS,
    APP_PROFILE_RUNTIME_COMMAND_SEND,
    APP_PROFILE_RUNTIME_COMMAND_SEND_DYNAMIC,
} app_profile_runtime_command_type_t;

typedef struct {
    app_profile_runtime_command_type_t type;
    uint16_t text_offset;
    uint16_t text_len;
    int line_number;
    int wait_ms;
    int can_bus;
    uint32_t can_id;
    uint8_t data[APP_HEAD_PROFILE_MAX_DLC];
    uint8_t dlc;
    bool uses_dynamic_j_placeholder;
} HeadRuntimeCommand;

typedef struct {
    char name[APP_PROFILE_MAX_ACTION_NAME_LEN + 1];
    uint16_t first_command;
    uint16_t command_count;
    int begin_line;
    bool has_value;
    int value;
} HeadRuntimeAction;

typedef struct {
    int den_count;
    int sic_count;
    int j_count;
    int yarn_count;
    int stitch_count;
    int feet_count;
    bool has_explicit_configuration;
} HeadRuntimeModuleCounts;

typedef struct {
    app_profile_origin_t origin;
    uint32_t generation;
    char filename[APP_PROFILE_MAX_FILENAME_LEN + 1];
    char profile_name[APP_PROFILE_MAX_PROFILE_NAME_LEN + 1];
    char system[APP_PROFILE_MAX_SYSTEM_LEN + 1];
    char init_script[APP_PROFILE_MAX_INIT_SCRIPT_LEN + 1];
    uint16_t program_number;
    bool has_profile_name;
    bool has_declared_system;
    bool has_declared_program;
    bool has_version;
    uint32_t version;
    bool has_declared_crc;
    uint32_t declared_crc32;
    uint32_t computed_crc32;
    bool crc_ok;
    size_t source_size;

    HeadRuntimeModuleCounts modules;

    HeadCommandProfile command_profile;
    const char *init_phase1_ptrs[APP_PROFILE_MAX_INIT_STEPS];
    const char *init_phase2_ptrs[APP_PROFILE_MAX_INIT_STEPS];
    char init_text[APP_PROFILE_INIT_TEXT_BYTES];
    size_t init_text_used;

    uint8_t den_run_sequence[APP_PROFILE_MAX_MOTION_SEQUENCE];
    uint8_t den_alternate_run_sequence[APP_PROFILE_MAX_MOTION_SEQUENCE];
    uint8_t sic_run_sequence[APP_PROFILE_MAX_MOTION_SEQUENCE];
    uint8_t feet_run_sequence[APP_PROFILE_MAX_MOTION_SEQUENCE];
    uint16_t den_positions[APP_PROFILE_MAX_POSITIONS];
    uint16_t sic_positions[APP_PROFILE_MAX_POSITIONS];
    uint16_t feet_positions[APP_PROFILE_MAX_POSITIONS];
    uint8_t yarn_addresses[APP_PROFILE_MAX_YARN_ADDRESSES];
    uint8_t stitch_addresses[APP_PROFILE_MAX_STITCH_ADDRESSES];

    HeadRuntimeAction actions[APP_PROFILE_MAX_ACTIONS];
    size_t action_count;
    HeadRuntimeCommand commands[APP_PROFILE_MAX_COMMANDS];
    size_t command_count;
    char command_text[APP_PROFILE_COMMAND_TEXT_BYTES];
    size_t command_text_used;
} HeadRuntimeProfile;

bool app_profile_load_from_file(const char *name, HeadRuntimeProfile *out);
bool app_profile_validate(const HeadRuntimeProfile *profile);
esp_err_t app_profile_apply(const HeadRuntimeProfile *profile);
esp_err_t app_profile_select(const char *name);
const HeadRuntimeProfile *app_profile_get_active(void);
const char *app_profile_get_active_filename(void);
esp_err_t app_profile_load_selected(void);
esp_err_t app_profile_store_init(void);

esp_err_t app_profile_apply_compiled(app_head_program_id_t program_id);
const HeadCommandProfile *app_profile_get_active_command_profile(void);
app_profile_origin_t app_profile_get_active_origin(void);
uint32_t app_profile_get_active_generation(void);
bool app_profile_is_valid_filename(const char *name);
bool app_profile_is_active_filename(const char *name);
bool app_profile_is_loading_filename(const char *name);
bool app_profile_is_loading(void);
const HeadRuntimeAction *app_profile_find_action(const HeadRuntimeProfile *profile,
                                                 const char *action_name);
const char *app_profile_command_text(const HeadRuntimeProfile *profile,
                                     const HeadRuntimeCommand *command);
HeadRuntimeProfile *app_profile_get_inactive_scratch(void);
