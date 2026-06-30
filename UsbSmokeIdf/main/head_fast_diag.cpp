#include "head_fast_diag.h"

#include <stdarg.h>
#include <ctype.h>
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#define CONFIG_TWAI_SUPPRESS_DEPRECATE_WARN 1
#include "driver/twai.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"

#include "can_driver_twai.h"
#include "head_state_manager.h"
#include "head_program_runtime.h"
#include "line_codec.h"
#include "reply_dispatcher.h"

static const char *TAG = "head_fast_diag";

typedef enum
{
    APP_HEAD_FAST_DIAG_NONE = 0,
    APP_HEAD_FAST_DIAG_TESTEO,
    APP_HEAD_FAST_DIAG_INIT,
} app_head_fast_diag_kind_t;

typedef struct
{
    bool running;
    bool cancel_requested;
    bool armed;
    bool latched;
    bool waiting;
    uint32_t run_id;
    uint32_t next_ping_ms;
    uint32_t wait_deadline_ms;
    uint32_t last_reset_ms;
    uint16_t tries;
    uint16_t max_tries;
    bool got_first;
    uint8_t first_code;
    uint8_t last_code;
    uint16_t a2_cnt;
    uint16_t a1_cnt;
    int can_bus;
    app_reply_route_t *route;
    app_reply_ctx_release_fn_t route_release;
    TaskHandle_t task_handle;
} app_head_testeo_state_t;

typedef struct
{
    bool running;
    bool cancel_requested;
    uint32_t started_ms;
    int can_bus;
    app_reply_route_t *route;
    app_reply_ctx_release_fn_t route_release;
    TaskHandle_t task_handle;
} app_head_init_state_t;

typedef struct
{
    app_head_fast_diag_kind_t kind;
    app_head_testeo_state_t testeo;
    app_head_init_state_t init;
} app_head_fast_diag_state_t;

typedef struct
{
    app_head_fast_diag_kind_t kind;
    app_reply_route_t *route;
    app_reply_ctx_release_fn_t route_release;
    int can_bus;
    const HeadCommandProfile *profile;
} app_head_fast_diag_task_args_t;

static SemaphoreHandle_t s_state_mutex = NULL;
static app_head_fast_diag_state_t s_state;
static uint32_t s_testeo_run_seq = 0;

static bool app_head_fast_diag_ensure_mutex(void)
{
    if (s_state_mutex != NULL) {
        return true;
    }

    s_state_mutex = xSemaphoreCreateMutex();
    if (s_state_mutex == NULL) {
        ESP_LOGE(TAG, "HEAD_FAST_DIAG_MUTEX_CREATE_FAIL");
        return false;
    }

    ESP_LOGI(TAG, "HEAD_FAST_DIAG_MUTEX_READY");
    return true;
}

static bool app_head_fast_diag_take_mutex(TickType_t timeout)
{
    return app_head_fast_diag_ensure_mutex() && xSemaphoreTake(s_state_mutex, timeout) == pdTRUE;
}

static void app_head_fast_diag_give_mutex(void)
{
    if (s_state_mutex != NULL) {
        xSemaphoreGive(s_state_mutex);
    }
}

static uint32_t app_head_fast_diag_now_ms(void)
{
    return (uint32_t)(esp_timer_get_time() / 1000LL);
}

static const char *app_head_fast_diag_bus_name(int bus)
{
    return bus == 2 ? "CAN2" : "CAN1";
}

static void app_head_fast_diag_format_data_hex(const uint8_t *data,
                                               size_t len,
                                               char *out,
                                               size_t out_len)
{
    size_t used = 0;

    if (out == NULL || out_len == 0) {
        return;
    }

    out[0] = '\0';
    if (data == NULL || len == 0) {
        return;
    }

    for (size_t i = 0; i < len && used < out_len; ++i) {
        int written = snprintf(out + used,
                               out_len - used,
                               "%s%02X",
                               i == 0 ? "" : " ",
                               data[i]);
        if (written < 0) {
            out[0] = '\0';
            return;
        }

        if ((size_t)written >= out_len - used) {
            out[out_len - 1] = '\0';
            return;
        }

        used += (size_t)written;
    }
}

static int app_head_fast_diag_normalize_bus(int bus)
{
    return bus == 2 ? 2 : 1;
}

static void app_head_fast_diag_send_line(const app_reply_route_t *route, const char *line)
{
    if (route == NULL || line == NULL || line[0] == '\0') {
        return;
    }

    (void)app_reply_dispatcher_reply(line, (void *)route);
}

static void app_head_fast_diag_send_linef(const app_reply_route_t *route, const char *fmt, ...)
{
    char line[APP_REPLY_MAX_LINE_LENGTH];
    va_list ap;
    int written;

    if (route == NULL || fmt == NULL) {
        return;
    }

    va_start(ap, fmt);
    written = vsnprintf(line, sizeof(line), fmt, ap);
    va_end(ap);

    if (written < 0) {
        return;
    }

    line[sizeof(line) - 1] = '\0';
    app_head_fast_diag_send_line(route, line);
}

static void app_head_fast_diag_release_route(app_head_fast_diag_task_args_t *args)
{
    if (args == NULL) {
        return;
    }

    if (args->route != NULL && args->route_release != NULL) {
        args->route_release(args->route);
    }

    free(args);
}

static bool app_head_fast_diag_should_cancel_locked(app_head_fast_diag_kind_t kind)
{
    if (kind == APP_HEAD_FAST_DIAG_TESTEO) {
        return s_state.testeo.cancel_requested;
    }

    if (kind == APP_HEAD_FAST_DIAG_INIT) {
        return s_state.init.cancel_requested;
    }

    return true;
}

static void app_head_fast_diag_reset_kind_locked(app_head_fast_diag_kind_t kind)
{
    if (kind == APP_HEAD_FAST_DIAG_TESTEO) {
        s_state.testeo.running = false;
        s_state.testeo.cancel_requested = false;
        s_state.testeo.waiting = false;
        s_state.testeo.next_ping_ms = 0;
        s_state.testeo.wait_deadline_ms = 0;
        s_state.testeo.task_handle = NULL;
        return;
    }

    if (kind == APP_HEAD_FAST_DIAG_INIT) {
        s_state.init.running = false;
        s_state.init.cancel_requested = false;
        s_state.init.task_handle = NULL;
        return;
    }
}

static bool app_head_fast_diag_parse_wait_line(const char *line, uint32_t *wait_ms)
{
    const char *text = line;
    char *endptr = NULL;
    long value;

    if (text == NULL || wait_ms == NULL) {
        return false;
    }

    while (*text != '\0' && isspace((unsigned char)*text)) {
        text++;
    }

    if (strncasecmp(text, "WAIT", 4) != 0) {
        return false;
    }

    text += 4;
    while (*text != '\0' && isspace((unsigned char)*text)) {
        text++;
    }

    value = strtol(text, &endptr, 10);
    if (endptr == text || value < 0) {
        return false;
    }

    *wait_ms = (uint32_t)value;
    return true;
}

static bool app_head_fast_diag_delay_cancelable(uint32_t wait_ms, app_head_fast_diag_kind_t kind)
{
    uint32_t start = app_head_fast_diag_now_ms();

    while ((uint32_t)(app_head_fast_diag_now_ms() - start) < wait_ms) {
        if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
            return false;
        }

        bool cancel_requested = app_head_fast_diag_should_cancel_locked(kind);
        app_head_fast_diag_give_mutex();
        if (cancel_requested) {
            return false;
        }

        vTaskDelay(pdMS_TO_TICKS(10));
    }

    return true;
}

static esp_err_t app_head_fast_diag_run_script_line(const char *line, int bus, app_head_fast_diag_kind_t kind)
{
    uint32_t wait_ms = 0;
    uint32_t id = 0;
    uint8_t data[8] = {0};
    size_t len = 0;
    esp_err_t err = ESP_OK;
    char data_hex[32];

    if (line == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    if (app_head_fast_diag_parse_wait_line(line, &wait_ms)) {
        if (kind == APP_HEAD_FAST_DIAG_INIT) {
            ESP_LOGI(TAG, "INIT_STEP|WAIT_MS=%u", (unsigned)wait_ms);
        }
        return app_head_fast_diag_delay_cancelable(wait_ms, kind) ? ESP_OK : ESP_ERR_INVALID_STATE;
    }

    if (!app_parse_frame_line(line, &id, data, &len, 8, 0x7FF)) {
        return ESP_ERR_INVALID_ARG;
    }

    if (kind == APP_HEAD_FAST_DIAG_INIT) {
        app_head_fast_diag_format_data_hex(data, len, data_hex, sizeof(data_hex));
        ESP_LOGI(TAG,
                 "INIT_CAN_BEGIN|ID=0x%03" PRIX32 "|DLC=%u|DATA=%s",
                 id,
                 (unsigned)len,
                 data_hex);
    }

    err = app_can_send_standard(bus, id, data, len);
    if (kind == APP_HEAD_FAST_DIAG_INIT) {
        if (err == ESP_OK) {
            ESP_LOGI(TAG, "INIT_CAN_OK|ID=0x%03" PRIX32, id);
        } else {
            ESP_LOGE(TAG,
                     "INIT_CAN_ERROR|ERR=%s|ID=0x%03" PRIX32,
                     esp_err_to_name(err),
                     id);
        }
    }

    return err;
}

static bool app_head_fast_diag_clone_route(const app_command_env_t *env,
                                           void *reply_ctx,
                                           app_reply_route_t **route_out,
                                           app_reply_ctx_release_fn_t *release_out)
{
    app_reply_route_t *route = NULL;

    if (route_out == NULL || release_out == NULL || env == NULL || reply_ctx == NULL) {
        return false;
    }

    if (env->reply_ctx_clone == NULL || env->reply_ctx_release == NULL) {
        return false;
    }

    route = (app_reply_route_t *)env->reply_ctx_clone(reply_ctx);
    if (route == NULL) {
        return false;
    }

    *route_out = route;
    *release_out = env->reply_ctx_release;
    return true;
}

static bool app_head_fast_diag_testeo_finish_locked(app_head_testeo_state_t *state,
                                                    const app_reply_route_t *route,
                                                    const char *line)
{
    if (state == NULL || route == NULL || line == NULL) {
        return false;
    }

    state->running = false;
    state->cancel_requested = false;
    state->waiting = false;
    state->next_ping_ms = 0;
    state->wait_deadline_ms = 0;
    state->armed = false;
    state->latched = true;
    app_head_fast_diag_send_line(route, line);
    return true;
}

static void app_head_fast_diag_testeo_on_rx(app_head_testeo_state_t *state,
                                             const app_can_rx_event_t *event,
                                             const app_reply_route_t *route,
                                             const HeadTesteoCommandProfile *commands)
{
    uint32_t now_ms = 0;
    char line[160];
    char reset_data_hex[32];

    if (state == NULL || event == NULL || route == NULL || commands == NULL) {
        return;
    }

    now_ms = event->time_ms;

    if (commands->reset_dlc > 0
        && event->id == commands->reset_can_id
        && event->dlc >= commands->reset_dlc
        && memcmp(event->data, commands->reset_data, commands->reset_dlc) == 0) {
        if ((uint32_t)(now_ms - state->last_reset_ms) < commands->reset_debounce_ms) {
            return;
        }

        state->last_reset_ms = now_ms;
        state->running = false;
        state->waiting = false;
        state->armed = true;
        state->latched = false;
        state->next_ping_ms = 0;
        state->wait_deadline_ms = 0;
        state->tries = 0;
        state->got_first = false;
        state->first_code = 0;
        state->last_code = 0;
        state->a1_cnt = 0;
        state->a2_cnt = 0;
        app_head_fast_diag_format_data_hex(commands->reset_data,
                                           commands->reset_dlc,
                                           reset_data_hex,
                                           sizeof(reset_data_hex));
        snprintf(line,
                 sizeof(line),
                 "TESTEO|REARMED|%03" PRIX32 " %s|TIME=%u",
                 commands->reset_can_id,
                 reset_data_hex,
                 (unsigned)now_ms);
        app_head_fast_diag_send_line(route, line);
        return;
    }

    if (event->id != commands->response_can_id || event->dlc < 1) {
        return;
    }

    state->last_code = event->data[0];
    if (!state->got_first) {
        state->got_first = true;
        state->first_code = state->last_code;
    }

    if (state->last_code == commands->force_board_2_code) {
        state->a2_cnt++;
    }
    if (state->last_code == commands->force_board_1_code) {
        state->a1_cnt++;
    }

    if (state->last_code == commands->success_code) {
        app_head_fast_diag_testeo_finish_locked(state, route, "TESTEO|DONE|OK: placas del cabezal presentes (CB)");
        return;
    }

    if (state->last_code == commands->missing_expansion_code) {
        app_head_fast_diag_testeo_finish_locked(state, route, "TESTEO|DONE|FALTA: placa 3 de expansion (BC)");
        return;
    }

    if (state->last_code == commands->missing_force_code) {
        if (state->first_code == commands->force_board_2_code) {
            if (state->a2_cnt <= 1) {
                app_head_fast_diag_testeo_finish_locked(state, route, "TESTEO|DONE|FALTA: ambas placas de fuerza (A2->BF)");
            } else {
                app_head_fast_diag_testeo_finish_locked(state, route, "TESTEO|DONE|FALTA: placa 2 (A2 repetido -> BF)");
            }
        } else if (state->first_code == commands->force_board_1_code) {
            if (state->a1_cnt <= 1) {
                app_head_fast_diag_testeo_finish_locked(state, route, "TESTEO|DONE|FALTA: ambas placas de fuerza (A1->BF)");
            } else {
                app_head_fast_diag_testeo_finish_locked(state, route, "TESTEO|DONE|FALTA: placa 1 (A1 repetido -> BF)");
            }
        } else {
            app_head_fast_diag_testeo_finish_locked(state, route, "TESTEO|DONE|BF: faltan placas (patron no clasificado)");
        }
        return;
    }

    state->waiting = false;
    state->next_ping_ms = now_ms + commands->retry_delay_ms;
    snprintf(line,
             sizeof(line),
             "TESTEO|RX%03" PRIX32 "|CODE=0x%02X|TRY=%u",
             commands->response_can_id,
             state->last_code,
             (unsigned)state->tries);
    app_head_fast_diag_send_line(route, line);
}

static void app_head_fast_diag_testeo_task(void *arg)
{
    app_head_fast_diag_task_args_t *task_args = (app_head_fast_diag_task_args_t *)arg;
    app_head_testeo_state_t *state = NULL;
    const app_reply_route_t *route = NULL;
    const HeadCommandProfile *profile = task_args != NULL ? task_args->profile : NULL;
    const HeadTesteoCommandProfile *commands = profile != NULL ? &profile->testeo : NULL;
    char ping_data_hex[32];
    int bus = 1;

    if (task_args == NULL || commands == NULL || commands->ping.dlc > APP_HEAD_PROFILE_MAX_DLC) {
        app_head_fast_diag_release_route(task_args);
        vTaskDelete(NULL);
        return;
    }

    if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        app_head_fast_diag_release_route(task_args);
        vTaskDelete(NULL);
        return;
    }

    state = &s_state.testeo;
    route = task_args->route;
    bus = task_args->can_bus;
    state->task_handle = xTaskGetCurrentTaskHandle();
    state->running = true;
    state->cancel_requested = false;
    state->waiting = false;
    state->next_ping_ms = app_head_fast_diag_now_ms();
    state->wait_deadline_ms = 0;
    state->tries = 0;
    state->max_tries = commands->max_tries;
    state->got_first = false;
    state->first_code = 0;
    state->last_code = 0;
    state->a1_cnt = 0;
    state->a2_cnt = 0;
    state->armed = true;
    state->latched = false;
    state->can_bus = bus;
    state->route = task_args->route;
    state->route_release = task_args->route_release;
    app_can_rx_clear();
    app_head_fast_diag_give_mutex();

    s_testeo_run_seq++;
    app_head_fast_diag_format_data_hex(commands->ping.data,
                                       commands->ping.dlc,
                                       ping_data_hex,
                                       sizeof(ping_data_hex));
    app_head_fast_diag_send_linef(route,
                                  "PROFILE_COMMAND|PROGRAM=%u|MODULE=TESTEO|STEP=1",
                                  (unsigned)(profile != NULL ? profile->program_id : APP_HEAD_PROGRAM_1));
    app_head_fast_diag_send_linef(route,
                                  "TESTEO|START|RUN_ID=%u|BUS=%s|PING=%03" PRIX32 " %s",
                                  (unsigned)s_testeo_run_seq,
                                  app_head_fast_diag_bus_name(bus),
                                  commands->ping.can_id,
                                  ping_data_hex);

    while (true) {
        bool cancel_requested = false;
        bool waiting = false;
        uint32_t now_ms = app_head_fast_diag_now_ms();
        app_can_rx_event_t rx_event = {};
        esp_err_t rx_err;

        if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
            break;
        }

        cancel_requested = app_head_fast_diag_should_cancel_locked(APP_HEAD_FAST_DIAG_TESTEO);
        waiting = s_state.testeo.waiting;
        if (cancel_requested) {
            s_state.testeo.running = false;
        }
        app_head_fast_diag_give_mutex();

        if (cancel_requested) {
            app_head_fast_diag_send_line(route, "TESTEO|CANCELLED");
            break;
        }

        if (!waiting) {
            if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
                break;
            }

            if (!s_state.testeo.running) {
                app_head_fast_diag_give_mutex();
                break;
            }

            if ((int32_t)(now_ms - s_state.testeo.next_ping_ms) < 0) {
                app_head_fast_diag_give_mutex();
                vTaskDelay(pdMS_TO_TICKS(10));
                continue;
            }

            if (s_state.testeo.tries >= s_state.testeo.max_tries) {
                const char *result_line = NULL;
                if (s_state.testeo.a2_cnt != 0 && s_state.testeo.a1_cnt != 0) {
                    result_line = "TESTEO|DONE|INCONCLUSO: A1/A2 sin BC/BF.";
                } else if (s_state.testeo.a2_cnt != 0 && s_state.testeo.a1_cnt == 0) {
                    result_line = "TESTEO|DONE|INCONCLUSO: A2 repetido sin BF/BC.";
                } else {
                    result_line = "TESTEO|DONE|INCONCLUSO: sin patron valido.";
                }

                s_state.testeo.running = false;
                s_state.testeo.waiting = false;
                s_state.testeo.latched = true;
                s_state.testeo.armed = false;
                app_head_fast_diag_give_mutex();
                app_head_fast_diag_send_line(route, result_line);
                break;
            }

            s_state.testeo.waiting = true;
            s_state.testeo.wait_deadline_ms = now_ms + commands->response_timeout_ms;
            s_state.testeo.tries++;
            if (FAST_PERF_LOG && s_state.testeo.tries == 1) {
                ESP_LOGI(TAG, "[FAST] TESTEO first ping queued");
            }
            app_head_fast_diag_give_mutex();

            if (app_can_send_standard(bus,
                                      commands->ping.can_id,
                                      commands->ping.data,
                                      commands->ping.dlc) != ESP_OK) {
                if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
                    s_state.testeo.running = false;
                    s_state.testeo.waiting = false;
                    s_state.testeo.latched = true;
                    s_state.testeo.armed = false;
                    app_head_fast_diag_give_mutex();
                }
                app_head_fast_diag_send_line(route, "TESTEO|ERROR|CAN_TX_FAIL");
                break;
            }

            app_head_fast_diag_send_linef(route,
                                          "TESTEO|PING|TRY=%u|DEADLINE=%u",
                                          (unsigned)s_state.testeo.tries,
                                          (unsigned)app_head_fast_diag_now_ms() + commands->response_timeout_ms);
            continue;
        }

        rx_err = app_can_rx_receive(&rx_event, pdMS_TO_TICKS(10));
        if (rx_err == ESP_OK) {
            if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
                break;
            }
            app_head_fast_diag_testeo_on_rx(&s_state.testeo, &rx_event, route, commands);
            bool still_running = s_state.testeo.running;
            bool still_waiting = s_state.testeo.waiting;
            app_head_fast_diag_give_mutex();
            if (!still_running || !still_waiting) {
                continue;
            }
            continue;
        }

        if (rx_err != ESP_ERR_TIMEOUT) {
            if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
                s_state.testeo.running = false;
                s_state.testeo.waiting = false;
                s_state.testeo.latched = true;
                s_state.testeo.armed = false;
                app_head_fast_diag_give_mutex();
            }
            app_head_fast_diag_send_linef(route, "TESTEO|ERROR|RX_FAIL|CODE=%s", esp_err_to_name(rx_err));
            break;
        }

        if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
            break;
        }

        cancel_requested = app_head_fast_diag_should_cancel_locked(APP_HEAD_FAST_DIAG_TESTEO);
        if (cancel_requested) {
            s_state.testeo.running = false;
            s_state.testeo.waiting = false;
            app_head_fast_diag_give_mutex();
            app_head_fast_diag_send_line(route, "TESTEO|CANCELLED");
            break;
        }

        now_ms = app_head_fast_diag_now_ms();
        if (s_state.testeo.waiting && (int32_t)(now_ms - s_state.testeo.wait_deadline_ms) >= 0) {
            s_state.testeo.running = false;
            s_state.testeo.waiting = false;
            s_state.testeo.latched = true;
            s_state.testeo.armed = false;
            app_head_fast_diag_give_mutex();
            app_head_fast_diag_send_linef(route,
                                          "TESTEO|ERROR|sin respuesta 0x%03" PRIX32 " (timeout).",
                                          commands->response_can_id);
            break;
        }

        app_head_fast_diag_give_mutex();
    }

    if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        app_head_fast_diag_reset_kind_locked(APP_HEAD_FAST_DIAG_TESTEO);
        app_head_fast_diag_give_mutex();
    }

    app_head_fast_diag_release_route(task_args);
    vTaskDelete(NULL);
}

static void app_head_fast_diag_init_task(void *arg)
{
    app_head_fast_diag_task_args_t *task_args = (app_head_fast_diag_task_args_t *)arg;
    const HeadCommandProfile *profile = task_args != NULL ? task_args->profile : NULL;
    const HeadInitCommandSequence *commands = profile != NULL ? &profile->init_sequence : NULL;
    size_t total = commands != NULL
        ? commands->phase1_step_count + commands->phase2_step_count
        : 0;
    size_t step = 0;
    int bus = 1;
    const app_reply_route_t *route = NULL;
    const char *end_result = "CANCELLED";
    esp_err_t end_err = ESP_OK;

    if (task_args == NULL
        || commands == NULL
        || commands->phase1_steps == NULL
        || commands->phase1_step_count == 0
        || commands->phase2_steps == NULL
        || commands->phase2_step_count == 0) {
        app_head_fast_diag_release_route(task_args);
        vTaskDelete(NULL);
        return;
    }

    ESP_LOGI(TAG, "INIT_TASK_START");
    if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        ESP_LOGE(TAG, "INIT_LOCK_TIMEOUT|STAGE=TASK_START");
        app_head_fast_diag_release_route(task_args);
        vTaskDelete(NULL);
        return;
    }
    ESP_LOGI(TAG, "INIT_LOCK_OK|STAGE=TASK_START");

    s_state.init.task_handle = xTaskGetCurrentTaskHandle();
    s_state.init.running = true;
    s_state.init.cancel_requested = false;
    s_state.init.started_ms = app_head_fast_diag_now_ms();
    s_state.init.can_bus = task_args->can_bus;
    s_state.init.route = task_args->route;
    s_state.init.route_release = task_args->route_release;
    bus = task_args->can_bus;
    route = task_args->route;
    app_head_fast_diag_give_mutex();

    app_head_fast_diag_send_linef(route,
                                  "PROFILE_COMMAND|PROGRAM=%u|MODULE=INIT|STEP=1",
                                  (unsigned)(profile != NULL ? profile->program_id : APP_HEAD_PROGRAM_1));
    app_head_fast_diag_send_linef(route,
                                  "INIT|START|TIME=%u|BUS=%s|TOTAL=%u",
                                  (unsigned)s_state.init.started_ms,
                                  app_head_fast_diag_bus_name(bus),
                                  (unsigned)total);
    ESP_LOGI(TAG,
             "INIT_BEGIN|BUS=%s|TOTAL=%u",
             app_head_fast_diag_bus_name(bus),
             (unsigned)total);

    if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        bool cancel_requested = app_head_fast_diag_should_cancel_locked(APP_HEAD_FAST_DIAG_INIT);
        app_head_fast_diag_give_mutex();
        if (cancel_requested) {
            app_head_fast_diag_send_line(route, "INIT|CANCELLED");
            goto cleanup;
        }
    }

    // [ACURATEX] La inicializacion parte de un estado quieto para no arrastrar
    // secuencias de movimiento activas a la rutina de arranque.
    app_head_state_manager_stop_all_motion();

    for (size_t i = 0; i < commands->phase1_step_count; ++i) {
        char progress[160];

        if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
            goto cleanup;
        }

        if (app_head_fast_diag_should_cancel_locked(APP_HEAD_FAST_DIAG_INIT)) {
            app_head_fast_diag_give_mutex();
            app_head_fast_diag_send_line(route, "INIT|CANCELLED");
            goto cleanup;
        }

        snprintf(progress, sizeof(progress), "INIT|STEP|INIT1|%u/%u|%s",
                 (unsigned)(step + 1U),
                 (unsigned)total,
                 commands->phase1_steps[i]);
        ESP_LOGI(TAG,
                 "INIT_STEP|N=%u|PHASE=INIT1|LINE=%s",
                 (unsigned)(step + 1U),
                 commands->phase1_steps[i]);
        app_head_fast_diag_give_mutex();
        app_head_fast_diag_send_line(route, progress);

        {
            esp_err_t step_err = app_head_fast_diag_run_script_line(commands->phase1_steps[i], bus, APP_HEAD_FAST_DIAG_INIT);
            if (step_err == ESP_ERR_INVALID_STATE) {
                app_head_fast_diag_send_line(route, "INIT|CANCELLED");
                goto cleanup;
            }
            if (step_err != ESP_OK) {
                end_result = "ERROR";
                end_err = step_err;
                if (step_err == ESP_ERR_INVALID_ARG) {
                    app_head_fast_diag_send_linef(route, "INIT|ERROR|PARSE|INIT1|%s", commands->phase1_steps[i]);
                } else {
                    app_head_fast_diag_send_linef(route, "INIT|ERROR|TX|INIT1|ERR=%s|LINE=%s",
                                                  esp_err_to_name(step_err),
                                                  commands->phase1_steps[i]);
                }
                goto cleanup_error;
            }
        }

        if (!app_head_fast_diag_delay_cancelable(commands->phase1_step_delay_ms, APP_HEAD_FAST_DIAG_INIT)) {
            app_head_fast_diag_send_line(route, "INIT|CANCELLED");
            goto cleanup;
        }

        step++;
    }

    app_head_fast_diag_send_linef(route, "INIT|STEP|GAP|%u/%u|WAIT %u",
                                  (unsigned)step,
                                  (unsigned)total,
                                  (unsigned)commands->phase_gap_ms);
    ESP_LOGI(TAG,
             "INIT_STEP|N=%u|PHASE=GAP|WAIT_MS=%u",
             (unsigned)step,
             (unsigned)commands->phase_gap_ms);
    if (!app_head_fast_diag_delay_cancelable(commands->phase_gap_ms, APP_HEAD_FAST_DIAG_INIT)) {
        app_head_fast_diag_send_line(route, "INIT|CANCELLED");
        goto cleanup;
    }

    for (size_t i = 0; i < commands->phase2_step_count; ++i) {
        char progress[160];

        if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
            goto cleanup;
        }

        if (app_head_fast_diag_should_cancel_locked(APP_HEAD_FAST_DIAG_INIT)) {
            app_head_fast_diag_give_mutex();
            app_head_fast_diag_send_line(route, "INIT|CANCELLED");
            goto cleanup;
        }

        snprintf(progress, sizeof(progress), "INIT|STEP|INIT2|%u/%u|%s",
                 (unsigned)(step + 1U),
                 (unsigned)total,
                 commands->phase2_steps[i]);
        ESP_LOGI(TAG,
                 "INIT_STEP|N=%u|PHASE=INIT2|LINE=%s",
                 (unsigned)(step + 1U),
                 commands->phase2_steps[i]);
        app_head_fast_diag_give_mutex();
        app_head_fast_diag_send_line(route, progress);

        {
            esp_err_t step_err = app_head_fast_diag_run_script_line(commands->phase2_steps[i], bus, APP_HEAD_FAST_DIAG_INIT);
            if (step_err == ESP_ERR_INVALID_STATE) {
                app_head_fast_diag_send_line(route, "INIT|CANCELLED");
                goto cleanup;
            }
            if (step_err != ESP_OK) {
                end_result = "ERROR";
                end_err = step_err;
                if (step_err == ESP_ERR_INVALID_ARG) {
                    app_head_fast_diag_send_linef(route, "INIT|ERROR|PARSE|INIT2|%s", commands->phase2_steps[i]);
                } else {
                    app_head_fast_diag_send_linef(route, "INIT|ERROR|TX|INIT2|ERR=%s|LINE=%s",
                                                  esp_err_to_name(step_err),
                                                  commands->phase2_steps[i]);
                }
                goto cleanup_error;
            }
        }

        if (!app_head_fast_diag_delay_cancelable(commands->phase2_step_delay_ms, APP_HEAD_FAST_DIAG_INIT)) {
            app_head_fast_diag_send_line(route, "INIT|CANCELLED");
            goto cleanup;
        }

        step++;
    }

    app_head_fast_diag_send_line(route, "INIT|DONE");
    end_result = "OK";
    end_err = ESP_OK;
    goto cleanup;

cleanup_error:
    if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        s_state.init.running = false;
        s_state.init.cancel_requested = false;
        s_state.init.task_handle = NULL;
        app_head_fast_diag_give_mutex();
    }
    goto cleanup;

cleanup:
    ESP_LOGI(TAG,
             "INIT_END|RESULT=%s|ERR=%s",
             end_result,
             esp_err_to_name(end_err));
    if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        app_head_fast_diag_reset_kind_locked(APP_HEAD_FAST_DIAG_INIT);
        app_head_fast_diag_give_mutex();
    }

    app_head_fast_diag_release_route(task_args);
    vTaskDelete(NULL);
}

static esp_err_t app_head_fast_diag_start_task(app_head_fast_diag_kind_t kind,
                                               const app_command_env_t *env,
                                               void *reply_ctx)
{
    app_head_fast_diag_task_args_t *task_args = NULL;
    app_reply_route_t *route = NULL;
    app_reply_ctx_release_fn_t release_fn = NULL;
    int bus = 1;
    BaseType_t ok;

    if (env == NULL || reply_ctx == NULL || env->reply_ctx_clone == NULL || env->reply_ctx_release == NULL) {
        return ESP_ERR_INVALID_STATE;
    }

    if (!app_can_is_started()) {
        return ESP_ERR_INVALID_STATE;
    }

    bus = app_head_fast_diag_normalize_bus(env->active_bus);
    if (kind == APP_HEAD_FAST_DIAG_INIT) {
        ESP_LOGI(TAG, "INIT_BEGIN|REQUEST|BUS=%s", app_head_fast_diag_bus_name(bus));
    }

    if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        if (kind == APP_HEAD_FAST_DIAG_INIT) {
            ESP_LOGE(TAG, "INIT_LOCK_TIMEOUT|STAGE=START");
        }
        return ESP_ERR_TIMEOUT;
    }

    if (kind == APP_HEAD_FAST_DIAG_INIT) {
        ESP_LOGI(TAG, "INIT_LOCK_OK|STAGE=START");
    }

    if (kind == APP_HEAD_FAST_DIAG_TESTEO) {
        if (s_state.testeo.running || s_state.init.running || s_state.testeo.latched) {
            app_head_fast_diag_give_mutex();
            return ESP_ERR_INVALID_STATE;
        }
        s_state.testeo.cancel_requested = false;
    }
    else if (kind == APP_HEAD_FAST_DIAG_INIT) {
        if (s_state.init.running || s_state.testeo.running) {
            app_head_fast_diag_give_mutex();
            return ESP_ERR_INVALID_STATE;
        }
    }
    else {
        app_head_fast_diag_give_mutex();
        return ESP_ERR_INVALID_ARG;
    }

    app_head_fast_diag_give_mutex();

    if (!app_head_fast_diag_clone_route(env, reply_ctx, &route, &release_fn)) {
        return ESP_ERR_NO_MEM;
    }

    task_args = (app_head_fast_diag_task_args_t *)calloc(1, sizeof(app_head_fast_diag_task_args_t));
    if (task_args == NULL) {
        release_fn(route);
        return ESP_ERR_NO_MEM;
    }

    task_args->kind = kind;
    task_args->route = route;
    task_args->route_release = release_fn;
    task_args->can_bus = bus;
    task_args->profile = app_head_program_get_active_profile();
    if (task_args->profile == NULL) {
        release_fn(route);
        free(task_args);
        return ESP_ERR_INVALID_STATE;
    }

    if (kind == APP_HEAD_FAST_DIAG_INIT) {
        if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
            ESP_LOGE(TAG, "INIT_LOCK_TIMEOUT|STAGE=RESERVE");
            release_fn(route);
            free(task_args);
            return ESP_ERR_TIMEOUT;
        }

        if (s_state.init.running || s_state.testeo.running) {
            app_head_fast_diag_give_mutex();
            release_fn(route);
            free(task_args);
            return ESP_ERR_INVALID_STATE;
        }

        s_state.init.running = true;
        s_state.init.cancel_requested = false;
        s_state.init.can_bus = bus;
        s_state.init.task_handle = NULL;
        app_head_fast_diag_give_mutex();
    }

    if (kind == APP_HEAD_FAST_DIAG_TESTEO) {
        ok = xTaskCreatePinnedToCore(app_head_fast_diag_testeo_task,
                                     "fast_testeo",
                                     6144,
                                     task_args,
                                     5,
                                     &s_state.testeo.task_handle,
                                     1);
    }
    else {
        ok = xTaskCreatePinnedToCore(app_head_fast_diag_init_task,
                                     "fast_init",
                                     6144,
                                     task_args,
                                     5,
                                     &s_state.init.task_handle,
                                     1);
    }

    if (ok != pdPASS) {
        release_fn(route);
        free(task_args);
        if (kind == APP_HEAD_FAST_DIAG_TESTEO) {
            if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
                s_state.testeo.task_handle = NULL;
                app_head_fast_diag_give_mutex();
            }
        }
        else {
            if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
                app_head_fast_diag_reset_kind_locked(APP_HEAD_FAST_DIAG_INIT);
                app_head_fast_diag_give_mutex();
            }
        }
        return ESP_ERR_NO_MEM;
    }

    if (kind == APP_HEAD_FAST_DIAG_INIT) {
        ESP_LOGI(TAG, "INIT_TASK_START|CREATE_OK|BUS=%s", app_head_fast_diag_bus_name(bus));
    }

    return ESP_OK;
}

esp_err_t app_head_fast_diag_start_testeo(const app_command_env_t *env,
                                          void *reply_ctx)
{
    esp_err_t err = app_head_fast_diag_start_task(APP_HEAD_FAST_DIAG_TESTEO, env, reply_ctx);
    if (err == ESP_OK) {
        if (app_head_fast_diag_take_mutex(portMAX_DELAY)) {
            s_state.testeo.armed = true;
            s_state.testeo.latched = false;
            s_state.testeo.cancel_requested = false;
            app_head_fast_diag_give_mutex();
        }
    }
    return err;
}

esp_err_t app_head_fast_diag_start_init(const app_command_env_t *env,
                                        void *reply_ctx)
{
    if (env == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    esp_err_t err = app_head_fast_diag_start_task(APP_HEAD_FAST_DIAG_INIT, env, reply_ctx);
    return err;
}

void app_head_fast_diag_request_stop(void)
{
    if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        return;
    }

    s_state.testeo.cancel_requested = true;
    s_state.init.cancel_requested = true;
    app_head_fast_diag_give_mutex();
}

bool app_head_fast_diag_is_busy(void)
{
    bool busy = false;

    if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        return false;
    }

    busy = s_state.testeo.running || s_state.init.running;
    app_head_fast_diag_give_mutex();
    return busy;
}

bool app_head_fast_diag_testeo_can_start(void)
{
    bool can_start = false;

    if (!app_head_fast_diag_take_mutex(portMAX_DELAY)) {
        return false;
    }

    can_start = !s_state.testeo.running
        && !s_state.init.running
        && !s_state.testeo.latched;
    app_head_fast_diag_give_mutex();
    return can_start;
}




