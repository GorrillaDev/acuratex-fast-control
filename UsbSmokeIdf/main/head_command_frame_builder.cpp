#include "head_command_frame_builder.h"

#include <string.h>

static uint32_t app_head_instance_can_id(uint32_t fallback,
                                         const uint32_t *ids,
                                         size_t instance_index)
{
    return ids != NULL ? ids[instance_index] : fallback;
}

static uint8_t app_head_instance_selector(uint8_t fallback,
                                          const uint8_t *selectors,
                                          size_t instance_index)
{
    return selectors != NULL ? selectors[instance_index]
                             : (uint8_t)(fallback + instance_index);
}

bool app_head_profile_instance_is_active(uint32_t active_instance_mask,
                                         size_t instance_index)
{
    return instance_index < 32U
        && (active_instance_mask == 0U
            || (active_instance_mask & (1UL << instance_index)) != 0U);
}

bool app_head_build_motion_frame(const HeadMotionCommandProfile *commands,
                                 size_t instance_index,
                                 uint16_t position,
                                 HeadCanCommand *frame)
{
    if (commands == NULL || frame == NULL
        || instance_index >= commands->instance_count
        || !app_head_profile_instance_is_active(commands->active_instance_mask, instance_index)) {
        return false;
    }

    if (commands->build_frame != NULL) {
        return commands->build_frame(commands, instance_index, position, frame);
    }

    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_head_instance_can_id(commands->can_id,
                                             commands->instance_can_ids,
                                             instance_index);
    const uint8_t selector = app_head_instance_selector(commands->motor_index_base,
                                                         commands->instance_selectors,
                                                         instance_index);
    frame->data[0] = commands->opcode;
    frame->data[1] = selector;
    frame->data[2] = (uint8_t)(position & 0xFFU);
    frame->data[3] = (uint8_t)((position >> 8U) & 0xFFU);
    frame->dlc = 4;
    return true;
}

bool app_head_build_j_frame(const HeadJCommandProfile *commands,
                            size_t instance_index,
                            uint8_t physical_register,
                            HeadCanCommand *frame)
{
    if (commands == NULL || frame == NULL
        || instance_index >= commands->instance_count
        || !app_head_profile_instance_is_active(commands->active_instance_mask, instance_index)) {
        return false;
    }

    if (commands->build_frame != NULL) {
        return commands->build_frame(commands,
                                     instance_index,
                                     physical_register,
                                     frame);
    }

    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_head_instance_can_id(commands->can_id,
                                             commands->instance_can_ids,
                                             instance_index);
    const uint8_t selector = app_head_instance_selector(commands->instance_index_base,
                                                         commands->instance_selectors,
                                                         instance_index);
    frame->data[0] = commands->opcode;
    frame->data[1] = selector;
    frame->data[2] = physical_register;
    frame->dlc = 3;
    return true;
}

bool app_head_build_cascade_frame(const HeadCascadeCommandProfile *commands,
                                  size_t instance_index,
                                  size_t channel_index,
                                  bool on,
                                  HeadCanCommand *frame)
{
    if (commands == NULL || frame == NULL
        || instance_index >= commands->instance_count
        || channel_index >= commands->addresses_per_instance
        || !app_head_profile_instance_is_active(commands->active_instance_mask, instance_index)) {
        return false;
    }

    if (commands->build_frame != NULL) {
        return commands->build_frame(commands,
                                     instance_index,
                                     channel_index,
                                     on,
                                     frame);
    }

    if (commands->addresses == NULL) {
        return false;
    }
    memset(frame, 0, sizeof(*frame));
    frame->can_id = app_head_instance_can_id(commands->can_id,
                                             commands->instance_can_ids,
                                             instance_index);
    frame->data[0] = commands->opcode;
    frame->data[1] = commands->addresses[
        instance_index * commands->addresses_per_instance + channel_index];
    frame->data[2] = on ? commands->on_value : commands->off_value;
    frame->dlc = 3;
    return true;
}
