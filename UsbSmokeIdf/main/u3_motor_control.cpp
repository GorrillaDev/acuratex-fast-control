#include "u3_motor_control.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#include "can_driver_twai.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "u3_motor_protocol.h"

static const char *TAG = "u3_motors";
static constexpr int U3_CAN_BUS = 2;
static constexpr size_t U3_INIT_ITEM_COUNT = 296U;

enum class U3InitState : uint8_t { Idle, Initializing, Finished, Error };

static SemaphoreHandle_t s_mutex;
static TaskHandle_t s_init_task;
static TaskHandle_t s_sequence_task;
static U3InitState s_init_state = U3InitState::Idle;
static size_t s_init_step;
static bool s_init_cancel;
static char s_init_message[64] = "LISTO";

static int16_t s_s1_positions[3] = {0, 500, 1000};
static char s_s1_sequence[U3_S1_SEQUENCE_MAX + 1U] = "123";
static size_t s_s1_sequence_length = 3U;
static size_t s_s1_sequence_index;
static uint64_t s_s1_period_us = 1000000ULL;
static uint64_t s_s1_next_us;
static bool s_s1_running;

static bool s_s2_right = true;
static uint8_t s_s2_level = 10U;
static bool s_s2_running;

// Copia literal de U3_INIT_SEQ en la referencia Arduino. Cada trama conserva
// su WAIT 200 asociado; no se eliminan duplicados ni se reordena la captura.
static const char *const U3_INIT_SEQUENCE[] = {
  "352 69 08 10 00 7c e7 00 00", "WAIT 200", "352 6f 08 00 00 00 00 00 00", "WAIT 200", "752 6f 09 00 80 00 00 00 00", "WAIT 200",
  "352 09 09 60 69 06 69 69 09", "WAIT 200", "752 60 09 0a 08 00 00 00 00", "WAIT 200", "352 40 09 1a 20 00 20 20 20", "WAIT 200",
  "752 60 09 01 00 00 00 00 00", "WAIT 200", "352 b2 09 3b d2 d2 d2 d2 e9", "WAIT 200", "752 60 09 0a 00 00 00 00 00", "WAIT 200",
  "792 07 00 5b 00 ff 00 01 00", "WAIT 200", "793 07 40 5b 00 ff 00 01 00", "WAIT 200", "792 07 80 6d 00 00 00 00 00", "WAIT 200",
  "792 07 c0 12 00 00 00 00 00", "WAIT 200", "792 07 00 04 00 00 00 00 00", "WAIT 200", "793 07 40 04 00 00 00 00 00", "WAIT 200",
  "352 9a 08 02 f3 f9 f3 f3 f1", "WAIT 200", "352 de 08 02 b7 85 b7 b7 b5", "WAIT 200", "352 96 08 02 ff f5 ff ff fd", "WAIT 200",
  "352 ac 08 02 c5 f7 c5 c5 c7", "WAIT 200", "352 d2 08 02 bb b1 bb bb b9", "WAIT 200", "352 c3 08 a8 aa 98 aa 00 aa", "WAIT 200",
  "792 07 80 32 00 3f 00 00 00", "WAIT 200", "793 07 c0 32 00 3f 00 00 00", "WAIT 200", "792 07 00 3a 00 06 00 00 00", "WAIT 200",
  "793 07 40 3a 00 06 00 00 00", "WAIT 200", "792 07 80 39 00 08 00 00 00", "WAIT 200", "793 07 c0 39 00 08 00 00 00", "WAIT 200",
  "352 a7 08 60 c7 38 38 c7 a7", "WAIT 200", "752 60 09 0a 08 00 00 00 00", "WAIT 200", "352 5f 08 2e 3f 07 57 3f 68", "WAIT 200",
  "752 60 09 00 01 00 00 00 00", "WAIT 200", "352 77 08 60 17 17 17 17 77", "WAIT 200", "752 60 09 0a 08 00 00 00 00", "WAIT 200",
  "352 aa 08 d1 ca ca 00 ca ca", "WAIT 200", "752 60 09 64 00 00 00 00 00", "WAIT 200", "352 2d 09 53 01 43 7f 7f 7e", "WAIT 200",
  "792 07 00 33 00 00 00 00 00", "WAIT 200", "352 da 08 02 b3 b9 b3 b3 b1", "WAIT 200", "352 cb 08 a0 a2 32 a2 a2 90", "WAIT 200",
  "792 07 40 32 00 3f 00 00 00", "WAIT 200", "793 07 80 32 00 3f 00 00 00", "WAIT 200", "792 0f c0 0e 00 52 00 00 00", "WAIT 200",
  "792 0f c1 00 00 00 00 00 00", "WAIT 200", "792 0d c2 00 00 00 00 00 00", "WAIT 200", "1 e6 01 00 00 38 de 14 80", "WAIT 200",
  "1 02 01 05 80 01 00 14 00", "WAIT 200", "1 02 01 03 80 25 00 00 00", "WAIT 200", "1 02 01 04 80 20 4e 00 00", "WAIT 200",
  "1 02 01 01 80 20 4e 00 00", "WAIT 200", "1 07 01 01 00 00 00 00 00", "WAIT 200", "1 12 01 03 00 00 00 00 00", "WAIT 200",
  "1 13 01 7f 00 0a 00 00 00", "WAIT 200", "1 71 01 00 00 00 00 00 00", "WAIT 200", "1 07 02 01 00 00 00 00 00", "WAIT 200",
  "1 13 02 7f 00 0a 00 00 00", "WAIT 200", "1 71 02 00 00 00 00 00 00", "WAIT 200", "1 07 a1 01 00 00 00 00 00", "WAIT 200",
  "1 02 a1 02 80 00 00 01 00", "WAIT 200", "1 07 b1 01 00 00 00 00 00", "WAIT 200", "1 02 b1 02 80 00 00 00 00", "WAIT 200",
  "792 07 00 35 00 01 00 00 00", "WAIT 200", "793 07 40 35 00 01 00 00 00", "WAIT 200", "352 eb 08 80 82 88 82 00 82", "WAIT 200",
  "352 f6 08 02 9f ad 9f 9f 9d", "WAIT 200", "352 a8 08 02 c1 cb c1 c1 c3", "WAIT 200", "352 58 08 02 31 03 31 31 33", "WAIT 200",
  "352 9d 08 f6 00 fe f4 f4 f4", "WAIT 200", "352 c1 08 aa a8 32 a8 a8 9a", "WAIT 200", "352 d2 08 02 bb b1 bb bb b9", "WAIT 200",
  "352 88 08 02 e1 d3 e1 e1 e3", "WAIT 200", "352 3d 08 56 00 5e 54 54 54", "WAIT 200", "352 b5 08 de 00 ee dc dc dc", "WAIT 200",
  "352 43 08 28 2a 20 2a 00 2a", "WAIT 200", "352 e1 08 8a 88 ba 00 88 88", "WAIT 200", "352 0f 08 64 00 6c 66 66 66", "WAIT 200",
  "352 7b 08 10 12 32 12 12 20", "WAIT 200", "352 0e 08 02 67 6d 67 67 65", "WAIT 200", "352 6e 08 02 07 35 07 07 05", "WAIT 200",
  "352 6e 08 02 07 0d 07 07 05", "WAIT 200", "352 a3 08 c8 ca f8 00 ca ca", "WAIT 200", "352 31 08 5a 58 0a 58 58 52", "WAIT 200",
  "352 0e 08 02 67 55 67 67 65", "WAIT 200", "352 82 08 02 eb e1 eb eb e9", "WAIT 200", "352 0f 08 64 00 54 66 66 66", "WAIT 200",
  "352 29 08 42 40 4a 00 40 40", "WAIT 200", "352 c7 08 ac 00 9c ae ae ae", "WAIT 200", "352 1a 08 02 73 79 73 73 71", "WAIT 200",
  "352 8b 08 e0 e2 32 e2 e2 d0", "WAIT 200", "352 df 08 b4 00 bc b6 b6 b6", "WAIT 200", "352 84 08 02 ed df ed ed ef", "WAIT 200",
  "352 d2 08 02 bb b1 bb bb b9", "WAIT 200", "352 b0 08 02 d9 eb d9 d9 db", "WAIT 200", "352 0c 08 02 65 6f 65 65 67", "WAIT 200",
  "352 ad 08 c6 00 f6 c4 c4 c4", "WAIT 200", "352 81 08 ea e8 e2 00 e8 e8", "WAIT 200", "352 da 08 02 b3 81 b3 b3 b1", "WAIT 200",
  "352 8f 08 e4 00 ec e6 e6 e6", "WAIT 200", "352 fb 08 90 92 32 92 92 a0", "WAIT 200", "352 21 08 4a 48 42 00 48 48", "WAIT 200",
  "352 90 08 02 f9 cb f9 f9 fb", "WAIT 200", "352 7b 08 10 12 0a 12 12 18", "WAIT 200", "352 a2 08 02 cb f9 cb cb c9", "WAIT 200",
  "352 79 08 12 10 0a 10 10 1a", "WAIT 200", "352 a9 08 c2 c0 f2 00 c0 c0", "WAIT 200", "352 05 08 6e 00 66 6c 6c 6c", "WAIT 200",
  "352 4b 08 20 22 10 22 00 22", "WAIT 200", "1 08 01 00 00 00 00 00 00", "WAIT 200", "261 10 02 00 00 09 00", "WAIT 200",
  "262 10 02 00 00 00 00", "WAIT 200", "261 10 02 00 01 08 00", "WAIT 200", "262 10 02 00 01 01 00", "WAIT 200",
  "261 10 02 00 02 07 00", "WAIT 200", "262 10 02 00 02 02 00", "WAIT 200", "261 10 02 00 03 06 00", "WAIT 200",
  "262 10 02 00 03 03 00", "WAIT 200", "261 10 02 00 04 05 00", "WAIT 200", "262 10 02 00 04 04 00", "WAIT 200",
  "261 10 02 00 05 04 00", "WAIT 200", "262 10 02 00 05 05 00", "WAIT 200", "261 10 02 00 06 03 00", "WAIT 200",
  "262 10 02 00 06 06 00", "WAIT 200", "261 10 02 00 07 02 00", "WAIT 200", "262 10 02 00 07 07 00", "WAIT 200",
  "261 10 02 00 08 01 00", "WAIT 200", "262 10 02 00 08 08 00", "WAIT 200", "261 10 02 00 09 00 00", "WAIT 200",
  "262 10 02 00 09 09 00", "WAIT 200", "352 2d 08 2d 4d 5d 8d 4d c0", "WAIT 200", "752 60 09 0a 08 00 00 00 00", "WAIT 200",
  "352 92 08 11 f2 ca a5 f2 e3", "WAIT 200", "752 60 09 00 01 00 00 00 00", "WAIT 200", "352 49 08 60 29 29 29 29 49", "WAIT 200",
  "752 60 09 0a 08 00 00 00 00", "WAIT 200", "352 0c 08 77 00 6c 6c 6c 6c", "WAIT 200", "752 60 09 64 00 00 00 00 00", "WAIT 200",
  "792 07 80 90 00 00 00 00 00", "WAIT 200", "352 27 01 4f 81 67 20 37 b6", "WAIT 200", "352 62 01 76 12 00 12 12 12", "WAIT 200",
  "352 0f 08 00 63 63 63 63 63", "WAIT 200", "752 13 01 01 00 00 00 00 00", "WAIT 200", "352 11 08 00 7d 7d 7d 7d 7d", "WAIT 200",
  "752 13 01 01 00 00 00 00 00", "WAIT 200", "352 94 08 ff 07 07 07 f8 07", "WAIT 200", "752 13 01 01 00 00 00 00 00", "WAIT 200",
  "352 b6 08 ff 25 25 25 da 25", "WAIT 200", "752 13 01 01 00 00 00 00 00", "WAIT 200", "352 d8 08 ff 4b 4b 4b b4 4b", "WAIT 200",
  "752 13 01 01 00 00 00 00 00", "WAIT 200",
};

static_assert(sizeof(U3_INIT_SEQUENCE) / sizeof(U3_INIT_SEQUENCE[0]) == U3_INIT_ITEM_COUNT,
              "U3 INIT debe conservar 296 items");

static bool lock_state(TickType_t timeout = portMAX_DELAY)
{
    return s_mutex != nullptr && xSemaphoreTake(s_mutex, timeout) == pdTRUE;
}

static void unlock_state()
{
    xSemaphoreGive(s_mutex);
}

static const char *init_state_name(U3InitState state)
{
    switch (state) {
    case U3InitState::Initializing: return "INICIALIZANDO";
    case U3InitState::Finished: return "FINALIZADO";
    case U3InitState::Error: return "ERROR";
    default: return "IDLE";
    }
}

static esp_err_t send_servo_frame(const uint8_t data[8])
{
    return app_can_send_standard(U3_CAN_BUS, U3_SERVO_CAN_ID, data, 8U);
}

static esp_err_t send_s2_run_burst()
{
    uint8_t variable[8] = {};
    bool right;
    uint8_t level;
    if (!lock_state()) return ESP_ERR_INVALID_STATE;
    right = s_s2_right;
    level = s_s2_level;
    unlock_state();

    if (!app_u3_s2_build_speed_frame(level, right, variable)) return ESP_ERR_INVALID_ARG;
    esp_err_t err = send_servo_frame(APP_U3_S2_RUN_FRAME_A);
    if (err == ESP_OK) err = send_servo_frame(variable);
    if (err == ESP_OK) err = send_servo_frame(APP_U3_S2_RUN_FRAME_C);

    if (lock_state()) {
        s_s2_running = err == ESP_OK;
        unlock_state();
    }
    return err;
}

static esp_err_t send_s2_stop()
{
    esp_err_t err = send_servo_frame(APP_U3_S2_STOP_FRAME);
    if (lock_state()) {
        s_s2_running = false;
        unlock_state();
    }
    return err;
}

static bool parse_hex(const char *token, uint32_t max_value, uint32_t *value)
{
    if (token == nullptr || value == nullptr || token[0] == '\0') return false;
    char *end = nullptr;
    unsigned long parsed = strtoul(token, &end, 16);
    if (end == token || *end != '\0' || parsed > max_value) return false;
    *value = static_cast<uint32_t>(parsed);
    return true;
}

static esp_err_t send_init_frame(const char *line)
{
    char copy[96];
    strlcpy(copy, line, sizeof(copy));
    char *save = nullptr;
    char *token = strtok_r(copy, " ", &save);
    uint32_t id = 0;
    if (!parse_hex(token, 0x7FFU, &id)) return ESP_ERR_INVALID_ARG;

    uint8_t data[8] = {};
    size_t dlc = 0;
    while ((token = strtok_r(nullptr, " ", &save)) != nullptr) {
        uint32_t byte = 0;
        if (dlc >= 8U || !parse_hex(token, 0xFFU, &byte)) return ESP_ERR_INVALID_ARG;
        data[dlc++] = static_cast<uint8_t>(byte);
    }
    return app_can_send_standard(U3_CAN_BUS, id, data, dlc);
}

static bool delay_init_cancelable(uint32_t milliseconds)
{
    for (uint32_t elapsed = 0; elapsed < milliseconds; elapsed += 10U) {
        uint32_t slice = milliseconds - elapsed;
        if (slice > 10U) slice = 10U;
        vTaskDelay(pdMS_TO_TICKS(slice));
        if (lock_state()) {
            const bool cancel = s_init_cancel;
            unlock_state();
            if (cancel) return false;
        }
    }
    return true;
}

static void init_task(void *)
{
    while (true) {
        ulTaskNotifyTake(pdTRUE, portMAX_DELAY);

        bool stop_s2 = false;
        if (lock_state()) {
            s_s1_running = false;
            s_s1_sequence_index = 0U;
            stop_s2 = s_s2_running;
            unlock_state();
        }
        if (stop_s2 && send_s2_stop() != ESP_OK) {
            if (lock_state()) {
                s_init_state = U3InitState::Error;
                strlcpy(s_init_message, "ERROR STOP SERVO CAN 2", sizeof(s_init_message));
                unlock_state();
            }
            continue;
        }

        bool ok = true;
        for (size_t i = 0; i < U3_INIT_ITEM_COUNT; ++i) {
            if (lock_state()) {
                if (s_init_cancel) ok = false;
                s_init_step = i + 1U;
                unlock_state();
            }
            if (!ok) break;

            const char *item = U3_INIT_SEQUENCE[i];
            if (strncasecmp(item, "WAIT ", 5U) == 0) {
                const uint32_t wait_ms = static_cast<uint32_t>(strtoul(item + 5U, nullptr, 10));
                ok = delay_init_cancelable(wait_ms);
            } else {
                ok = send_init_frame(item) == ESP_OK;
            }
        }

        if (lock_state()) {
            s_init_state = ok ? U3InitState::Finished : U3InitState::Error;
            strlcpy(s_init_message, ok ? "SECUENCIA COMPLETA" : "CANCELADO O ERROR TX",
                    sizeof(s_init_message));
            unlock_state();
        }
        ESP_LOGI(TAG, "U3_INIT_RESULT|STATE=%s|STEP=%u/%u",
                 ok ? "FINALIZADO" : "ERROR", (unsigned)s_init_step,
                 (unsigned)U3_INIT_ITEM_COUNT);
    }
}

static void sequence_task(void *)
{
    while (true) {
        int16_t target = 0;
        bool transmit = false;
        const uint64_t now = static_cast<uint64_t>(esp_timer_get_time());
        if (lock_state()) {
            if (s_s1_running && s_init_state != U3InitState::Initializing && now >= s_s1_next_us) {
                const char item = s_s1_sequence[s_s1_sequence_index];
                target = s_s1_positions[item - '1'];
                s_s1_sequence_index = (s_s1_sequence_index + 1U) % s_s1_sequence_length;
                s_s1_next_us = now + s_s1_period_us;
                transmit = true;
            }
            unlock_state();
        }

        if (transmit) {
            uint8_t frame[8] = {};
            if (!app_u3_s1_build_position_frame(target, frame) || send_servo_frame(frame) != ESP_OK) {
                if (lock_state()) {
                    s_s1_running = false;
                    unlock_state();
                }
            }
        }
        vTaskDelay(pdMS_TO_TICKS(1));
    }
}

static bool parse_named_long(const char *token, const char *name, long min, long max, long *value)
{
    const size_t name_length = strlen(name);
    if (strncasecmp(token, name, name_length) != 0 || token[name_length] != '=') return false;
    char *end = nullptr;
    long parsed = strtol(token + name_length + 1U, &end, 10);
    if (end == token + name_length + 1U || *end != '\0' || parsed < min || parsed > max) return false;
    *value = parsed;
    return true;
}

static esp_err_t configure_s1(const char *line)
{
    char copy[192];
    strlcpy(copy, line, sizeof(copy));
    char *save = nullptr;
    (void)strtok_r(copy, "|", &save);
    long positions[3] = {};
    bool have_positions[3] = {};
    char sequence[U3_S1_SEQUENCE_MAX + 2U] = {};
    double hz = 0.0;
    bool have_sequence = false;
    bool have_hz = false;

    for (char *token = strtok_r(nullptr, "|", &save); token != nullptr;
         token = strtok_r(nullptr, "|", &save)) {
        long value = 0;
        if (parse_named_long(token, "P1", -32768, 32767, &value)) {
            positions[0] = value; have_positions[0] = true;
        } else if (parse_named_long(token, "P2", -32768, 32767, &value)) {
            positions[1] = value; have_positions[1] = true;
        } else if (parse_named_long(token, "P3", -32768, 32767, &value)) {
            positions[2] = value; have_positions[2] = true;
        } else if (strncasecmp(token, "SEQ=", 4U) == 0) {
            strlcpy(sequence, token + 4U, sizeof(sequence)); have_sequence = true;
        } else if (strncasecmp(token, "HZ=", 3U) == 0) {
            char *end = nullptr;
            hz = strtod(token + 3U, &end);
            have_hz = end != token + 3U && *end == '\0' && isfinite(hz) && hz > 0.0 && hz <= 1000.0;
        } else {
            return ESP_ERR_INVALID_ARG;
        }
    }

    if (!have_positions[0] || !have_positions[1] || !have_positions[2]
        || !have_sequence || !have_hz || !app_u3_s1_validate_sequence(sequence)) {
        return ESP_ERR_INVALID_ARG;
    }

    if (!lock_state()) return ESP_ERR_INVALID_STATE;
    if (s_init_state == U3InitState::Initializing) {
        unlock_state();
        return ESP_ERR_INVALID_STATE;
    }
    for (size_t i = 0; i < 3U; ++i) s_s1_positions[i] = static_cast<int16_t>(positions[i]);
    strlcpy(s_s1_sequence, sequence, sizeof(s_s1_sequence));
    s_s1_sequence_length = strlen(sequence);
    s_s1_sequence_index = 0U;
    s_s1_period_us = static_cast<uint64_t>((1000000.0 / hz) + 0.5);
    if (s_s1_period_us < 1000ULL) s_s1_period_us = 1000ULL;
    s_s1_next_us = static_cast<uint64_t>(esp_timer_get_time());
    s_s1_running = true;
    unlock_state();
    return ESP_OK;
}

static void format_status(char *response, size_t response_size)
{
    if (!lock_state()) {
        snprintf(response, response_size, "ERR|U3_STATUS|LOCK");
        return;
    }
    snprintf(response, response_size,
             "U3_STATE|INIT=%s|STEP=%u/%u|S1=%s|S2=%s|DIR=%s|LEVEL=%u|MSG=%s",
             init_state_name(s_init_state), (unsigned)s_init_step,
             (unsigned)U3_INIT_ITEM_COUNT, s_s1_running ? "RUN" : "STOP",
             s_s2_running ? "RUN" : "STOP", s_s2_right ? "RIGHT" : "LEFT",
             (unsigned)s_s2_level, s_init_message);
    unlock_state();
}

esp_err_t app_u3_motor_init(void)
{
    if (s_mutex == nullptr) s_mutex = xSemaphoreCreateMutex();
    if (s_mutex == nullptr) return ESP_ERR_NO_MEM;
    if (s_init_task == nullptr
        && xTaskCreatePinnedToCore(init_task, "u3_init", 4096, nullptr, 5, &s_init_task, 1) != pdPASS) {
        return ESP_ERR_NO_MEM;
    }
    if (s_sequence_task == nullptr
        && xTaskCreatePinnedToCore(sequence_task, "u3_s1_seq", 3072, nullptr, 5, &s_sequence_task, 1) != pdPASS) {
        return ESP_ERR_NO_MEM;
    }
    return ESP_OK;
}

bool app_u3_motor_is_command(const char *line)
{
    return line != nullptr && strncasecmp(line, "U3_", 3U) == 0;
}

esp_err_t app_u3_motor_process_line(const char *line, char *response, size_t response_size)
{
    if (line == nullptr || response == nullptr || response_size == 0U) return ESP_ERR_INVALID_ARG;
    if (strcasecmp(line, "U3_STATUS") == 0) {
        format_status(response, response_size);
        return ESP_OK;
    }

    bool init_running = false;
    if (lock_state()) {
        init_running = s_init_state == U3InitState::Initializing;
        unlock_state();
    }
    if (init_running && strcasecmp(line, "U3_S1_STOP") != 0
        && strcasecmp(line, "U3_S2_STOP") != 0) {
        snprintf(response, response_size, "ERR|U3|BUSY_INIT");
        return ESP_ERR_INVALID_STATE;
    }

    esp_err_t err = ESP_OK;
    if (strcasecmp(line, "U3_INIT") == 0) {
        if (!lock_state()) err = ESP_ERR_INVALID_STATE;
        else {
            s_init_state = U3InitState::Initializing;
            s_init_step = 0U;
            s_init_cancel = false;
            strlcpy(s_init_message, "INICIALIZANDO", sizeof(s_init_message));
            s_s1_running = false;
            unlock_state();
            xTaskNotifyGive(s_init_task);
        }
    } else if (strncasecmp(line, "U3_S1_CONFIG|", 13U) == 0) {
        err = configure_s1(line);
    } else if (strcasecmp(line, "U3_S1_STOP") == 0) {
        if (lock_state()) { s_s1_running = false; s_s1_sequence_index = 0U; unlock_state(); }
    } else if (strcasecmp(line, "U3_S2_DIR|RIGHT") == 0 || strcasecmp(line, "U3_S2_DIR|LEFT") == 0) {
        const bool right = strcasecmp(line + 10U, "RIGHT") == 0;
        bool resend = false;
        if (lock_state()) { s_s2_right = right; resend = s_s2_running; unlock_state(); }
        if (resend) err = send_s2_run_burst();
    } else if (strncasecmp(line, "U3_S2_SPEED|LEVEL=", 18U) == 0) {
        char *end = nullptr;
        long level = strtol(line + 18U, &end, 10);
        if (end == line + 18U || *end != '\0' || level < 1L || level > 30L) err = ESP_ERR_INVALID_ARG;
        else {
            bool resend = false;
            if (lock_state()) { s_s2_level = static_cast<uint8_t>(level); resend = s_s2_running; unlock_state(); }
            if (resend) err = send_s2_run_burst();
        }
    } else if (strcasecmp(line, "U3_S2_RUN") == 0) {
        err = send_s2_run_burst();
    } else if (strcasecmp(line, "U3_S2_STOP") == 0) {
        err = send_s2_stop();
    } else {
        err = ESP_ERR_NOT_SUPPORTED;
    }

    if (err == ESP_OK) {
        snprintf(response, response_size, "OK|%s", line);
    } else {
        snprintf(response, response_size, "ERR|U3|%s", esp_err_to_name(err));
    }
    return err;
}

void app_u3_motor_emergency_stop(void)
{
    bool stop_s2 = false;
    if (lock_state()) {
        s_init_cancel = true;
        s_s1_running = false;
        s_s1_sequence_index = 0U;
        stop_s2 = s_s2_running;
        unlock_state();
    }
    if (stop_s2) (void)send_s2_stop();
}
