#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define APP_HEAD_PROFILE_MAX_DLC 8

typedef enum {
    APP_HEAD_PROGRAM_1 = 1,
    APP_HEAD_PROGRAM_2 = 2,
    APP_HEAD_PROGRAM_3 = 3,
} app_head_program_id_t;

typedef struct {
    uint32_t can_id;
    uint8_t data[APP_HEAD_PROFILE_MAX_DLC];
    size_t dlc;
} HeadCanCommand;

struct HeadMotionCommandProfile;
struct HeadJCommandProfile;
struct HeadCascadeCommandProfile;

typedef bool (*app_head_motion_frame_builder_fn_t)(
    const struct HeadMotionCommandProfile *commands,
    size_t instance_index,
    uint16_t position,
    HeadCanCommand *frame);
typedef bool (*app_head_j_frame_builder_fn_t)(
    const struct HeadJCommandProfile *commands,
    size_t instance_index,
    uint8_t physical_register,
    HeadCanCommand *frame);
typedef bool (*app_head_cascade_frame_builder_fn_t)(
    const struct HeadCascadeCommandProfile *commands,
    size_t instance_index,
    size_t channel_index,
    bool on,
    HeadCanCommand *frame);

typedef struct HeadInitCommandSequence {
    const char *const *phase1_steps;
    size_t phase1_step_count;
    uint32_t phase1_step_delay_ms;
    uint32_t phase_gap_ms;
    const char *const *phase2_steps;
    size_t phase2_step_count;
    uint32_t phase2_step_delay_ms;
    bool skip_step_delay_after_wait = false;
} HeadInitCommandSequence;

typedef struct {
    HeadCanCommand ping;
    uint32_t response_can_id;
    uint32_t reset_can_id;
    uint8_t reset_data[APP_HEAD_PROFILE_MAX_DLC];
    size_t reset_dlc;
    uint8_t success_code;
    uint8_t missing_expansion_code;
    uint8_t missing_force_code;
    uint8_t force_board_1_code;
    uint8_t force_board_2_code;
    uint16_t max_tries;
    uint32_t response_timeout_ms;
    uint32_t retry_delay_ms;
    uint32_t reset_debounce_ms;
} HeadTesteoCommandProfile;

typedef struct HeadMotionCommandProfile {
    uint32_t can_id;
    uint8_t opcode;
    uint8_t motor_index_base;
    size_t instance_count;
    const uint8_t *run_sequence;
    size_t run_sequence_count;
    const uint8_t *alternate_run_sequence;
    size_t alternate_run_sequence_count;
    const uint16_t *positions;
    size_t position_count;
    uint32_t run_period_ms;
    uint32_t alternate_run_period_ms;
    const uint32_t *instance_can_ids = nullptr;
    const uint8_t *instance_selectors = nullptr;
    uint32_t active_instance_mask = 0U;
    app_head_motion_frame_builder_fn_t build_frame = nullptr;
} HeadMotionCommandProfile;

typedef struct HeadJCommandProfile {
    uint32_t can_id;
    uint8_t opcode;
    uint8_t instance_index_base;
    size_t instance_count;
    uint8_t channel_count;
    uint8_t initial_register;
    uint8_t on_all_register;
    uint8_t off_all_register;
    uint32_t run_period_ms;
    const uint32_t *instance_can_ids = nullptr;
    const uint8_t *instance_selectors = nullptr;
    uint32_t active_instance_mask = 0U;
    app_head_j_frame_builder_fn_t build_frame = nullptr;
} HeadJCommandProfile;

typedef struct HeadCascadeCommandProfile {
    uint32_t can_id;
    uint8_t opcode;
    const uint8_t *addresses;
    size_t addresses_per_instance;
    size_t instance_count;
    uint8_t on_value;
    uint8_t off_value;
    uint32_t run_period_ms;
    const uint32_t *instance_can_ids = nullptr;
    const uint8_t *instance_selectors = nullptr;
    uint32_t active_instance_mask = 0U;
    app_head_cascade_frame_builder_fn_t build_frame = nullptr;
} HeadCascadeCommandProfile;

typedef struct {
    // STOP only cancels local state today; it intentionally transmits no CAN frame.
    bool sends_can_frame;
    HeadCanCommand frame;
} HeadStopCommandProfile;

typedef struct {
    app_head_program_id_t program_id;
    const char *program_name;
    HeadInitCommandSequence init_sequence;
    HeadTesteoCommandProfile testeo;
    HeadMotionCommandProfile den;
    HeadMotionCommandProfile sic;
    HeadMotionCommandProfile feet;
    HeadJCommandProfile j;
    HeadCascadeCommandProfile yarn;
    HeadCascadeCommandProfile stitch;
    HeadStopCommandProfile stop;
} HeadCommandProfile;
