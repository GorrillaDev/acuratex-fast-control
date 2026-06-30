#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define APP_HEAD_PROFILE_MAX_DLC 8

typedef enum {
    APP_HEAD_PROGRAM_1 = 1,
    APP_HEAD_PROGRAM_2 = 2,
} app_head_program_id_t;

typedef struct {
    uint32_t can_id;
    uint8_t data[APP_HEAD_PROFILE_MAX_DLC];
    size_t dlc;
} HeadCanCommand;

typedef struct {
    const char *const *phase1_steps;
    size_t phase1_step_count;
    uint32_t phase1_step_delay_ms;
    uint32_t phase_gap_ms;
    const char *const *phase2_steps;
    size_t phase2_step_count;
    uint32_t phase2_step_delay_ms;
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

typedef struct {
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
} HeadMotionCommandProfile;

typedef struct {
    uint32_t can_id;
    uint8_t opcode;
    uint8_t instance_index_base;
    size_t instance_count;
    uint8_t channel_count;
    uint8_t initial_register;
    uint8_t on_all_register;
    uint8_t off_all_register;
    uint32_t run_period_ms;
} HeadJCommandProfile;

typedef struct {
    uint32_t can_id;
    uint8_t opcode;
    const uint8_t *addresses;
    size_t addresses_per_instance;
    size_t instance_count;
    uint8_t on_value;
    uint8_t off_value;
    uint32_t run_period_ms;
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
