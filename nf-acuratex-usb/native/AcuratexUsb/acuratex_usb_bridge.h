#pragma once

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

bool acuratex_usb_bridge_init(void);

void acuratex_usb_bridge_set_mounted(bool mounted);
bool acuratex_usb_bridge_is_mounted(void);

void acuratex_usb_bridge_reset_rx_line(void);
void acuratex_usb_bridge_process_rx_bytes(const uint8_t* data, int len);

int  acuratex_usb_bridge_read_line(uint8_t* buffer, int maxLen);
bool acuratex_usb_bridge_write_line(const uint8_t* data, int len);

#ifdef __cplusplus
}
#endif