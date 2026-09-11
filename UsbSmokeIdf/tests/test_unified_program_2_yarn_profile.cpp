#include <cassert>
#include <cstddef>
#include <cstdint>
#include <cstring>

#include "head_command_frame_builder.h"
#include "head_unified_program_2_commands.h"

static void assert_cascade_frame(const HeadCascadeCommandProfile &commands,
                                 size_t instance,
                                 size_t channel,
                                 bool on,
                                 uint8_t address,
                                 uint8_t value)
{
    HeadCanCommand frame{};
    assert(app_head_build_cascade_frame(&commands, instance, channel, on, &frame));
    assert(frame.can_id == 0x320U);
    assert(frame.dlc == 3U);
    assert(frame.data[0] == 0x1EU);
    assert(frame.data[1] == address);
    assert(frame.data[2] == value);
}

int main()
{
    const HeadInitCommandSequence &init = kUnifiedProgram2Commands.init_sequence;
    assert(init.phase1_step_count == 71U);
    assert(init.phase1_step_delay_ms == 80U);
    assert(init.phase_gap_ms == 5000U);
    assert(init.phase2_step_count == 101U);
    assert(init.phase2_step_delay_ms == 200U);
    assert(init.skip_step_delay_after_wait);
    assert(std::strcmp(init.phase1_steps[0], "320 07") == 0);
    assert(std::strcmp(init.phase1_steps[70], "320 1e 27 01") == 0);
    assert(std::strcmp(init.phase2_steps[0], "320 30") == 0);
    assert(std::strcmp(init.phase2_steps[100], "320 0e 09") == 0);

    const HeadTesteoCommandProfile &testeo = kUnifiedProgram2Commands.testeo;
    assert(testeo.ping.can_id == 0x320U && testeo.ping.dlc == 1U);
    assert(testeo.ping.data[0] == 0x07U);
    assert(testeo.response_can_id == 0x700U);
    assert(testeo.reset_can_id == 0x702U && testeo.reset_dlc == 2U);
    assert(testeo.reset_data[0] == 0x3FU && testeo.reset_data[1] == 0x00U);

    const HeadMotionCommandProfile &den = kUnifiedProgram2Commands.den;
    assert(den.instance_count == 4U);
    assert(den.active_instance_mask == 0x0FU);
    assert(den.run_period_ms == 80U);
    assert(den.alternate_run_period_ms == 300U);
    const uint16_t den_positions[] = {650, 487, 325, 162, 18};
    const uint8_t den_run[] = {5, 3, 1, 4, 2};
    const uint8_t den_run1[] = {5, 3, 1};
    for (size_t i = 0; i < 5U; ++i) {
        assert(den.positions[i] == den_positions[i]);
        assert(den.run_sequence[i] == den_run[i]);
    }
    for (size_t i = 0; i < 3U; ++i) assert(den.alternate_run_sequence[i] == den_run1[i]);
    HeadCanCommand frame{};
    assert(app_head_build_motion_frame(&den, 3U, 650U, &frame));
    assert(frame.can_id == 0x320U && frame.dlc == 4U);
    assert(frame.data[0] == 0x1CU && frame.data[1] == 0x03U);
    assert(frame.data[2] == 0x8AU && frame.data[3] == 0x02U);
    assert(!app_head_build_motion_frame(&den, 4U, 650U, &frame));

    const HeadMotionCommandProfile &sic = kUnifiedProgram2Commands.sic;
    assert(sic.instance_count == 2U && sic.run_period_ms == 300U);
    assert(app_head_build_motion_frame(&sic, 1U, sic.positions[0], &frame));
    assert(frame.can_id == 0x320U && frame.dlc == 4U);
    assert(frame.data[0] == 0x1CU && frame.data[1] == 0x09U);
    assert(frame.data[2] == 0x3CU && frame.data[3] == 0x00U);

    const HeadJCommandProfile &selection = kUnifiedProgram2Commands.j;
    assert(selection.instance_count == 4U && selection.active_instance_mask == 0x0FU);
    assert(selection.on_all_register == 0x00U && selection.off_all_register == 0xFFU);
    assert(selection.preserve_register_on_run);
    assert(app_head_build_j_frame(&selection, 3U, 0xA5U, &frame));
    assert(frame.can_id == 0x320U && frame.dlc == 3U);
    assert(frame.data[0] == 0x1DU && frame.data[1] == 0x03U && frame.data[2] == 0xA5U);
    assert(!app_head_build_j_frame(&selection, 4U, 0x00U, &frame));

    const HeadCascadeCommandProfile &yarn = kUnifiedProgram2Commands.yarn;
    assert(yarn.instance_count == 1U);
    assert(yarn.active_instance_mask == 0x01U);
    assert(yarn.addresses_per_instance == 8U);
    assert(yarn.run_period_ms == 80U);
    for (size_t channel = 0; channel < yarn.addresses_per_instance; ++channel) {
        assert(yarn.addresses[channel] == 0x18U + channel);
        assert_cascade_frame(yarn, 0U, channel, false, (uint8_t)(0x18U + channel), 0x01U);
        assert_cascade_frame(yarn, 0U, channel, true, (uint8_t)(0x18U + channel), 0x00U);
    }
    assert(!app_head_build_cascade_frame(&yarn, 1U, 0U, true, &frame));

    const HeadCascadeCommandProfile &transfer = kUnifiedProgram2Commands.stitch;
    const uint8_t transfer_addresses[] = {0x00, 0x01, 0x02, 0x05, 0x06, 0x07, 0x08, 0x0B};
    assert(transfer.instance_count == 2U && transfer.addresses_per_instance == 4U);
    assert(transfer.active_instance_mask == 0x03U && transfer.run_period_ms == 80U);
    for (size_t instance = 0; instance < 2U; ++instance) {
        for (size_t channel = 0; channel < 4U; ++channel) {
            const uint8_t address = transfer_addresses[instance * 4U + channel];
            assert_cascade_frame(transfer, instance, channel, false, address, 0x00U);
            assert_cascade_frame(transfer, instance, channel, true, address, 0x01U);
        }
    }
    assert(!app_head_build_cascade_frame(&transfer, 2U, 0U, true, &frame));
    assert(kUnifiedProgram2Commands.feet.instance_count == 0U);
    return 0;
}
