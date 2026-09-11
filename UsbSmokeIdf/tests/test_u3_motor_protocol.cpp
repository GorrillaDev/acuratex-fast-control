#include <array>
#include <cassert>
#include <cstdint>
#include <cstring>

#include "can_physical_config.h"
#include "command_unified_head_processor.h"
#include "u3_motor_protocol.h"

static void assert_frame(const uint8_t actual[8], const std::array<uint8_t, 8> &expected)
{
    assert(std::memcmp(actual, expected.data(), expected.size()) == 0);
}

int main()
{
    uint8_t frame[8] = {};
    assert(app_u3_s1_build_position_frame(0, frame));
    assert_frame(frame, {0x2B,0x08,0x00,0x47,0x47,0x47,0x47,0x47});
    assert(app_u3_s1_build_position_frame(500, frame));
    assert_frame(frame, {0x2B,0x08,0xF4,0x46,0xB3,0x46,0x47,0xB3});
    assert(app_u3_s1_build_position_frame(1000, frame));
    assert_frame(frame, {0x2B,0x08,0xE8,0x44,0xAF,0x44,0x47,0xAF});
    assert(app_u3_s1_build_position_frame(-1, frame));
    assert_frame(frame, {0x2A,0x08,0xFF,0xB9,0xB9,0xB9,0x46,0xB9});
    assert(app_u3_s1_build_position_frame(-500, frame));
    assert_frame(frame, {0x2B,0x08,0x0C,0xB9,0x4B,0xB9,0x47,0x4B});
    assert(!app_u3_s1_build_position_frame(-32769, frame));
    assert(!app_u3_s1_build_position_frame(32768, frame));

    assert(app_u3_s1_validate_sequence("123"));
    assert(app_u3_s1_validate_sequence("13231"));
    assert(!app_u3_s1_validate_sequence("124"));
    assert(!app_u3_s1_validate_sequence(""));
    char too_long[66];
    std::memset(too_long, '1', 65U);
    too_long[65] = '\0';
    assert(!app_u3_s1_validate_sequence(too_long));

    const uint16_t magnitudes[30] = {
        12,22,24,34,53,63,65,75,82,100,110,112,123,141,151,
        159,160,170,188,198,201,211,229,239,246,248,258,276,287,289,
    };
    for (size_t i = 0; i < 30U; ++i) assert(APP_U3_S2_SPEED_MAGNITUDES[i] == magnitudes[i]);

    assert(app_u3_s2_build_speed_frame(1, true, frame));
    assert_frame(frame, {0x4D,0x08,0x27,0x00,0x2D,0x24,0x24,0x24});
    assert(app_u3_s2_build_speed_frame(1, false, frame));
    assert_frame(frame, {0x4D,0x08,0x27,0x00,0x2D,0x34,0x24,0x24});
    assert(app_u3_s2_build_speed_frame(10, true, frame));
    assert_frame(frame, {0x4D,0x08,0x27,0x00,0x45,0x24,0x24,0x24});
    assert(app_u3_s2_build_speed_frame(10, false, frame));
    assert_frame(frame, {0x4D,0x08,0x27,0x00,0x45,0x34,0x24,0x24});
    assert(app_u3_s2_build_speed_frame(27, true, frame));
    assert_frame(frame, {0x4D,0x08,0x27,0x00,0x23,0x25,0x24,0x24});
    assert(app_u3_s2_build_speed_frame(30, true, frame));
    assert_frame(frame, {0x4D,0x08,0x27,0x00,0x00,0x25,0x24,0x24});
    assert(app_u3_s2_build_speed_frame(30, false, frame));
    assert_frame(frame, {0x4D,0x08,0x27,0x00,0x00,0x35,0x24,0x24});
    assert(!app_u3_s2_build_speed_frame(0, true, frame));
    assert(!app_u3_s2_build_speed_frame(31, true, frame));

    assert_frame(APP_U3_S2_RUN_FRAME_A, {0xE0,0x08,0x04,0x89,0x89,0x89,0x89,0x8D});
    assert_frame(APP_U3_S2_RUN_FRAME_C, {0x15,0x08,0x01,0x7C,0x7C,0x7C,0x7C,0x7D});
    assert_frame(APP_U3_S2_STOP_FRAME, {0x25,0x08,0x4E,0x00,0x4D,0x4C,0x4C,0x4C});

    assert(APP_CAN1_PHYSICAL.bus == 1 && APP_CAN1_PHYSICAL.tx_gpio == 4
           && APP_CAN1_PHYSICAL.rx_gpio == 5 && APP_CAN1_PHYSICAL.stby_gpio == 6);
    assert(APP_CAN2_PHYSICAL.bus == 2 && APP_CAN2_PHYSICAL.tx_gpio == 7
           && APP_CAN2_PHYSICAL.rx_gpio == 15 && APP_CAN2_PHYSICAL.stby_gpio == 16);
    assert(APP_CAN_BITRATE == 1000000U && app_can_physical_config_is_safe());
    assert(APP_UNIFIED_HEAD_PHYSICAL_CAN_BUS == APP_CMD_CAN_BUS_1);
    return 0;
}
