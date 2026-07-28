#include "head_sequence_executor.h"

#include <limits.h>

#include "freertos/FreeRTOS.h"

typedef struct {
    const HeadSequenceDefinition *definition;
    bool active;
    size_t current_step;
    uint32_t next_due_ms;
    uint32_t revision;
    int can_bus;
    app_head_sequence_state_t state;
    app_head_sequence_cancel_reason_t cancel_reason;
    esp_err_t last_error;
} HeadSequenceExecutorRuntime;

static portMUX_TYPE s_sequence_mux = portMUX_INITIALIZER_UNLOCKED;
static HeadSequenceExecutorRuntime s_sequence = {
    .definition = NULL,
    .active = false,
    .current_step = 0,
    .next_due_ms = 0,
    .revision = 0,
    .can_bus = 1,
    .state = APP_HEAD_SEQUENCE_STATE_IDLE,
    .cancel_reason = APP_HEAD_SEQUENCE_CANCEL_NONE,
    .last_error = ESP_OK,
};

static uint8_t app_head_sequence_batch_limit(const HeadSequenceDefinition *definition)
{
    uint8_t limit = definition != NULL ? definition->max_batch_steps : 0U;
    if (limit == 0U) {
        limit = APP_HEAD_SEQUENCE_DEFAULT_MAX_BATCH_STEPS;
    }
    if (limit > APP_HEAD_SEQUENCE_HARD_MAX_BATCH_STEPS) {
        limit = APP_HEAD_SEQUENCE_HARD_MAX_BATCH_STEPS;
    }
    return limit;
}

bool app_head_sequence_definition_is_valid(const HeadSequenceDefinition *definition)
{
    if (definition == NULL
        || definition->frames == NULL || definition->frame_count == 0U
        || definition->step_catalog == NULL || definition->step_catalog_count == 0U
        || definition->step_stream == NULL || definition->step_stream_count == 0U
        || definition->blocks == NULL || definition->block_count == 0U
        || definition->expanded_step_count == 0U) {
        return false;
    }

    for (size_t i = 0; i < definition->frame_count; ++i) {
        if (definition->frames[i].dlc > APP_HEAD_PROFILE_MAX_DLC) {
            return false;
        }
    }
    for (size_t i = 0; i < definition->step_catalog_count; ++i) {
        if (definition->step_catalog[i].frame_index >= definition->frame_count) {
            return false;
        }
    }
    for (size_t i = 0; i < definition->step_stream_count; ++i) {
        if (definition->step_stream[i] >= definition->step_catalog_count) {
            return false;
        }
    }

    size_t expanded_count = 0U;
    for (size_t i = 0; i < definition->block_count; ++i) {
        const HeadSequenceBlock *block = &definition->blocks[i];
        if (block->step_count == 0U || block->repetitions == 0U
            || block->first_stream_index >= definition->step_stream_count
            || block->step_count > definition->step_stream_count - block->first_stream_index
            || block->step_count > SIZE_MAX / block->repetitions) {
            return false;
        }
        const size_t block_steps = (size_t)block->step_count * block->repetitions;
        if (expanded_count > SIZE_MAX - block_steps) {
            return false;
        }
        expanded_count += block_steps;
    }
    return expanded_count == definition->expanded_step_count;
}

bool app_head_sequence_definition_expand_step(const HeadSequenceDefinition *definition,
                                              size_t expanded_index,
                                              HeadCanCommand *frame,
                                              uint16_t *wait_ms)
{
    if (frame == NULL || wait_ms == NULL
        || !app_head_sequence_definition_is_valid(definition)
        || expanded_index >= definition->expanded_step_count) {
        return false;
    }

    size_t remaining = expanded_index;
    for (size_t i = 0; i < definition->block_count; ++i) {
        const HeadSequenceBlock *block = &definition->blocks[i];
        const size_t block_steps = (size_t)block->step_count * block->repetitions;
        if (remaining >= block_steps) {
            remaining -= block_steps;
            continue;
        }

        const size_t stream_offset =
            block->first_stream_index + (remaining % block->step_count);
        const uint8_t catalog_index = definition->step_stream[stream_offset];
        const HeadSequenceStep *step = &definition->step_catalog[catalog_index];
        *frame = definition->frames[step->frame_index];
        *wait_ms = step->wait_ms;
        return true;
    }
    return false;
}

bool app_head_sequence_executor_start(const HeadSequenceDefinition *definition,
                                      int can_bus,
                                      uint32_t now_ms)
{
    if (!app_head_sequence_definition_is_valid(definition)) {
        return false;
    }

    portENTER_CRITICAL(&s_sequence_mux);
    s_sequence.definition = definition;
    s_sequence.active = true;
    s_sequence.current_step = 0U;
    s_sequence.next_due_ms = now_ms;
    s_sequence.can_bus = can_bus == 2 ? 2 : 1;
    s_sequence.state = APP_HEAD_SEQUENCE_STATE_RUNNING;
    s_sequence.cancel_reason = APP_HEAD_SEQUENCE_CANCEL_NONE;
    s_sequence.last_error = ESP_OK;
    s_sequence.revision++;
    portEXIT_CRITICAL(&s_sequence_mux);
    return true;
}

void app_head_sequence_executor_cancel(app_head_sequence_cancel_reason_t reason)
{
    if (reason == APP_HEAD_SEQUENCE_CANCEL_NONE) {
        reason = APP_HEAD_SEQUENCE_CANCEL_STOP;
    }

    portENTER_CRITICAL(&s_sequence_mux);
    if (reason >= s_sequence.cancel_reason) {
        s_sequence.cancel_reason = reason;
        s_sequence.state = reason == APP_HEAD_SEQUENCE_CANCEL_ERROR
            ? APP_HEAD_SEQUENCE_STATE_ERROR
            : APP_HEAD_SEQUENCE_STATE_CANCELLED;
    }
    s_sequence.active = false;
    s_sequence.next_due_ms = 0U;
    s_sequence.revision++;
    portEXIT_CRITICAL(&s_sequence_mux);
}

bool app_head_sequence_executor_is_active(void)
{
    portENTER_CRITICAL(&s_sequence_mux);
    const bool active = s_sequence.active;
    portEXIT_CRITICAL(&s_sequence_mux);
    return active;
}

HeadSequenceExecutorStatus app_head_sequence_executor_get_status(void)
{
    HeadSequenceExecutorStatus status = {};
    portENTER_CRITICAL(&s_sequence_mux);
    status.state = s_sequence.state;
    status.cancel_reason = s_sequence.cancel_reason;
    status.current_step = s_sequence.current_step;
    status.total_steps = s_sequence.definition != NULL
        ? s_sequence.definition->expanded_step_count
        : 0U;
    status.next_due_ms = s_sequence.next_due_ms;
    status.last_error = s_sequence.last_error;
    portEXIT_CRITICAL(&s_sequence_mux);
    return status;
}

esp_err_t app_head_sequence_executor_tick(app_head_sequence_can_send_fn_t send,
                                          uint32_t now_ms)
{
    if (send == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    uint8_t sent_this_tick = 0U;
    while (sent_this_tick < APP_HEAD_SEQUENCE_HARD_MAX_BATCH_STEPS) {
        const HeadSequenceDefinition *definition = NULL;
        size_t step_index = 0U;
        uint32_t revision = 0U;
        int bus = 1;

        portENTER_CRITICAL(&s_sequence_mux);
        if (!s_sequence.active
            || (int32_t)(now_ms - s_sequence.next_due_ms) < 0) {
            portEXIT_CRITICAL(&s_sequence_mux);
            return ESP_OK;
        }
        definition = s_sequence.definition;
        step_index = s_sequence.current_step;
        revision = s_sequence.revision;
        bus = s_sequence.can_bus;
        portEXIT_CRITICAL(&s_sequence_mux);

        if (definition == NULL
            || step_index >= definition->expanded_step_count) {
            portENTER_CRITICAL(&s_sequence_mux);
            if (s_sequence.active && s_sequence.revision == revision) {
                s_sequence.active = false;
                s_sequence.next_due_ms = 0U;
                s_sequence.state = APP_HEAD_SEQUENCE_STATE_COMPLETED;
            }
            portEXIT_CRITICAL(&s_sequence_mux);
            return ESP_OK;
        }

        HeadCanCommand frame = {};
        uint16_t wait_ms = 0U;
        if (!app_head_sequence_definition_expand_step(definition,
                                                      step_index,
                                                      &frame,
                                                      &wait_ms)) {
            portENTER_CRITICAL(&s_sequence_mux);
            if (s_sequence.active && s_sequence.revision == revision) {
                s_sequence.last_error = ESP_ERR_INVALID_ARG;
            }
            portEXIT_CRITICAL(&s_sequence_mux);
            app_head_sequence_executor_cancel(APP_HEAD_SEQUENCE_CANCEL_ERROR);
            return ESP_ERR_INVALID_ARG;
        }

        // Segunda comprobacion justo antes de transmitir: STOP/emergencia
        // pueden haber invalidado el snapshot mientras se expandia el paso.
        portENTER_CRITICAL(&s_sequence_mux);
        const bool still_current =
            s_sequence.active && s_sequence.revision == revision;
        portEXIT_CRITICAL(&s_sequence_mux);
        if (!still_current) {
            return ESP_OK;
        }

        const esp_err_t tx_err =
            send(bus, frame.can_id, frame.data, frame.dlc);
        if (tx_err != ESP_OK) {
            portENTER_CRITICAL(&s_sequence_mux);
            if (s_sequence.active && s_sequence.revision == revision) {
                s_sequence.last_error = tx_err;
            }
            portEXIT_CRITICAL(&s_sequence_mux);
            app_head_sequence_executor_cancel(APP_HEAD_SEQUENCE_CANCEL_ERROR);
            return tx_err;
        }

        bool continue_batch = false;
        portENTER_CRITICAL(&s_sequence_mux);
        if (s_sequence.active && s_sequence.revision == revision) {
            s_sequence.current_step++;
            if (s_sequence.current_step >= definition->expanded_step_count) {
                s_sequence.active = false;
                s_sequence.next_due_ms = 0U;
                s_sequence.state = APP_HEAD_SEQUENCE_STATE_COMPLETED;
            } else {
                s_sequence.next_due_ms = now_ms + wait_ms;
                continue_batch = wait_ms == 0U;
            }
        }
        portEXIT_CRITICAL(&s_sequence_mux);

        sent_this_tick++;
        if (!continue_batch
            || sent_this_tick >= app_head_sequence_batch_limit(definition)) {
            return ESP_OK;
        }
        // No se conserva ningun snapshot entre transmisiones. La siguiente
        // iteracion vuelve a comprobar revision y prioridad de cancelacion.
    }
    return ESP_OK;
}
