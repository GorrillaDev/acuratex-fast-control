#pragma once

#include <stdint.h>
#include "tusb.h"

#ifdef __cplusplus
extern "C" {
#endif

#define ACURATEX_USB_VENDOR_ID                 0xCAFE
#define ACURATEX_USB_PRODUCT_ID                0x4030
#define ACURATEX_USB_BCD_DEVICE                0x0100

#define ACURATEX_USB_VENDOR_REQUEST_MICROSOFT  0x20
#define ACURATEX_USB_MS_OS_20_DESC_LEN         0xB2

#define ACURATEX_USB_VENDOR_ITF                0
#define ACURATEX_USB_BULK_OUT_EP               0x01
#define ACURATEX_USB_BULK_IN_EP                0x81
#define ACURATEX_USB_BULK_EP_SIZE              64

#define ACURATEX_USB_INTERFACE_GUID            "{D7761D50-5F1B-4D33-95F2-733B0E5F2EED}"

#define ACURATEX_USB_CONFIG_TOTAL_LEN          (TUD_CONFIG_DESC_LEN + TUD_VENDOR_DESC_LEN)
#define ACURATEX_USB_BOS_TOTAL_LEN             (TUD_BOS_DESC_LEN + TUD_BOS_MICROSOFT_OS_DESC_LEN)

const tusb_desc_device_t* acuratex_usb_get_device_descriptor(void);
const uint8_t* acuratex_usb_get_configuration_descriptor(void);
const uint8_t* acuratex_usb_get_bos_descriptor(void);
const uint8_t* acuratex_usb_get_ms_os_20_descriptor(void);

const char* const* acuratex_usb_get_string_descriptor_table(void);
int acuratex_usb_get_string_descriptor_count(void);

void acuratex_usb_fill_serial_string(char* serialBuffer, int serialBufferLen);

#ifdef __cplusplus
}
#endif