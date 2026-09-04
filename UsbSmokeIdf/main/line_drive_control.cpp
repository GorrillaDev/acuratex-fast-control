#include "line_drive_control.h"
#include "line_drive_math.h"

#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#include "driver/gpio.h"
#include "driver/ledc.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char *TAG = "line_drive";
static constexpr gpio_num_t S1_PULSE = GPIO_NUM_17;
static constexpr gpio_num_t S1_DIR = GPIO_NUM_18;
static constexpr gpio_num_t S1_SON = GPIO_NUM_10;
static constexpr gpio_num_t S2_PULSE = GPIO_NUM_8;
static constexpr gpio_num_t S2_DIR = GPIO_NUM_9;
static constexpr gpio_num_t S2_SON = GPIO_NUM_11;
static constexpr gpio_num_t STEP_PULSE = GPIO_NUM_12;
static constexpr gpio_num_t STEP_DIR = GPIO_NUM_13;
static constexpr int64_t S1_UNITS_PER_REV = LINE_DRIVE_OPERATOR_UNITS_PER_REV;
static constexpr int64_t S1_STEPS_PER_REV = LINE_DRIVE_STEPS_PER_REV;
static constexpr uint32_t S1_RPM = 480;
static constexpr uint32_t S1_HZ = S1_RPM * S1_STEPS_PER_REV / 60;
static constexpr uint64_t S1_HALF_US = 1000000ULL / (2ULL * S1_HZ);

struct Position { int64_t target_units; int turns; int64_t total_units; int64_t steps; bool configured; };
static Position positions[3] = {};
static esp_timer_handle_t s1_timer;
static esp_timer_handle_t step_timer;
static portMUX_TYPE state_mux = portMUX_INITIALIZER_UNLOCKED;
static volatile bool s1_son;
static volatile bool s1_moving;
static volatile bool s1_pulse_high;
static volatile int64_t s1_current;
static volatile int64_t s1_target;
static volatile uint64_t s1_remaining;
static volatile bool s1_position_valid;
static volatile bool routine_on;
static int routine_order[3] = {1,2,3};
static size_t routine_length = 3;
static uint32_t routine_dwell_ms = 1000;
static TaskHandle_t routine_task_handle;
static bool s2_son;
static bool s2_running;
static bool s2_dir;
static uint32_t s2_hz = 1000;
static volatile bool step_running;
static volatile bool step_high;
static bool step_dir;
static uint32_t step_hz = 200;

extern "C" int64_t app_line_drive_operator_units_to_steps(int64_t units) { return line_drive_operator_units_to_steps(units); }

static void stop_s1_motion()
{
    portENTER_CRITICAL(&state_mux);
    s1_moving = false; s1_remaining = 0; s1_pulse_high = false;
    portEXIT_CRITICAL(&state_mux);
    if (s1_timer) esp_timer_stop(s1_timer);
    gpio_set_level(S1_PULSE, 0);
}

static void IRAM_ATTR s1_tick(void *)
{
    portENTER_CRITICAL_ISR(&state_mux);
    if (!s1_son || !s1_moving || s1_remaining == 0) {
        s1_pulse_high = false;
        gpio_set_level(S1_PULSE, 0);
        portEXIT_CRITICAL_ISR(&state_mux);
        return;
    }
    s1_pulse_high = !s1_pulse_high;
    gpio_set_level(S1_PULSE, s1_pulse_high);
    if (!s1_pulse_high) {
        s1_remaining = s1_remaining - 1;
    }
    if (!s1_pulse_high && s1_remaining == 0) {
        s1_current = s1_target;
        s1_moving = false;
    }
    portEXIT_CRITICAL_ISR(&state_mux);
}

static esp_err_t move_s1(int64_t target)
{
    if (!s1_son || !s1_position_valid) return ESP_ERR_INVALID_STATE;
    stop_s1_motion();
    int64_t current = s1_current;
    int64_t delta = target - current;
    s1_target = target;
    if (delta == 0) return ESP_OK;
    gpio_set_level(S1_DIR, delta > 0 ? 1 : 0);
    s1_remaining = (uint64_t)(delta > 0 ? delta : -delta);
    s1_moving = true;
    return esp_timer_start_periodic(s1_timer, S1_HALF_US);
}

static void s1_son_set(bool on)
{
    if (on && !s1_son) {
        gpio_set_level(S1_SON, 1);
        s1_son = true; s1_current = 0; s1_target = 0; s1_position_valid = true;
    } else if (!on) {
        routine_on = false; stop_s1_motion(); gpio_set_level(S1_SON, 0);
        s1_son = false; s1_current = 0; s1_target = 0; s1_position_valid = false;
    }
}

static void s2_pwm_apply()
{
    ledc_set_freq(LEDC_LOW_SPEED_MODE, LEDC_TIMER_0, s2_hz);
    ledc_set_duty(LEDC_LOW_SPEED_MODE, LEDC_CHANNEL_0, s2_running && s2_son ? 32 : 0);
    ledc_update_duty(LEDC_LOW_SPEED_MODE, LEDC_CHANNEL_0);
}

static void s2_dir_set(bool dir)
{
    bool resume = s2_running;
    if (resume) { s2_running = false; s2_pwm_apply(); vTaskDelay(pdMS_TO_TICKS(2)); }
    s2_dir = dir; gpio_set_level(S2_DIR, dir); vTaskDelay(pdMS_TO_TICKS(2));
    if (resume) { s2_running = true; s2_pwm_apply(); }
}

static void IRAM_ATTR step_tick(void *)
{
    if (!step_running) { gpio_set_level(STEP_PULSE, 0); return; }
    step_high = !step_high; gpio_set_level(STEP_PULSE, step_high);
}

static void step_stop() { step_running = false; if (step_timer) esp_timer_stop(step_timer); step_high = false; gpio_set_level(STEP_PULSE, 0); }
static esp_err_t step_start()
{
    if (step_running) return ESP_OK;
    step_running = true;
    esp_err_t err = esp_timer_start_periodic(step_timer, 1000000ULL / (2ULL * step_hz));
    if (err != ESP_OK) step_running = false;
    return err;
}
static esp_err_t step_set_hz(uint32_t hz)
{
    step_hz = hz < 1 ? 1 : (hz > 200000 ? 200000 : hz);
    bool resume = step_running; step_stop(); return resume ? step_start() : ESP_OK;
}
static void step_reverse()
{
    bool resume = step_running; step_stop(); vTaskDelay(pdMS_TO_TICKS(3));
    step_dir = !step_dir; gpio_set_level(STEP_DIR, step_dir); vTaskDelay(pdMS_TO_TICKS(3));
    if (resume) (void)step_start();
}

static void routine_task(void *)
{
    while (true) {
        ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
        size_t index = 0;
        while (routine_on) {
            int pos = routine_order[index];
            if (pos >= 1 && pos <= 3 && positions[pos-1].configured) {
                if (move_s1(positions[pos-1].steps) != ESP_OK) { routine_on = false; break; }
                while (routine_on && s1_moving) vTaskDelay(pdMS_TO_TICKS(1));
                if (!routine_on) break;
                uint32_t waited = 0;
                while (routine_on && waited < routine_dwell_ms) { uint32_t slice = (routine_dwell_ms-waited > 10) ? 10 : routine_dwell_ms-waited; vTaskDelay(pdMS_TO_TICKS(slice)); waited += slice; }
            }
            index = (index + 1) % routine_length;
        }
    }
}

extern "C" void app_line_drive_safe_stop(void)
{
    routine_on = false; s1_son_set(false);
    s2_running = false; s2_son = false; s2_pwm_apply(); gpio_set_level(S2_SON, 0);
    step_stop();
}

static bool token_value(const char *token, const char *key, const char **value)
{
    size_t n = strlen(key); if (strncasecmp(token,key,n) || token[n] != '=') return false; *value = token+n+1; return true;
}
static bool on_value(const char *value) { return value && !strcasecmp(value,"ON"); }

extern "C" bool app_line_drive_is_command(const char *line) { return line && !strncasecmp(line,"LD_",3); }

extern "C" esp_err_t app_line_drive_process_line(const char *incoming, char *response, size_t response_size)
{
    if (!incoming || !response || response_size == 0) return ESP_ERR_INVALID_ARG;
    char line[192]; strlcpy(line,incoming,sizeof(line));
    char *save=nullptr; char *cmd=strtok_r(line,"|",&save); char *arg=strtok_r(nullptr,"|",&save);
    esp_err_t err=ESP_OK;
    if (!strcasecmp(cmd,"LD_SAFE_STOP")) app_line_drive_safe_stop();
    else if (!strcasecmp(cmd,"LD_S1_SON")) s1_son_set(on_value(arg));
    else if (!strcasecmp(cmd,"LD_S1_POS_CONFIG")) {
        int pos=0,turns=0; int64_t target=0; const char *v;
        for(char *t=arg;t;t=strtok_r(nullptr,"|",&save)) { if(token_value(t,"POS",&v)) pos=atoi(v); else if(token_value(t,"TARGET",&v)) target=strtoll(v,nullptr,10); else if(token_value(t,"TURNS",&v)) turns=atoi(v); }
        if(pos<1||pos>3) err=ESP_ERR_INVALID_ARG; else { Position &p=positions[pos-1]; p.target_units=target; p.turns=turns; p.total_units=line_drive_total_operator_units(target,turns); p.steps=app_line_drive_operator_units_to_steps(p.total_units); p.configured=true; }
    } else if (!strcasecmp(cmd,"LD_S1_GOTO")) { const char *v; int pos=0; if(arg&&token_value(arg,"POS",&v)) pos=atoi(v); if(pos<1||pos>3||!positions[pos-1].configured) err=ESP_ERR_INVALID_ARG; else err=move_s1(positions[pos-1].steps); }
    else if (!strcasecmp(cmd,"LD_S1_ROUTINE")) {
        if (!on_value(arg)) { routine_on=false; stop_s1_motion(); }
        else { routine_length=0; const char *v; for(char *t=strtok_r(nullptr,"|",&save);t;t=strtok_r(nullptr,"|",&save)) { if(token_value(t,"ORDER",&v)) { char order[24]; strlcpy(order,v,sizeof(order)); char *os=nullptr; for(char *n=strtok_r(order,",",&os);n&&routine_length<3;n=strtok_r(nullptr,",",&os)) { int p=atoi(n); if(p>=1&&p<=3&&positions[p-1].configured) routine_order[routine_length++]=p; } } else if(token_value(t,"DWELL_MS",&v)) routine_dwell_ms=(uint32_t)strtoul(v,nullptr,10); } if(!s1_son||!s1_position_valid||routine_length==0) err=ESP_ERR_INVALID_STATE; else { routine_on=true; xTaskNotifyGive(routine_task_handle); } }
    } else if (!strcasecmp(cmd,"LD_S2_SON")) { s2_son=on_value(arg); gpio_set_level(S2_SON,s2_son); if(!s2_son)s2_running=false; s2_pwm_apply(); }
    else if (!strcasecmp(cmd,"LD_S2_RUN")) { s2_running=on_value(arg)&&s2_son; s2_pwm_apply(); }
    else if (!strcasecmp(cmd,"LD_S2_DIR")) s2_dir_set(arg&&atoi(arg)!=0);
    else if (!strcasecmp(cmd,"LD_S2_FREQ")) { const char *v; if(!arg||!token_value(arg,"HZ",&v))err=ESP_ERR_INVALID_ARG; else { uint32_t hz=strtoul(v,nullptr,10); s2_hz=hz<1?1:(hz>160000?160000:hz); s2_pwm_apply(); } }
    else if (!strcasecmp(cmd,"LD_STEP_RUN")) err=on_value(arg)?step_start():(step_stop(),ESP_OK);
    else if (!strcasecmp(cmd,"LD_STEP_DIR")) { bool resume=step_running; step_stop(); vTaskDelay(pdMS_TO_TICKS(3)); step_dir=arg&&atoi(arg)!=0; gpio_set_level(STEP_DIR,step_dir); vTaskDelay(pdMS_TO_TICKS(3)); if(resume)err=step_start(); }
    else if (!strcasecmp(cmd,"LD_STEP_REV")) step_reverse();
    else if (!strcasecmp(cmd,"LD_STEP_FREQ")) { const char *v; if(!arg||!token_value(arg,"HZ",&v))err=ESP_ERR_INVALID_ARG; else err=step_set_hz(strtoul(v,nullptr,10)); }
    else err=ESP_ERR_NOT_SUPPORTED;
    snprintf(response,response_size,err==ESP_OK?"OK|%s":"ERR|%s|%s",cmd,esp_err_to_name(err));
    return err;
}

extern "C" esp_err_t app_line_drive_init(void)
{
    gpio_config_t io={}; io.mode=GPIO_MODE_OUTPUT; io.pin_bit_mask=(1ULL<<S1_PULSE)|(1ULL<<S1_DIR)|(1ULL<<S1_SON)|(1ULL<<S2_DIR)|(1ULL<<S2_SON)|(1ULL<<STEP_PULSE)|(1ULL<<STEP_DIR); ESP_ERROR_CHECK(gpio_config(&io));
    gpio_set_level(S1_PULSE,0); gpio_set_level(S1_SON,0); gpio_set_level(S2_SON,0); gpio_set_level(STEP_PULSE,0);
    ledc_timer_config_t lt={}; lt.speed_mode=LEDC_LOW_SPEED_MODE; lt.duty_resolution=LEDC_TIMER_6_BIT; lt.timer_num=LEDC_TIMER_0; lt.freq_hz=s2_hz; lt.clk_cfg=LEDC_AUTO_CLK; ESP_ERROR_CHECK(ledc_timer_config(&lt));
    ledc_channel_config_t lc={}; lc.gpio_num=S2_PULSE; lc.speed_mode=LEDC_LOW_SPEED_MODE; lc.channel=LEDC_CHANNEL_0; lc.timer_sel=LEDC_TIMER_0; lc.duty=0; lc.hpoint=0; ESP_ERROR_CHECK(ledc_channel_config(&lc));
    esp_timer_create_args_t s1args={}; s1args.callback=s1_tick; s1args.name="ld_s1"; ESP_ERROR_CHECK(esp_timer_create(&s1args,&s1_timer));
    esp_timer_create_args_t stepargs={}; stepargs.callback=step_tick; stepargs.name="ld_step"; ESP_ERROR_CHECK(esp_timer_create(&stepargs,&step_timer));
    if(xTaskCreate(routine_task,"ld_routine",3072,nullptr,5,&routine_task_handle)!=pdPASS)return ESP_ERR_NO_MEM;
    ESP_LOGI(TAG,"ready S1=17/18/10 S2=8/9/11 STEP=12/13"); return ESP_OK;
}