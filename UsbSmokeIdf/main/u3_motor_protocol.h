#pragma once

#include <stddef.h>
#include <stdint.h>

inline constexpr uint32_t U3_SERVO_CAN_ID = 0x352U;
inline constexpr size_t U3_SERVO_FRAME_SIZE = 8U;
inline constexpr size_t U3_S1_SEQUENCE_MAX = 64U;

extern const uint16_t APP_U3_S2_SPEED_MAGNITUDES[30];
extern const uint8_t APP_U3_S2_RUN_FRAME_A[8];
extern const uint8_t APP_U3_S2_RUN_FRAME_C[8];
extern const uint8_t APP_U3_S2_STOP_FRAME[8];

bool app_u3_s1_build_position_frame(int32_t position, uint8_t out[8]);
bool app_u3_s1_validate_sequence(const char *sequence);
bool app_u3_s2_build_speed_frame(uint8_t level, bool right, uint8_t out[8]);
