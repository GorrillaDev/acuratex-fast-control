#pragma once

#include <stdint.h>

// Configuracion fisica validada de los dos transceivers del tester.
// Los nombres CAN1/CAN2 describen conectores fisicos, no motores.
struct AppCanPhysicalConfig {
    int bus;
    int tx_gpio;
    int rx_gpio;
    int stby_gpio;
};

inline constexpr AppCanPhysicalConfig APP_CAN1_PHYSICAL = {1, 4, 5, 6};
inline constexpr AppCanPhysicalConfig APP_CAN2_PHYSICAL = {2, 7, 15, 16};
inline constexpr uint32_t APP_CAN_BITRATE = 1000000U;

inline constexpr bool app_can_physical_config_is_safe()
{
    return APP_CAN1_PHYSICAL.stby_gpio != APP_CAN2_PHYSICAL.stby_gpio
        && APP_CAN1_PHYSICAL.tx_gpio != APP_CAN2_PHYSICAL.tx_gpio
        && APP_CAN1_PHYSICAL.rx_gpio != APP_CAN2_PHYSICAL.rx_gpio;
}

static_assert(app_can_physical_config_is_safe(),
              "CAN1 y CAN2 deben usar GPIO fisicos independientes");
