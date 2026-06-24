#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "tusb.h"
#include "class/vendor/vendor_device.h"
#include "acuratex_usb_descriptors.h"
#include "acuratex_usb_bridge.h"

typedef struct
{
    char line[160];
} acuratex_usb_line_t;

static QueueHandle_t s_rx_queue = NULL;
static bool s_usb_mounted = false;

static char s_rx_line[160];
static int s_rx_line_len = 0;

static void acuratex_usb_bridge_submit_line(void)
{
    acuratex_usb_line_t item;

    if (s_rx_line_len <= 0 || s_rx_queue == NULL)
    {
        return;
    }

    s_rx_line[s_rx_line_len] = '\0';

    memset(&item, 0, sizeof(item));
    strncpy(item.line, s_rx_line, sizeof(item.line) - 1);

    xQueueSend(s_rx_queue, &item, 0);

    s_rx_line_len = 0;
    s_rx_line[0] = '\0';
}

bool acuratex_usb_bridge_init(void)
{
    if (s_rx_queue == NULL)
    {
        s_rx_queue = xQueueCreate(8, sizeof(acuratex_usb_line_t));
    }

    s_rx_line_len = 0;
    s_rx_line[0] = '\0';

    return (s_rx_queue != NULL);
}

void acuratex_usb_bridge_set_mounted(bool mounted)
{
    s_usb_mounted = mounted;
}

bool acuratex_usb_bridge_is_mounted(void)
{
    return s_usb_mounted;
}

void acuratex_usb_bridge_reset_rx_line(void)
{
    s_rx_line_len = 0;
    s_rx_line[0] = '\0';
}

void acuratex_usb_bridge_process_rx_bytes(const uint8_t* data, int len)
{
    if (data == NULL || len <= 0)
    {
        return;
    }

    for (int i = 0; i < len; i++)
    {
        char c = (char)data[i];

        if (c == '\r')
        {
            continue;
        }

        if (c == '\n')
        {
            acuratex_usb_bridge_submit_line();
            continue;
        }

        if (s_rx_line_len < (int)(sizeof(s_rx_line) - 1))
        {
            s_rx_line[s_rx_line_len++] = c;
        }
    }
}

int acuratex_usb_bridge_read_line(uint8_t* buffer, int maxLen)
{
    acuratex_usb_line_t item;
    int len;

    if (buffer == NULL || maxLen <= 0 || s_rx_queue == NULL)
    {
        return 0;
    }

    if (xQueueReceive(s_rx_queue, &item, 0) != pdTRUE)
    {
        return 0;
    }

    len = (int)strlen(item.line);
    if (len > maxLen)
    {
        len = maxLen;
    }

    memcpy(buffer, item.line, len);
    return len;
}

bool acuratex_usb_bridge_write_line(const uint8_t* data, int len)
{
    uint32_t retries = 0;
    uint32_t offset = 0;

    if (data == NULL || len <= 0)
    {
        return false;
    }

    if (!s_usb_mounted || !tud_mounted())
    {
        return false;
    }

    while (offset < (uint32_t)len)
    {
        uint32_t available = tud_vendor_n_write_available(ACURATEX_USB_VENDOR_ITF);

        if (available == 0)
        {
            if (retries++ >= 20)
            {
                return false;
            }

            vTaskDelay(pdMS_TO_TICKS(1));
            continue;
        }

        uint32_t remaining = (uint32_t)len - offset;
        uint32_t chunk = (available < remaining) ? available : remaining;

        uint32_t written = tud_vendor_n_write(
            ACURATEX_USB_VENDOR_ITF,
            &data[offset],
            chunk
        );

        if (written == 0)
        {
            if (retries++ >= 20)
            {
                return false;
            }

            vTaskDelay(pdMS_TO_TICKS(1));
            continue;
        }

        offset += written;
        retries = 0;
    }

    tud_vendor_n_write_flush(ACURATEX_USB_VENDOR_ITF);
    return true;
}