#include "u3_motor_protocol.h"

#include <string.h>

const uint16_t APP_U3_S2_SPEED_MAGNITUDES[30] = {
    12, 22, 24, 34, 53, 63, 65, 75, 82, 100,
    110, 112, 123, 141, 151, 159, 160, 170, 188, 198,
    201, 211, 229, 239, 246, 248, 258, 276, 287, 289,
};

const uint8_t APP_U3_S2_RUN_FRAME_A[8] = {
    0xE0, 0x08, 0x04, 0x89, 0x89, 0x89, 0x89, 0x8D,
};

const uint8_t APP_U3_S2_RUN_FRAME_C[8] = {
    0x15, 0x08, 0x01, 0x7C, 0x7C, 0x7C, 0x7C, 0x7D,
};

const uint8_t APP_U3_S2_STOP_FRAME[8] = {
    0x25, 0x08, 0x4E, 0x00, 0x4D, 0x4C, 0x4C, 0x4C,
};

bool app_u3_s1_build_position_frame(int32_t position, uint8_t out[8])
{
    if (out == nullptr || position < -32768 || position > 32767) return false;

    const uint16_t raw = static_cast<uint16_t>(static_cast<int16_t>(position));
    const uint8_t high = static_cast<uint8_t>(raw >> 8);
    const uint8_t low = static_cast<uint8_t>(raw & 0xFFU);
    const uint8_t seed = (low & 1U) != 0U ? 0x2AU : 0x2BU;
    const uint8_t key = seed ^ 0x6CU;
    const uint8_t clean[6] = {
        static_cast<uint8_t>(low ^ key), high, low, high, 0x00U, low,
    };

    out[0] = seed;
    out[1] = 0x08U;
    for (size_t i = 0; i < 6U; ++i) out[i + 2U] = clean[i] ^ key;
    return true;
}

bool app_u3_s1_validate_sequence(const char *sequence)
{
    if (sequence == nullptr) return false;
    const size_t length = strlen(sequence);
    if (length == 0U || length > U3_S1_SEQUENCE_MAX) return false;
    for (size_t i = 0; i < length; ++i) {
        if (sequence[i] < '1' || sequence[i] > '3') return false;
    }
    return true;
}

bool app_u3_s2_build_speed_frame(uint8_t level, bool right, uint8_t out[8])
{
    if (out == nullptr || level < 1U || level > 30U) return false;

    constexpr uint8_t seed = 0x4DU;
    constexpr uint8_t key = 0x21U;
    const uint16_t magnitude = APP_U3_S2_SPEED_MAGNITUDES[level - 1U];
    const uint8_t low = static_cast<uint8_t>(magnitude & 0xFFU);
    const uint8_t high = static_cast<uint8_t>(magnitude >> 8);
    uint8_t marker = high == 0U ? 0x05U : 0x04U;
    if (!right) marker ^= 0x10U;

    const uint8_t clean[6] = {0x06U, 0x21U, low, marker, 0x05U, 0x05U};
    out[0] = seed;
    out[1] = 0x08U;
    for (size_t i = 0; i < 6U; ++i) out[i + 2U] = clean[i] ^ key;
    return true;
}
