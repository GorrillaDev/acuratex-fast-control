#include <cassert>
#include <cstddef>
#include <cstdint>

#include "head_unified_program_2_commands.h"

static void assert_frame(size_t channel, bool on)
{
    HeadCanCommand frame{};
    assert(kUnifiedProgram2Commands.yarn.build_frame(
        &kUnifiedProgram2Commands.yarn, 0U, channel, on, &frame));
    assert(frame.can_id == 0x363U);
    assert(frame.dlc == 6U);
    assert(frame.data[0] == 0x05U);
    assert(frame.data[1] == 0x01U);
    assert(frame.data[2] == 0x00U);
    assert(frame.data[3] == channel);
    assert(frame.data[4] == 0x00U);
    assert(frame.data[5] == (on ? 0x01U : 0x00U));
}

int main()
{
    const HeadCascadeCommandProfile &yarn = kUnifiedProgram2Commands.yarn;
    assert(yarn.instance_count == 1U);
    assert(yarn.active_instance_mask == 0x01U);
    assert(yarn.addresses_per_instance == 6U);
    assert(yarn.run_period_ms == 80U);
    for (size_t channel = 0; channel < yarn.addresses_per_instance; ++channel) {
        assert(yarn.addresses[channel] == channel);
        assert_frame(channel, false);
        assert_frame(channel, true);
    }
    assert_frame(0U, true);
    assert_frame(5U, false);
    return 0;
}