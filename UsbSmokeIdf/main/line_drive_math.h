#pragma once
#include <stdint.h>

static constexpr int64_t LINE_DRIVE_OPERATOR_UNITS_PER_REV = 1000;
static constexpr int64_t LINE_DRIVE_STEPS_PER_REV = 256;
static inline int64_t line_drive_total_operator_units(int64_t target, int turns) { return (int64_t)turns * LINE_DRIVE_OPERATOR_UNITS_PER_REV + target; }
static inline int64_t line_drive_operator_units_to_steps(int64_t units) { return (units * LINE_DRIVE_STEPS_PER_REV) / LINE_DRIVE_OPERATOR_UNITS_PER_REV; }
static inline uint64_t line_drive_required_pulses(int64_t current, int64_t target) { int64_t delta=target-current; return (uint64_t)(delta < 0 ? -delta : delta); }