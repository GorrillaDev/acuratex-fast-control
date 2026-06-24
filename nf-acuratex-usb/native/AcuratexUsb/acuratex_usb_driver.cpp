#include <string.h>
#include "esp_log.h"
#include "tinyusb.h"
#include "tinyusb_default_config.h"
#include "tusb.h"
#include "class/vendor/vendor_device.h"

#include "acuratex_usb_descriptors.h"
#include "acuratex_usb_bridge.h"
#include "acuratex_usb_driver.h"

static const char *TAG = "acuratex_usb";
static char s_serial[17] = {0};

static void acuratex_usb_event_cb(tinyusb_event_t *event, void *arg)
{
    (void)arg;

    if (event == NULL)
    {
        return;
    }

    switch (event->id)
    {
        case TINYUSB_EVENT_ATTACHED:
            acuratex_usb_bridge_set_mounted(true);
            ESP_LOGI(TAG, "USB mounted");
            break;

        case TINYUSB_EVENT_DETACHED:
            acuratex_usb_bridge_set_mounted(false);
            acuratex_usb_bridge_reset_rx_line();
            ESP_LOGI(TAG, "USB unmounted");
            break;

        default:
            break;
    }
}

bool acuratex_usb_driver_init(void)
{
    tinyusb_config_t tusb_cfg = TINYUSB_DEFAULT_CONFIG(acuratex_usb_event_cb);

    if (!acuratex_usb_bridge_init())
    {
        ESP_LOGE(TAG, "acuratex_usb_bridge_init fallo");
        return false;
    }

    acuratex_usb_bridge_set_mounted(false);
    acuratex_usb_bridge_reset_rx_line();

    acuratex_usb_fill_serial_string(s_serial, sizeof(s_serial));

    tusb_cfg.descriptor.device = acuratex_usb_get_device_descriptor();
    tusb_cfg.descriptor.full_speed_config = acuratex_usb_get_configuration_descriptor();
    tusb_cfg.descriptor.string = (const char **)acuratex_usb_get_string_descriptor_table();
    tusb_cfg.descriptor.string_count = acuratex_usb_get_string_descriptor_count();

#if (TUD_OPT_HIGH_SPEED)
    tusb_cfg.descriptor.high_speed_config = acuratex_usb_get_configuration_descriptor();
#endif

    if (tinyusb_driver_install(&tusb_cfg) != ESP_OK)
    {
        ESP_LOGE(TAG, "tinyusb_driver_install fallo");
        return false;
    }

    ESP_LOGI(TAG,
             "USB custom iniciado VID=0x%04X PID=0x%04X GUID=%s",
             ACURATEX_USB_VENDOR_ID,
             ACURATEX_USB_PRODUCT_ID,
             ACURATEX_USB_INTERFACE_GUID);

    return true;
}

extern "C" const uint8_t *tud_descriptor_bos_cb(void)
{
    return acuratex_usb_get_bos_descriptor();
}

/* ===========================================
   Microsoft OS 2.0 descriptor vendor request
   =========================================== */

extern "C" bool tud_vendor_control_xfer_cb(uint8_t rhport,
                                           uint8_t stage,
                                           tusb_control_request_t const *request)
{
    if (stage != CONTROL_STAGE_SETUP)
    {
        return true;
    }

    if (request->bmRequestType_bit.type == TUSB_REQ_TYPE_VENDOR &&
        request->bRequest == ACURATEX_USB_VENDOR_REQUEST_MICROSOFT &&
        request->wIndex == 7)
    {
        return tud_control_xfer(
            rhport,
            request,
            (void *)(uintptr_t)acuratex_usb_get_ms_os_20_descriptor(),
            ACURATEX_USB_MS_OS_20_DESC_LEN);
    }

    return false;
}

/* ============================
   Vendor RX callback
   ============================ */

extern "C" void tud_vendor_rx_cb(uint8_t itf, const uint8_t *buffer, uint16_t bufsize)
{
    uint8_t tmp[64];

    if (itf != ACURATEX_USB_VENDOR_ITF)
    {
        return;
    }

    if (buffer != NULL && bufsize > 0)
    {
        acuratex_usb_bridge_process_rx_bytes(buffer, (int)bufsize);
    }

    while (true)
    {
        uint32_t available = tud_vendor_n_available(itf);
        if (available == 0)
        {
            break;
        }

        uint32_t to_read = available;
        if (to_read > sizeof(tmp))
        {
            to_read = sizeof(tmp);
        }

        uint32_t read = tud_vendor_n_read(itf, tmp, to_read);
        if (read == 0)
        {
            break;
        }

        acuratex_usb_bridge_process_rx_bytes(tmp, (int)read);
    }

    tud_vendor_n_read_flush(itf);
}
