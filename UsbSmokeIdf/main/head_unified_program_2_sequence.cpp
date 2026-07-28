#include "head_unified_program_2_sequence.h"

// Catalogo CAN exacto de SECUENCIA_SEQ en firmware_programa2_final.ino.
static const HeadCanCommand kFrames[] = {
    { .can_id = 0x363, .data = { 0x04, 0x01, 0x02, 0x00, 0x00, 0x80 }, .dlc = 6 },
    { .can_id = 0x363, .data = { 0x04, 0x01, 0x02, 0x01, 0x00, 0x80 }, .dlc = 6 },
    { .can_id = 0x363, .data = { 0x04, 0x03, 0x02, 0x00, 0x00, 0x00 }, .dlc = 6 },
    { .can_id = 0x364, .data = { 0x04, 0x03, 0x02, 0x00, 0x00, 0x00 }, .dlc = 6 },
    { .can_id = 0x733, .data = { 0x04, 0x03, 0xFC, 0xFF, 0x00, 0x00, 0x00, 0x00 }, .dlc = 8 },
    { .can_id = 0x733, .data = { 0x04, 0x03, 0xFD, 0xFF, 0x00, 0x00, 0x00, 0x00 }, .dlc = 8 },
    { .can_id = 0x733, .data = { 0x04, 0x03, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x00 }, .dlc = 8 },
    { .can_id = 0x733, .data = { 0xBB, 0x06, 0x04, 0x00, 0x00, 0x00, 0x31, 0x00 }, .dlc = 8 },
    { .can_id = 0x733, .data = { 0xBB, 0x06, 0x04, 0x00, 0x00, 0x00, 0x32, 0x00 }, .dlc = 8 },
    { .can_id = 0x733, .data = { 0xBB, 0x07, 0x04, 0x00, 0x00, 0x00, 0x2B, 0x00 }, .dlc = 8 },
    { .can_id = 0x733, .data = { 0xF0, 0x00, 0xBC, 0x00, 0x00, 0x00 }, .dlc = 6 },
};

// Las 38 combinaciones unicas (trama, espera), en orden de primera aparicion.
static const HeadSequenceStep kStepCatalog[] = {
    { 2, 0 }, { 4, 0 }, { 3, 150 }, { 3, 100 },
    { 0, 50 }, { 5, 0 }, { 3, 77 }, { 8, 150 },
    { 3, 122 }, { 0, 28 }, { 3, 99 }, { 7, 50 },
    { 3, 75 }, { 7, 150 }, { 3, 134 }, { 0, 15 },
    { 3, 112 }, { 8, 37 }, { 1, 50 }, { 6, 0 },
    { 3, 71 }, { 9, 150 }, { 3, 70 }, { 3, 69 },
    { 3, 121 }, { 0, 29 }, { 3, 97 }, { 3, 119 },
    { 1, 31 }, { 3, 89 }, { 3, 104 }, { 1, 46 },
    { 3, 73 }, { 1, 0 }, { 0, 121 }, { 9, 7 },
    { 8, 13225 }, { 10, 0 },
};

// Los indices 0..2 describen una sola copia del motivo repetido. El resto
// contiene solamente las 120 acciones que no pertenecen a ese motivo.
static const uint8_t kStepStream[] = {
    0, 1, 2, 0, 1, 3, 4, 0, 5, 6, 7, 0, 1, 8, 9, 0, 5, 10, 11, 0, 1, 3, 4, 0,
    5, 12, 13, 0, 1, 3, 4, 0, 5, 12, 7, 0, 1, 14, 15, 0, 1, 16, 17, 0, 1, 3, 18, 0,
    19, 20, 21, 0, 1, 3, 18, 0, 19, 22, 21, 0, 1, 3, 18, 0, 19, 23, 21, 0, 1, 24, 25, 0,
    5, 26, 7, 0, 1, 3, 4, 0, 5, 6, 13, 0, 1, 3, 4, 0, 5, 6, 7, 0, 1, 3, 18, 0,
    19, 20, 21, 0, 1, 27, 28, 0, 19, 29, 21, 0, 1, 30, 31, 0, 19, 32, 21, 0, 1, 3, 33, 34,
    35, 36, 37,
};

// Los quince bloques con first_stream_index=0 expanden 152 repeticiones del
// motivo de tres pasos (456 acciones). Los otros quince bloques aportan ocho
// acciones literales cada uno (120); total expandido: 576.
static const HeadSequenceBlock kBlocks[] = {
    { 0, 3, 27 }, { 3, 8, 1 },
    { 0, 3, 4 }, { 11, 8, 1 },
    { 0, 3, 8 }, { 19, 8, 1 },
    { 0, 3, 5 }, { 27, 8, 1 },
    { 0, 3, 5 }, { 35, 8, 1 },
    { 0, 3, 20 }, { 43, 8, 1 },
    { 0, 3, 5 }, { 51, 8, 1 },
    { 0, 3, 5 }, { 59, 8, 1 },
    { 0, 3, 21 }, { 67, 8, 1 },
    { 0, 3, 6 }, { 75, 8, 1 },
    { 0, 3, 5 }, { 83, 8, 1 },
    { 0, 3, 12 }, { 91, 8, 1 },
    { 0, 3, 4 }, { 99, 8, 1 },
    { 0, 3, 4 }, { 107, 8, 1 },
    { 0, 3, 21 }, { 115, 8, 1 },
};

const HeadSequenceDefinition kUnifiedProgram2SequenceDefinition = {
    .frames = kFrames,
    .frame_count = sizeof(kFrames) / sizeof(kFrames[0]),
    .step_catalog = kStepCatalog,
    .step_catalog_count = sizeof(kStepCatalog) / sizeof(kStepCatalog[0]),
    .step_stream = kStepStream,
    .step_stream_count = sizeof(kStepStream) / sizeof(kStepStream[0]),
    .blocks = kBlocks,
    .block_count = sizeof(kBlocks) / sizeof(kBlocks[0]),
    .expanded_step_count = 576U,
    .max_batch_steps = APP_HEAD_SEQUENCE_DEFAULT_MAX_BATCH_STEPS,
};
