#include "head_unified_program_2_commands.h"

#include <string.h>

// Fuente funcional: referenciaspresentacion/firmware.ino y
// referenciaspresentacion/dasboardreferencia.html.
// Solo se portan CAN y tiempos al runtime ESP-IDF existente.
static const char *const kInitPhase1[] = {
    "320 07", "WAIT 2000", "320 30", "WAIT 2000", "370 fd 06 10 00", "WAIT 2000",
    "370 fd 06 11 00", "WAIT 2000", "320 2d 00 bf ff", "WAIT 2000", "320 00", "320 07",
    "320 07", "320 07", "320 07", "320 02", "320 07", "320 25 07",
    "320 05", "320 19 04", "320 1a 19", "320 4d 18 0d 00", "320 4d 19 0d 00", "320 2b 00 03",
    "320 2c 00 03", "320 43 00", "320 2d 00 bf ff", "320 4c 02 32 00", "320 5a 08 b0 04", "320 5a 09 b0 04",
    "320 48 00 01 00", "320 00", "320 53 00 ff 03", "320 38 00 5a 01", "320 54 00", "320 58 00 00",
    "320 54 01", "320 58 01 00", "320 54 02", "320 58 02 00", "320 54 03", "320 58 03 00",
    "320 54 04", "320 58 04 00", "320 54 05", "320 58 05 00", "320 54 06", "320 58 06 00",
    "320 54 07", "320 58 07 00", "320 54 08", "320 58 08 00", "320 54 09", "320 58 09 00",
    "320 07", "320 1e 18 01", "320 1e 19 01", "320 1e 1a 01", "320 1e 1b 01", "320 1e 1c 01",
    "320 1e 1d 01", "320 1e 1e 01", "320 1e 1f 01", "320 1e 20 01", "320 1e 21 01", "320 1e 22 01",
    "320 1e 23 01", "320 1e 24 01", "320 1e 25 01", "320 1e 26 01", "320 1e 27 01",
};

static const char *const kInitPhase2[] = {
    "320 30", "WAIT 2000", "320 30", "WAIT 2000",
    "320 0d 00", "320 0e 00", "320 0c 00", "320 0e 01", "320 0d 01", "320 0d 02",
    "320 0e 02", "320 0c 01", "320 0e 03", "320 0d 03", "320 0d 04", "320 0e 04",
    "320 0c 02", "320 0e 05", "320 0d 05", "320 0d 06", "320 0e 06", "320 0c 03",
    "320 0e 07", "320 0d 07", "320 09", "320 26 01", "320 26 00", "320 09",
    "320 26 02", "320 26 03", "320 0b", "320 54 00", "320 54 01", "320 54 02",
    "320 54 03", "320 54 04", "320 54 05", "320 54 06", "320 54 07", "320 54 08",
    "320 54 09",
    "320 1c 00 08 00", "WAIT 200", "320 1c 00 00 00", "WAIT 200", "320 07", "320 0e 00",
    "320 1c 01 08 00", "WAIT 200", "320 1c 01 00 00", "WAIT 200", "320 07", "320 0e 01",
    "320 1c 02 08 00", "WAIT 200", "320 1c 02 00 00", "WAIT 200", "320 07", "320 0e 02",
    "320 1c 03 08 00", "WAIT 200", "320 1c 03 00 00", "WAIT 200", "320 07", "320 0e 03",
    "320 1c 04 08 00", "WAIT 200", "320 1c 04 00 00", "WAIT 200", "320 07", "320 0e 04",
    "320 1c 05 08 00", "WAIT 200", "320 1c 05 00 00", "WAIT 200", "320 07", "320 0e 05",
    "320 1c 06 08 00", "WAIT 200", "320 1c 06 00 00", "WAIT 200", "320 07", "320 0e 06",
    "320 1c 07 08 00", "WAIT 200", "320 1c 07 00 00", "WAIT 200", "320 07", "320 0e 07",
    "320 1c 08 08 00", "WAIT 200", "320 1c 08 00 00", "WAIT 200", "320 07", "320 0e 08",
    "320 1c 09 08 00", "WAIT 200", "320 1c 09 00 00", "WAIT 200", "320 07", "320 0e 09",
};

static const uint16_t kDenPositions[5] = { 650, 487, 325, 162, 18 };
static const uint8_t kDenRunSequence[5] = { 5, 3, 1, 4, 2 };
static const uint8_t kDenRun1Sequence[3] = { 5, 3, 1 };

// La tabla comun conserva POS1=0 para SIC1. El builder convierte ese unico
// valor a 60 para SIC2, tanto en seleccion directa como durante RUN.
static const uint16_t kSicPositions[3] = { 0, 374, 750 };
static const uint8_t kSicRunSequence[3] = { 1, 2, 3 };

static const uint8_t kYarnAddresses[8] = {
    0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
};
static const uint8_t kTransferAddresses[8] = {
    0x00, 0x01, 0x02, 0x05,
    0x06, 0x07, 0x08, 0x0B,
};

static bool app_program2_build_sic_frame(const HeadMotionCommandProfile *commands,
                                         size_t instance_index,
                                         uint16_t position,
                                         HeadCanCommand *frame)
{
    if (commands == NULL || frame == NULL || instance_index >= commands->instance_count) {
        return false;
    }

    const uint16_t physical_position =
        instance_index == 1U && position == 0U ? 60U : position;
    memset(frame, 0, sizeof(*frame));
    frame->can_id = commands->can_id;
    frame->data[0] = commands->opcode;
    frame->data[1] = (uint8_t)(commands->motor_index_base + instance_index);
    frame->data[2] = (uint8_t)(physical_position & 0xFFU);
    frame->data[3] = (uint8_t)((physical_position >> 8U) & 0xFFU);
    frame->dlc = 4;
    return true;
}

const HeadCommandProfile kUnifiedProgram2Commands = {
    .program_id = APP_HEAD_PROGRAM_2,
    .program_name = "Cabezal presentacion 10-09-2026",
    .init_sequence = {
        .phase1_steps = kInitPhase1,
        .phase1_step_count = sizeof(kInitPhase1) / sizeof(kInitPhase1[0]),
        .phase1_step_delay_ms = 80U,
        .phase_gap_ms = 5000U,
        .phase2_steps = kInitPhase2,
        .phase2_step_count = sizeof(kInitPhase2) / sizeof(kInitPhase2[0]),
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
        .can_id = 0x320,
        .opcode = 0x1C,
        .motor_index_base = 0x00,
        .instance_count = 4,
        .run_sequence = kDenRunSequence,
        .run_sequence_count = sizeof(kDenRunSequence) / sizeof(kDenRunSequence[0]),
        .alternate_run_sequence = kDenRun1Sequence,
        .alternate_run_sequence_count = sizeof(kDenRun1Sequence) / sizeof(kDenRun1Sequence[0]),
        .positions = kDenPositions,
        .position_count = sizeof(kDenPositions) / sizeof(kDenPositions[0]),
        .run_period_ms = 80U,
        .alternate_run_period_ms = 300U,
        .instance_can_ids = NULL,
        .instance_selectors = NULL,
        .active_instance_mask = 0x0FU,
        .build_frame = NULL,
    },
    .sic = {
        .can_id = 0x320,
        .opcode = 0x1C,
        .motor_index_base = 0x08,
        .instance_count = 2,
        .run_sequence = kSicRunSequence,
        .run_sequence_count = sizeof(kSicRunSequence) / sizeof(kSicRunSequence[0]),
        .alternate_run_sequence = NULL,
        .alternate_run_sequence_count = 0,
        .positions = kSicPositions,
        .position_count = sizeof(kSicPositions) / sizeof(kSicPositions[0]),
        .run_period_ms = 300U,
        .alternate_run_period_ms = 0,
        .instance_can_ids = NULL,
        .instance_selectors = NULL,
        .active_instance_mask = 0x03U,
        .build_frame = app_program2_build_sic_frame,
    },
    .feet = {
        .can_id = 0,
        .opcode = 0,
        .motor_index_base = 0,
        .instance_count = 0,
        .run_sequence = NULL,
        .run_sequence_count = 0,
        .alternate_run_sequence = NULL,
        .alternate_run_sequence_count = 0,
        .positions = NULL,
        .position_count = 0,
        .run_period_ms = 0,
        .alternate_run_period_ms = 0,
        .instance_can_ids = NULL,
        .instance_selectors = NULL,
        .active_instance_mask = 0,
        .build_frame = NULL,
    },
    .j = {
        .can_id = 0x320,
        .opcode = 0x1D,
        .instance_index_base = 0x00,
        .instance_count = 4,
        .channel_count = 8,
        .initial_register = 0xFF,
        .on_all_register = 0x00,
        .off_all_register = 0xFF,
        .run_period_ms = 80U,
        .instance_can_ids = NULL,
        .instance_selectors = NULL,
        .active_instance_mask = 0x0FU,
        .preserve_register_on_run = true,
        .build_frame = NULL,
    },
    .yarn = {
        .can_id = 0x320,
        .opcode = 0x1E,
        .addresses = kYarnAddresses,
        .addresses_per_instance = 8,
        .instance_count = 1,
        .on_value = 0x00,
        .off_value = 0x01,
        .run_period_ms = 80U,
        .instance_can_ids = NULL,
        .instance_selectors = NULL,
        .active_instance_mask = 0x01U,
        .build_frame = NULL,
    },
    .stitch = {
        .can_id = 0x320,
        .opcode = 0x1E,
        .addresses = kTransferAddresses,
        .addresses_per_instance = 4,
        .instance_count = 2,
        .on_value = 0x01,
        .off_value = 0x00,
        .run_period_ms = 80U,
        .instance_can_ids = NULL,
        .instance_selectors = NULL,
        .active_instance_mask = 0x03U,
        .build_frame = NULL,
    },
    .stop = { .sends_can_frame = false, .frame = {} },
};
