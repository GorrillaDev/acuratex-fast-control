#include <stdio.h>
#include <string.h>
#include "esp_mac.h"
#include "tusb.h"
#include "class/vendor/vendor_device.h"
#include "acuratex_usb_descriptors.h"

static char s_serial_number[17] = "000000000000";

static const tusb_desc_device_t s_device_descriptor = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0210,
    .bDeviceClass = TUSB_CLASS_VENDOR_SPECIFIC,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = ACURATEX_USB_VENDOR_ID,
    .idProduct = ACURATEX_USB_PRODUCT_ID,
    .bcdDevice = ACURATEX_USB_BCD_DEVICE,
    .iManufacturer = 0x01,
    .iProduct = 0x02,
    .iSerialNumber = 0x03,
    .bNumConfigurations = 0x01,
};

static const char* s_string_descriptor[] = {
    (char[]){0x09, 0x04},
    "Acuratex",
    "Acuratex Control Bridge",
    s_serial_number,
    "Acuratex Vendor Interface",
};

static const uint8_t s_configuration_descriptor[] = {
    TUD_CONFIG_DESCRIPTOR(1, 1, 0, ACURATEX_USB_CONFIG_TOTAL_LEN, 0, 100),
    TUD_VENDOR_DESCRIPTOR(
        ACURATEX_USB_VENDOR_ITF,
        4,
        ACURATEX_USB_BULK_OUT_EP,
        ACURATEX_USB_BULK_IN_EP,
        ACURATEX_USB_BULK_EP_SIZE
    ),
};

static const uint8_t s_bos_descriptor[] = {
    TUD_BOS_DESCRIPTOR(ACURATEX_USB_BOS_TOTAL_LEN, 1),
    TUD_BOS_MS_OS_20_DESCRIPTOR(
        ACURATEX_USB_MS_OS_20_DESC_LEN,
        ACURATEX_USB_VENDOR_REQUEST_MICROSOFT
    ),
};

static const uint8_t s_ms_os_20_descriptor[] = {
    /* Set header */
    U16_TO_U8S_LE(0x000A), U16_TO_U8S_LE(MS_OS_20_SET_HEADER_DESCRIPTOR),
    U32_TO_U8S_LE(0x06030000), U16_TO_U8S_LE(ACURATEX_USB_MS_OS_20_DESC_LEN),

    /* Configuration subset header */
    U16_TO_U8S_LE(0x0008), U16_TO_U8S_LE(MS_OS_20_SUBSET_HEADER_CONFIGURATION),
    0x00, 0x00,
    U16_TO_U8S_LE(ACURATEX_USB_MS_OS_20_DESC_LEN - 0x0A),

    /* Function subset header */
    U16_TO_U8S_LE(0x0008), U16_TO_U8S_LE(MS_OS_20_SUBSET_HEADER_FUNCTION),
    ACURATEX_USB_VENDOR_ITF, 0x00,
    U16_TO_U8S_LE(ACURATEX_USB_MS_OS_20_DESC_LEN - 0x0A - 0x08),

    /* Compatible ID -> WINUSB */
    U16_TO_U8S_LE(0x0014), U16_TO_U8S_LE(MS_OS_20_FEATURE_COMPATBLE_ID),
    'W','I','N','U','S','B',0x00,0x00,
    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,

    /* Registry property -> DeviceInterfaceGUIDs */
    U16_TO_U8S_LE(ACURATEX_USB_MS_OS_20_DESC_LEN - 0x0A - 0x08 - 0x08 - 0x14),
    U16_TO_U8S_LE(MS_OS_20_FEATURE_REG_PROPERTY),
    U16_TO_U8S_LE(0x0007), U16_TO_U8S_LE(0x002A),

    'D',0,'e',0,'v',0,'i',0,'c',0,'e',0,
    'I',0,'n',0,'t',0,'e',0,'r',0,'f',0,
    'a',0,'c',0,'e',0,'G',0,'U',0,'I',0,
    'D',0,'s',0,0,0,

    U16_TO_U8S_LE(0x0050),
    '{',0,'D',0,'7',0,'7',0,'6',0,'1',0,'D',0,'5',0,
    '0',0,'-',0,'5',0,'F',0,'1',0,'B',0,'-',0,'4',0,
    'D',0,'3',0,'3',0,'-',0,'9',0,'5',0,'F',0,'2',0,
    '-',0,'7',0,'3',0,'3',0,'B',0,'0',0,'E',0,'5',0,
    'F',0,'2',0,'E',0,'E',0,'D',0,'}',0,0,0,0,0
};

TU_VERIFY_STATIC(sizeof(s_configuration_descriptor) == ACURATEX_USB_CONFIG_TOTAL_LEN,
                 "Invalid Acuratex USB configuration descriptor size");
TU_VERIFY_STATIC(sizeof(s_bos_descriptor) == ACURATEX_USB_BOS_TOTAL_LEN,
                 "Invalid Acuratex USB BOS descriptor size");
TU_VERIFY_STATIC(sizeof(s_ms_os_20_descriptor) == ACURATEX_USB_MS_OS_20_DESC_LEN,
                 "Invalid Acuratex USB Microsoft OS 2.0 descriptor size");

const tusb_desc_device_t* acuratex_usb_get_device_descriptor(void)
{
    return &s_device_descriptor;
}

const uint8_t* acuratex_usb_get_configuration_descriptor(void)
{
    return s_configuration_descriptor;
}

const uint8_t* acuratex_usb_get_bos_descriptor(void)
{
    return s_bos_descriptor;
}

const uint8_t* acuratex_usb_get_ms_os_20_descriptor(void)
{
    return s_ms_os_20_descriptor;
}

const char* const* acuratex_usb_get_string_descriptor_table(void)
{
    return s_string_descriptor;
}

int acuratex_usb_get_string_descriptor_count(void)
{
    return sizeof(s_string_descriptor) / sizeof(s_string_descriptor[0]);
}

void acuratex_usb_fill_serial_string(char* serialBuffer, int serialBufferLen)
{
    uint8_t mac[6] = {0};

    if (serialBuffer == NULL || serialBufferLen < 13)
    {
        return;
    }

    esp_read_mac(mac, ESP_MAC_WIFI_STA);

    snprintf(serialBuffer, serialBufferLen,
             "%02X%02X%02X%02X%02X%02X",
             mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);

    strncpy(s_serial_number, serialBuffer, sizeof(s_serial_number) - 1);
    s_serial_number[sizeof(s_serial_number) - 1] = '\0';
}
