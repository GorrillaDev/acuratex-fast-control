#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "head_command_profile.h"

bool app_head_profile_instance_is_active(uint32_t active_instance_mask,
                                         size_t instance_index);
bool app_head_build_motion_frame(const HeadMotionCommandProfile *commands,
                                 size_t instance_index,
                                 uint16_t position,
                                 HeadCanCommand *frame);
bool app_head_build_j_frame(const HeadJCommandProfile *commands,
                            size_t instance_index,
                            uint8_t physical_register,
                            HeadCanCommand *frame);
bool app_head_build_cascade_frame(const HeadCascadeCommandProfile *commands,
                                  size_t instance_index,
                                  size_t channel_index,
                                  bool on,
                                  HeadCanCommand *frame);
