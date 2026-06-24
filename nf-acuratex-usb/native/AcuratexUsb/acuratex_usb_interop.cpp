#include <stdint.h>
#include <stdbool.h>
#include <string.h>

#include "acuratex_usb_driver.h"
#include "acuratex_usb_bridge.h"

extern "C"
{
    bool acuratex_usb_native_init(void)
    {
        return acuratex_usb_driver_init();
    }

    bool acuratex_usb_native_is_mounted(void)
    {
        return acuratex_usb_bridge_is_mounted();
    }

    int acuratex_usb_native_read_line(uint8_t* buffer, int maxLen)
    {
        return acuratex_usb_bridge_read_line(buffer, maxLen);
    }

    bool acuratex_usb_native_write_line(const uint8_t* data, int len)
    {
        return acuratex_usb_bridge_write_line(data, len);
    }
}