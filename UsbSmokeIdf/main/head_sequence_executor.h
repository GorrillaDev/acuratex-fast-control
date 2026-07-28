#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "esp_err.h"
#include "head_command_profile.h"

// Limite defensivo por tick. P2 necesita como maximo tres transmisiones
// consecutivas para respetar todos sus WAIT 0; 16 deja margen reutilizable sin
// permitir que una definicion defectuosa monopolice la tarea de estado.
#define APP_HEAD_SEQUENCE_DEFAULT_MAX_BATCH_STEPS 16U
#define APP_HEAD_SEQUENCE_HARD_MAX_BATCH_STEPS 32U

typedef struct {
    uint8_t frame_index;
    uint16_t wait_ms;
} HeadSequenceStep;

typedef struct {
    uint16_t first_stream_index;
    uint16_t step_count;
    uint16_t repetitions;
} HeadSequenceBlock;

typedef struct {
    const HeadCanCommand *frames;
    size_t frame_count;
    const HeadSequenceStep *step_catalog;
    size_t step_catalog_count;
    const uint8_t *step_stream;
    size_t step_stream_count;
    const HeadSequenceBlock *blocks;
    size_t block_count;
    size_t expanded_step_count;
    uint8_t max_batch_steps;
} HeadSequenceDefinition;

typedef enum {
    APP_HEAD_SEQUENCE_STATE_IDLE = 0,
    APP_HEAD_SEQUENCE_STATE_RUNNING,
    APP_HEAD_SEQUENCE_STATE_COMPLETED,
    APP_HEAD_SEQUENCE_STATE_CANCELLED,
    APP_HEAD_SEQUENCE_STATE_ERROR,
} app_head_sequence_state_t;

// El orden numerico expresa prioridad. Emergencia nunca puede ser degradada
// por un STOP normal que llegue despues.
typedef enum {
    APP_HEAD_SEQUENCE_CANCEL_NONE = 0,
    APP_HEAD_SEQUENCE_CANCEL_STOP = 1,
    APP_HEAD_SEQUENCE_CANCEL_ERROR = 2,
    APP_HEAD_SEQUENCE_CANCEL_EMERGENCY = 3,
} app_head_sequence_cancel_reason_t;

typedef struct {
    app_head_sequence_state_t state;
    app_head_sequence_cancel_reason_t cancel_reason;
    size_t current_step;
    size_t total_steps;
    uint32_t next_due_ms;
    esp_err_t last_error;
} HeadSequenceExecutorStatus;

typedef esp_err_t (*app_head_sequence_can_send_fn_t)(int bus,
                                                     uint32_t id,
                                                     const uint8_t *data,
                                                     size_t len);

bool app_head_sequence_definition_is_valid(const HeadSequenceDefinition *definition);
bool app_head_sequence_definition_expand_step(const HeadSequenceDefinition *definition,
                                              size_t expanded_index,
                                              HeadCanCommand *frame,
                                              uint16_t *wait_ms);

bool app_head_sequence_executor_start(const HeadSequenceDefinition *definition,
                                      int can_bus,
                                      uint32_t now_ms);
void app_head_sequence_executor_cancel(app_head_sequence_cancel_reason_t reason);
bool app_head_sequence_executor_is_active(void);
HeadSequenceExecutorStatus app_head_sequence_executor_get_status(void);
esp_err_t app_head_sequence_executor_tick(app_head_sequence_can_send_fn_t send,
                                          uint32_t now_ms);
