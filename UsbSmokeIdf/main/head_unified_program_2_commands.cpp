#include "head_unified_program_2_commands.h"

#include <string.h>

// Fuente funcional: referencias/programa2_final/firmware_programa2_final.ino.
// Solo se conservan datos CAN y tiempos; no se migra Arduino, Wi-Fi ni web.
static const char *const kInitPhase1[] = {
    "363 08 02 00 00 00 00", "WAIT 0", "363 04 0a 10 00 00 00", "WAIT 0", "363 08 14 00", "WAIT 364",
    "363 08 14 00", "WAIT 40", "363 08 01", "WAIT 0", "363 08 14 00", "WAIT 2027",
    "361 07 01", "WAIT 1685", "363 08 14 00", "WAIT 33", "363 08 01", "WAIT 0",
    "363 08 14 00", "WAIT 4046", "363 08 02 00 00 00 00", "WAIT 1", "330 40 00 00 00 00 00", "WAIT 100",
    "370 fd 06 10 00", "WAIT 100", "370 fd 06 11 00", "WAIT 100", "363 08 02 00 00 00 00", "WAIT 0",
    "363 04 0a 10 00 00 00", "WAIT 0", "363 07 0c 00 00", "WAIT 99", "361 02 04 00 0b 00 00", "WAIT 0",
    "320 6c 50 0b 00", "WAIT 0", "361 04 0f 01 01 e4 0c", "WAIT 0", "361 01 01 ff ff ff ff", "WAIT 0",
    "320 00", "WAIT 100", "361 01 02 ff ff", "WAIT 0", "320 02", "WAIT 100",
    "361 07 01", "WAIT 100", "361 06 0a 07 00 00", "WAIT 0", "361 01 0b 00 00 d0 07", "WAIT 0",
    "361 01 0b 02 00 20 03", "WAIT 0", "361 01 0b 01 00 dc 05", "WAIT 0", "361 01 12 01 00 20 03", "WAIT 0",
    "363 03 06 01 00 06 30", "WAIT 0", "363 03 06 00 00 ff ff", "WAIT 9", "361 01 1a 00 00 01 00", "WAIT 0",
    "361 06 01", "WAIT 0", "361 05 03 01 01 1e 00", "WAIT 0", "363 05 04 01 03 01 00", "WAIT 0",
    "361 03 05 00 00 80 00", "WAIT 0", "361 03 05 01 00 62 00", "WAIT 0", "320 2d 00 d7 ff", "WAIT 0",
    "363 03 06 04 00 00 00", "WAIT 0", "363 04 08 00 00 00 00", "WAIT 0", "361 04 06 00 01 b4 00", "WAIT 0",
    "361 03 06 02 00 30 0f", "WAIT 0", "363 08 01", "WAIT 0", "363 05 04 02 03 01 00", "WAIT 0",
    "361 04 0f 01 01 b8 0b", "WAIT 0", "361 06 08 00 00 04 00 00", "WAIT 0", "361 06 08 01 00 04 00 00", "WAIT 0",
    "361 05 03 00 00 1e 00", "WAIT 0", "361 05 03 00 01 1e 00", "WAIT 0", "361 05 03 01 00 1e 00", "WAIT 0",
    "363 04 08 03 01 01 00", "WAIT 18", "363 08 14 00", "WAIT 8", "363 08 01", "WAIT 0",
    "363 08 14 00", "WAIT 1447", "361 01 01 ff ff ff ff", "WAIT 0", "320 00", "WAIT 0",
    "361 04 07 02 00 00 00", "WAIT 1", "361 07 01", "WAIT 4", "361 04 10 02 02 fd 00", "WAIT 18",
    "363 05 01 00 00 01 00", "WAIT 14640", "361 07 01", "WAIT 0", "363 05 01 00 01 01 00", "WAIT 30",
    "363 05 01 00 02 01 00", "WAIT 30", "363 05 01 00 03 01 00", "WAIT 30", "363 05 01 00 04 01 00", "WAIT 30",
    "363 05 01 00 05 01 00", "WAIT 30", "363 05 01 00 06 01 00", "WAIT 30", "363 05 01 00 07 01 00",
};

static const uint32_t kDenCanIds[8] = { 0x363, 0x363, 0x364, 0x364, 0x363, 0x363, 0x364, 0x364 };
static const uint8_t kDenSelectors[8] = { 0x00, 0x01, 0x00, 0x01, 0x03, 0x02, 0x03, 0x02 };
static const uint16_t kDenPositions[5] = { 0, 162, 325, 487, 650 };
static const uint8_t kDenRunSequence[5] = { 1, 2, 3, 4, 5 };

static const uint32_t kSicCanIds[2] = { 0x363, 0x364 };
static const uint8_t kSicSelectors[2] = { 0x00, 0x01 };
static const uint16_t kSicPositions[13] = {
    180, 360, (uint16_t)-180, (uint16_t)-360,
    180, 360, (uint16_t)-180, (uint16_t)-360,
    180, 360, (uint16_t)-180, (uint16_t)-360,
    0x7FFF,
};
static const uint8_t kSicRunSequence[13] = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

static const uint32_t kFeetCanIds[2] = { 0x363, 0x363 };
static const uint8_t kFeetSelectors[2] = { 0x00, 0x01 };

static const uint32_t kJCanIds[8] = { 0x363, 0x363, 0x364, 0x364, 0x363, 0x363, 0x364, 0x364 };
static const uint8_t kJSelectors[8] = { 0x00, 0x01, 0x00, 0x01, 0x02, 0x03, 0x02, 0x03 };

static const uint8_t kYarnChannels[8] = { 0, 1, 2, 3, 4, 5, 6, 7 };
static const uint32_t kYarnCanIds[1] = { 0x363 };
static const uint8_t kStitchSteps[12] = { 1, 2, 3, 4, 5, 6, 1, 2, 3, 4, 5, 6 };
static const uint32_t kStitchCanIds[2] = { 0x363, 0x363 };
static const uint8_t kStitchSelectors[2] = { 0x00, 0x01 };

static uint32_t app_program2_can_id(uint32_t fallback,
                                    const uint32_t *ids,
                                    size_t instance_index)
{
    return ids != NULL ? ids[instance_index] : fallback;
}

static uint8_t app_program2_selector(uint8_t fallback,
                                     const uint8_t *selectors,
                                     size_t instance_index)
{
    return selectors != NULL ? selectors[instance_index]
                             : (uint8_t)(fallback + instance_index);
}

static bool app_program2_build_den_frame(const HeadMotionCommandProfile *commands,
                                         size_t instance_index,
                                         uint16_t position,
                                         HeadCanCommand *frame)
{
    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_program2_can_id(commands->can_id,
                                        commands->instance_can_ids,
                                        instance_index);
    frame->data[0] = 0x03;
    frame->data[1] = 0x03;
    frame->data[2] = app_program2_selector(commands->motor_index_base,
                                           commands->instance_selectors,
                                           instance_index);
    frame->data[3] = 0x00;
    frame->data[4] = (uint8_t)(position & 0xFFU);
    frame->data[5] = (uint8_t)((position >> 8U) & 0xFFU);
    frame->dlc = 6;
    return true;
}

static bool app_program2_build_sic_frame(const HeadMotionCommandProfile *commands,
                                         size_t instance_index,
                                         uint16_t position,
                                         HeadCanCommand *frame)
{
    const bool sync = position == 0x7FFFU;
    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_program2_can_id(commands->can_id,
                                        commands->instance_can_ids,
                                        instance_index);
    frame->data[0] = 0x04;
    frame->data[1] = 0x01;
    frame->data[2] = 0x00;
    frame->data[3] = app_program2_selector(commands->motor_index_base,
                                           commands->instance_selectors,
                                           instance_index);
    frame->data[4] = sync ? 0x00 : (uint8_t)(position & 0xFFU);
    frame->data[5] = sync ? 0x00 : (uint8_t)((position >> 8U) & 0xFFU);
    frame->data[6] = sync ? 0x10 : 0x00;
    frame->data[7] = 0x00;
    frame->dlc = 8;
    return true;
}

static bool app_program2_build_feet_frame(const HeadMotionCommandProfile *commands,
                                          size_t instance_index,
                                          uint16_t position,
                                          HeadCanCommand *frame)
{
    (void)position;
    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_program2_can_id(commands->can_id,
                                        commands->instance_can_ids,
                                        instance_index);
    frame->data[0] = 0x04;
    frame->data[1] = 0x01;
    frame->data[2] = 0x02;
    frame->data[3] = app_program2_selector(commands->motor_index_base,
                                           commands->instance_selectors,
                                           instance_index);
    frame->data[4] = 0x00;
    frame->data[5] = 0x80;
    frame->dlc = 6;
    return true;
}

static bool app_program2_build_j_frame(const HeadJCommandProfile *commands,
                                       size_t instance_index,
                                       uint8_t physical_register,
                                       HeadCanCommand *frame)
{
    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_program2_can_id(commands->can_id,
                                        commands->instance_can_ids,
                                        instance_index);
    frame->data[0] = 0x06;
    frame->data[1] = 0x03;
    frame->data[2] = app_program2_selector(commands->instance_index_base,
                                           commands->instance_selectors,
                                           instance_index);
    frame->data[3] = 0x00;
    frame->data[4] = physical_register;
    frame->data[5] = 0x00;
    frame->dlc = 6;
    return true;
}

static bool app_program2_build_yarn_frame(const HeadCascadeCommandProfile *commands,
                                          size_t instance_index,
                                          size_t channel_index,
                                          bool on,
                                          HeadCanCommand *frame)
{
    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_program2_can_id(commands->can_id,
                                        commands->instance_can_ids,
                                        instance_index);
    frame->data[0] = 0x05;
    frame->data[1] = 0x01;
    frame->data[2] = 0x00;
    frame->data[3] = (uint8_t)channel_index;
    frame->data[4] = 0x00;
    frame->data[5] = on ? 0x01 : 0x00;
    frame->dlc = 6;
    return true;
}

static bool app_program2_build_stitch_frame(const HeadCascadeCommandProfile *commands,
                                            size_t instance_index,
                                            size_t channel_index,
                                            bool on,
                                            HeadCanCommand *frame)
{
    static const uint8_t kMoves[5][4] = {
        { 0x00, 0x00, 0x04, 0x00 },
        { 0x40, 0x01, 0x04, 0x00 },
        { 0xE0, 0x01, 0x04, 0x00 },
        { 0xA0, 0x00, 0x04, 0x00 },
        { 0x80, 0x02, 0x05, 0x00 },
    };
    (void)on;
    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_program2_can_id(commands->can_id,
                                        commands->instance_can_ids,
                                        instance_index);
    const uint8_t selector = app_program2_selector(0,
                                                    commands->instance_selectors,
                                                    instance_index);
    if (channel_index < 5U) {
        frame->data[0] = 0x04;
        frame->data[1] = 0x01;
        frame->data[2] = 0x01;
        frame->data[3] = selector;
        memcpy(&frame->data[4], kMoves[channel_index], 4);
        frame->dlc = 8;
    } else {
        frame->data[0] = 0x04;
        frame->data[1] = 0x02;
        frame->data[2] = 0x01;
        frame->data[3] = selector;
        frame->data[4] = 0x01;
        frame->data[5] = 0x00;
        frame->dlc = 6;
    }
    return true;
}

const HeadCommandProfile kUnifiedProgram2Commands = {
    .program_id = APP_HEAD_PROGRAM_2,
    .program_name = "Programa 2 final",
    .init_sequence = {
        .phase1_steps = kInitPhase1,
        .phase1_step_count = sizeof(kInitPhase1) / sizeof(kInitPhase1[0]),
        .phase1_step_delay_ms = 80U,
        .phase_gap_ms = 5000U,
        .phase2_steps = NULL,
        .phase2_step_count = 0,
        .phase2_step_delay_ms = 200U,
        .skip_step_delay_after_wait = true,
    },
    .testeo = {
        .ping = { .can_id = 0x320, .data = { 0x07 }, .dlc = 1 },
        .response_can_id = 0x700,
        .reset_can_id = 0x702,
        .reset_data = { 0x3F, 0x00 },
        .reset_dlc = 2,
        .success_code = 0xCB,
        .missing_expansion_code = 0xBC,
        .missing_force_code = 0xBF,
        .force_board_1_code = 0xA1,
        .force_board_2_code = 0xA2,
        .max_tries = 25,
        .response_timeout_ms = 300U,
        .retry_delay_ms = 60U,
        .reset_debounce_ms = 250U,
    },
    .den = {
        .can_id = 0,
        .opcode = 0,
        .motor_index_base = 0,
        .instance_count = 8,
        .run_sequence = kDenRunSequence,
        .run_sequence_count = 5,
        .alternate_run_sequence = NULL,
        .alternate_run_sequence_count = 0,
        .positions = kDenPositions,
        .position_count = 5,
        .run_period_ms = 300U,
        .alternate_run_period_ms = 0,
        .instance_can_ids = kDenCanIds,
        .instance_selectors = kDenSelectors,
        .active_instance_mask = 0x33U,
        .build_frame = app_program2_build_den_frame,
    },
    .sic = {
        .can_id = 0,
        .opcode = 0,
        .motor_index_base = 0,
        .instance_count = 2,
        .run_sequence = kSicRunSequence,
        .run_sequence_count = 13,
        .alternate_run_sequence = NULL,
        .alternate_run_sequence_count = 0,
        .positions = kSicPositions,
        .position_count = 13,
        .run_period_ms = 300U,
        .alternate_run_period_ms = 0,
        .instance_can_ids = kSicCanIds,
        .instance_selectors = kSicSelectors,
        .active_instance_mask = 0x03U,
        .build_frame = app_program2_build_sic_frame,
    },
    .feet = {
        .can_id = 0,
        .opcode = 0,
        .motor_index_base = 0,
        .instance_count = 2,
        .run_sequence = NULL,
        .run_sequence_count = 0,
        .alternate_run_sequence = NULL,
        .alternate_run_sequence_count = 0,
        .positions = NULL,
        .position_count = 0,
        .run_period_ms = 0,
        .alternate_run_period_ms = 0,
        .instance_can_ids = kFeetCanIds,
        .instance_selectors = kFeetSelectors,
        .active_instance_mask = 0x03U,
        .build_frame = app_program2_build_feet_frame,
    },
    .j = {
        .can_id = 0,
        .opcode = 0,
        .instance_index_base = 0,
        .instance_count = 8,
        .channel_count = 8,
        .initial_register = 0xFF,
        .on_all_register = 0x00,
        .off_all_register = 0xFF,
        .run_period_ms = 80U,
        .instance_can_ids = kJCanIds,
        .instance_selectors = kJSelectors,
        .active_instance_mask = 0x33U,
        .build_frame = app_program2_build_j_frame,
    },
    .yarn = {
        .can_id = 0,
        .opcode = 0,
        .addresses = kYarnChannels,
        .addresses_per_instance = 8,
        .instance_count = 1,
        .on_value = 0x01,
        .off_value = 0x00,
        .run_period_ms = 80U,
        .instance_can_ids = kYarnCanIds,
        .instance_selectors = NULL,
        .active_instance_mask = 0x01U,
        .build_frame = app_program2_build_yarn_frame,
    },
    .stitch = {
        .can_id = 0,
        .opcode = 0,
        .addresses = kStitchSteps,
        .addresses_per_instance = 6,
        .instance_count = 2,
        .on_value = 0x01,
        .off_value = 0x00,
        .run_period_ms = 120U,
        .instance_can_ids = kStitchCanIds,
        .instance_selectors = kStitchSelectors,
        .active_instance_mask = 0x03U,
        .build_frame = app_program2_build_stitch_frame,
    },
    .stop = { .sends_can_frame = false, .frame = {} },
};
