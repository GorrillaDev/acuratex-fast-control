  // =======================================================
// FW UNIFICADO:
// (A) ESP32 CAN (TWAI) + INIT (bloqueante) + SCRIPT ENGINE (NO bloqueante)
//     + WiFi AP + HTTP API
//     + Core split: Core1 (CAN/script/serial + ejecuta comandos), Core0 (WiFi/HTTP)
//     IMPORTANTE: el HTTP NO toca TWAI directo -> TODO va por COLA (queue) al Core1
//     + SPIFFS: sirve /index.html desde el ESP32 (UI estable en celular)
//     + RX->UI: endpoint /rx (último frame recibido) SIN LENTEAR
//     + Cabezal activo: Yarn 1; Stitch 1 y 2
//     + TESTEO v2 ENCLAVADO + REARME por 0x702 DLC=2 DATA=3F 00
//
// (B) SERVO DRIVE + STEPPER PULSE/DIR GPIO12/GPIO13
//     + /set?servo_hz=&servo_duty=&step_hz=
//     + /cmd?do=servo_* o step_* (procesa directo en Core0, sin tocar TWAI)
//     + REV suave no bloqueante (step_rev_tick en loop)
//
// RUTAS WEB:
//   /      -> /index.html (CAN UI)
//   /b     -> /ui2.html   (Servo+Stepper UI)   [si lo usas]
//   /cmd?do=...
//   /send?line=...
//   /set?... (servo/step)
//   /status  (JSON combinado CAN + Servo/Stepper + INIT)
//   /rx
//   /fs
// =======================================================

#include <Arduino.h>

struct Cascade {
  bool running = false;
  uint8_t phase = 0;     // 0=ON sweep, 1=OFF sweep
  uint8_t p = 1;         // pin actual 1..N
  uint32_t next_ms = 0;
  uint16_t delay_ms = 80;
};
#include "driver/twai.h"
#include <WiFi.h>
#include <WebServer.h>
#include <FS.h>
#include "SPIFFS.h"

#include "driver/gpio.h"
#include "esp_timer.h"

// MODO CAN-ONLY:
// 1 = no inicializa ni toca pines de servo/stepper.
// Esto evita resets en ESP32-S3 cuando esos pines coinciden con flash/PSRAM
// o cuando por ahora solo se necesita probar CAN.
#define CAN_ONLY_MODE 0

// Motor stepper habilitado.
// Hardware actual:
//   STEP / PULSE -> GPIO12
//   DIR          -> GPIO13
//
// La tarjeta de este motor usa únicamente PULSE y DIR.
// No se agrega ENABLE porque no existe en el hardware actual.
#define STEPPER_ENABLED 1

// ===== Prototipos TESTEO (SIN twai_message_t) =====
static void testeo_start();
static void testeo_tick();
static void testeo_on_any_rx_fields(uint32_t id, uint8_t dlc, const uint8_t *data, uint8_t extd, uint8_t rtr);

// ============================
// ===== WIFI AP + SERVER =====
// ============================
static const char* AP_SSID = "ESP32_TEST";
static const char* AP_PASS = "12345678"; // >=8 chars, o "" si quieres abierto
WebServer server(80);

// ------------------- YARN ACTIVO -------------------
// Este cabezal tiene un solo bloque Yarn (Yarn 1) con seis canales físicos.
static constexpr uint8_t ACTIVE_YARN = 1;
static constexpr uint8_t YARN_CHANNEL_COUNT = 6;
static constexpr uint32_t YARN1_CAN_ID = 0x363;
// ------------------- CAN PINS -------------------
// Hardware actual:
//   CAN_TX   -> GPIO4
//   CAN_RX   -> GPIO5
//   CAN_STBY -> GPIO6
//
// MCP2561/MCP2562: STBY en LOW = transceiver activo / modo normal.
// Lo dejamos siempre activo para que el CAN funcione apenas arranca el ESP32.
static const gpio_num_t CAN_TX   = GPIO_NUM_4;
static const gpio_num_t CAN_RX   = GPIO_NUM_5;
static const gpio_num_t CAN_STBY = GPIO_NUM_6;

// ------------------- BITRATE --------------------
static const twai_timing_config_t TIMING = TWAI_TIMING_CONFIG_1MBITS();

static bool can_started = false;

// ============================
// ===== RX -> UI (last RX) ====
// ============================
struct LastRxFrame {
  uint32_t count;     // incrementa en cada RX
  uint32_t t_ms;      // millis cuando llegó
  uint32_t id;        // std 11-bit
  uint8_t  dlc;
  uint8_t  data[8];
  uint8_t  rtr;
  uint8_t  extd;
};

static LastRxFrame g_lastRx = {0,0,0,0,{0},0,0};
static portMUX_TYPE g_lastRxMux = portMUX_INITIALIZER_UNLOCKED;

// ============================
// ===== INIT PUBLIC STATE =====
// ============================
static constexpr uint8_t I_IDLE=0, I_RUNNING=1, I_DONE=2, I_ERROR=3;

struct InitPublic {
  uint32_t run_id = 0;
  uint8_t  state = I_IDLE;     // 0 idle, 1 running, 2 done, 3 error
  uint16_t i = 0;              // paso global (1..n)
  uint16_t n = 0;              // total global
  uint32_t t_ms = 0;           // millis última actualización
  char tag[8] = "IDLE";        // "INIT1","GAP","INIT2","INIT"
  char msg[96] = "IDLE";       // texto corto (línea actual / error)
};

static InitPublic g_ipub;
static portMUX_TYPE g_ipubMux = portMUX_INITIALIZER_UNLOCKED;
static uint32_t g_init_rid = 0;

static void init_pub_begin(uint16_t total) {
  portENTER_CRITICAL(&g_ipubMux);
  g_ipub.run_id = ++g_init_rid;
  g_ipub.state = I_RUNNING;
  g_ipub.i = 0;
  g_ipub.n = total;
  g_ipub.t_ms = millis();
  strlcpy(g_ipub.tag, "INIT", sizeof(g_ipub.tag));
  strlcpy(g_ipub.msg, "START", sizeof(g_ipub.msg));
  portEXIT_CRITICAL(&g_ipubMux);
}

static void init_pub_step(const char* tag, uint16_t i, uint16_t n, const char* msg) {
  portENTER_CRITICAL(&g_ipubMux);
  g_ipub.state = I_RUNNING;
  g_ipub.i = i;
  g_ipub.n = n;
  g_ipub.t_ms = millis();
  if (tag) strlcpy(g_ipub.tag, tag, sizeof(g_ipub.tag));
  if (msg) strlcpy(g_ipub.msg, msg, sizeof(g_ipub.msg));
  portEXIT_CRITICAL(&g_ipubMux);
}

static void init_pub_finish(uint8_t st, const char* tag, const char* msg) {
  portENTER_CRITICAL(&g_ipubMux);
  g_ipub.state = st;
  g_ipub.t_ms = millis();
  if (tag) strlcpy(g_ipub.tag, tag, sizeof(g_ipub.tag));
  if (msg) strlcpy(g_ipub.msg, msg, sizeof(g_ipub.msg));
  portEXIT_CRITICAL(&g_ipubMux);
}

// ============================
// ===== INIT SEQUENCES =======
// ============================
static const char* INIT1_SEQ[] = {
  "363 08 02 00 00 00 00", "WAIT 0", "363 04 0a 10 00 00 00", "WAIT 0", "363 08 14 00", "WAIT 364",
  "363 08 14 00", "WAIT 40", "363 08 01", "WAIT 0", "363 08 14 00", "WAIT 2027",
  "361 07 01", "WAIT 1685", "363 08 14 00", "WAIT 33", "363 08 01", "WAIT 0",
  "363 08 14 00", "WAIT 4046", "363 08 02 00 00 00 00", "WAIT 1", "330 40 00 00 00 00 00", "WAIT 100",
  "370 fd 06 10 00", "WAIT 100", "370 fd 06 11 00", "WAIT 100", "363 08 02 00 00 00 00", "WAIT 0",
  "363 04 0a 10 00 00 00", "WAIT 0", "363 07 0c 00 00", "WAIT 99", "361 02 04 00 0b 00 00", "WAIT 0",
  "320 6c 50 0b 00", "WAIT 0", "361 04 0f 01 01 e4 0c", "WAIT 0", "361 01 01 ff ff ff ff", "WAIT 0",
  "320 00", "WAIT 100", "361 01 02 ff ff", "WAIT 0", "320 02", "WAIT 100",
  "361 07 01", "WAIT 100", "361 06 0a 07 00 00", "WAIT 0", "361 01 0b 00 00 d0 07", "WAIT 0",
  "361 01 0b 02 00 20 03", "WAIT 0", "361 01 0b 01 00 dc 05", "WAIT 0", "361 01 12 01 00 20 03", "WAIT 0",
  "363 03 06 01 00 06 30", "WAIT 0", "363 03 06 00 00 ff ff", "WAIT 9", "361 01 1a 00 00 01 00", "WAIT 0",
  "361 06 01", "WAIT 0", "361 05 03 01 01 1e 00", "WAIT 0", "363 05 04 01 03 01 00", "WAIT 0",
  "361 03 05 00 00 80 00", "WAIT 0", "361 03 05 01 00 62 00", "WAIT 0", "320 2d 00 d7 ff", "WAIT 0",
  "363 03 06 04 00 00 00", "WAIT 0", "363 04 08 00 00 00 00", "WAIT 0", "361 04 06 00 01 b4 00", "WAIT 0",
  "361 03 06 02 00 30 0f", "WAIT 0", "363 08 01", "WAIT 0", "363 05 04 02 03 01 00", "WAIT 0",
  "361 04 0f 01 01 b8 0b", "WAIT 0", "361 06 08 00 00 04 00 00", "WAIT 0", "361 06 08 01 00 04 00 00", "WAIT 0",
  "361 05 03 00 00 1e 00", "WAIT 0", "361 05 03 00 01 1e 00", "WAIT 0", "361 05 03 01 00 1e 00", "WAIT 0",
  "363 04 08 03 01 01 00", "WAIT 18", "363 08 14 00", "WAIT 8", "363 08 01", "WAIT 0",
  "363 08 14 00", "WAIT 1447", "361 01 01 ff ff ff ff", "WAIT 0", "320 00", "WAIT 0",
  "361 04 07 02 00 00 00", "WAIT 1", "361 07 01", "WAIT 4", "361 04 10 02 02 fd 00", "WAIT 18",
  "363 05 01 00 00 01 00", "WAIT 14640", "361 07 01", "WAIT 0", "363 05 01 00 01 01 00", "WAIT 30",
  "363 05 01 00 02 01 00", "WAIT 30", "363 05 01 00 03 01 00", "WAIT 30", "363 05 01 00 04 01 00", "WAIT 30",
  "363 05 01 00 05 01 00", "WAIT 30", "363 05 01 00 06 01 00", "WAIT 30", "363 05 01 00 07 01 00",


};
static const size_t INIT1_N = sizeof(INIT1_SEQ) / sizeof(INIT1_SEQ[0]);
static uint32_t INIT1_DELAY_MS = 80;

static uint32_t INIT_GAP_MS = 5000;

static const char* INIT2_SEQ[] = {
/*  "320 30","WAIT 2000","320 30","WAIT 2000",
  "320 0d 00", "320 0e 00", "320 0c 00", "320 0e 01", "320 0d 01", "320 0d 02",
  "320 0e 02", "320 0c 01", "320 0e 03", "320 0d 03", "320 0d 04", "320 0e 04",
  "320 0c 02", "320 0e 05", "320 0d 05", "320 0d 06", "320 0e 06", "320 0c 03",
  "320 0e 07", "320 0d 07", "320 09", "320 26 01", "320 26 00", "320 09",
  "320 26 02", "320 26 03", "320 0b", "320 54 00", "320 54 01", "320 54 02",
  "320 54 03", "320 54 04", "320 54 05", "320 54 06", "320 54 07", "320 54 08",
  "320 54 09",
  "320 1c 00 08 00","WAIT 200","320 1c 00 00 00","WAIT 200","320 07","320 0e 00",
  "320 1c 01 08 00","WAIT 200","320 1c 01 00 00","WAIT 200","320 07","320 0e 01",
  "320 1c 02 08 00","WAIT 200","320 1c 02 00 00","WAIT 200","320 07","320 0e 02",
  "320 1c 03 08 00","WAIT 200","320 1c 03 00 00","WAIT 200","320 07","320 0e 03",
  "320 1c 04 08 00","WAIT 200","320 1c 04 00 00","WAIT 200","320 07","320 0e 04",
  "320 1c 05 08 00","WAIT 200","320 1c 05 00 00","WAIT 200","320 07","320 0e 05",
  "320 1c 06 08 00","WAIT 200","320 1c 06 00 00","WAIT 200","320 07","320 0e 06",
  "320 1c 07 08 00","WAIT 200","320 1c 07 00 00","WAIT 200","320 07","320 0e 07",
  "320 1c 08 08 00","WAIT 200","320 1c 08 00 00","WAIT 200","320 07","320 0e 08",
  "320 1c 09 08 00","WAIT 200","320 1c 09 00 00","WAIT 200","320 07","320 0e 09",*/

};
static const size_t INIT2_N = sizeof(INIT2_SEQ) / sizeof(INIT2_SEQ[0]);
static uint32_t INIT2_DELAY_MS = 200;

// ============================
// ===== SECUENCIA DE PRUEBA ===
// ============================
// EDITA SOLO ESTE ARREGLO para probar otra secuencia CAN.
//
// Formato aceptado:
//   "ID BYTE1 BYTE2 ... BYTE8"
//   "WAIT milisegundos"
//
// Ejecución:
//   Monitor serial (115200):  secuencia
//   Campo DO / HTTP:          secuencia
//
// Se ejecuta UNA SOLA VEZ mediante el motor SCRIPT no bloqueante.
// Para detenerla antes de terminar:
//   Serial:                   secuencia stop
//   Campo DO / HTTP:          secuencia_stop
//
// Si quieres agregar un retraso automático después de CADA trama,
// cambia SECUENCIA_DEFAULT_DELAY_MS. En 0 solo se respetan los WAIT
// escritos explícitamente en el arreglo.
static const char* SECUENCIA_SEQ[] = {
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 100",
  "363 04 01 02 00 00 80", "WAIT 50", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fd ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 77", "733 bb 06 04 00 00 00 32 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 122", "363 04 01 02 00 00 80", "WAIT 28",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fd ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 99",
  "733 bb 06 04 00 00 00 31 00", "WAIT 50", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 100", "363 04 01 02 00 00 80", "WAIT 50", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fd ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 75", "733 bb 06 04 00 00 00 31 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 100",
  "363 04 01 02 00 00 80", "WAIT 50", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fd ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 75", "733 bb 06 04 00 00 00 32 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 134", "363 04 01 02 00 00 80", "WAIT 15",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 112",
  "733 bb 06 04 00 00 00 32 00", "WAIT 37", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 100", "363 04 01 02 01 00 80", "WAIT 50", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fe ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 71", "733 bb 07 04 00 00 00 2b 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 100",
  "363 04 01 02 01 00 80", "WAIT 50", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fe ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 70", "733 bb 07 04 00 00 00 2b 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 100", "363 04 01 02 01 00 80", "WAIT 50",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fe ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 69",
  "733 bb 07 04 00 00 00 2b 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 121", "363 04 01 02 00 00 80", "WAIT 29", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fd ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 97", "733 bb 06 04 00 00 00 32 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 100",
  "363 04 01 02 00 00 80", "WAIT 50", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fd ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 77", "733 bb 06 04 00 00 00 31 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 100", "363 04 01 02 00 00 80", "WAIT 50",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fd ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 77",
  "733 bb 06 04 00 00 00 32 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 100", "363 04 01 02 01 00 80", "WAIT 50", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fe ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 71", "733 bb 07 04 00 00 00 2b 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 119",
  "363 04 01 02 01 00 80", "WAIT 31", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fe ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 89", "733 bb 07 04 00 00 00 2b 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0",
  "733 04 03 fc ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 104", "363 04 01 02 01 00 80", "WAIT 46",
  "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fe ff 00 00 00 00", "WAIT 0", "364 04 03 02 00 00 00", "WAIT 73",
  "733 bb 07 04 00 00 00 2b 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 150", "363 04 03 02 00 00 00", "WAIT 0", "733 04 03 fc ff 00 00 00 00", "WAIT 0",
  "364 04 03 02 00 00 00", "WAIT 100", "363 04 01 02 01 00 80", "WAIT 0", "363 04 01 02 00 00 80", "WAIT 121",
  "733 bb 07 04 00 00 00 2b 00", "WAIT 7", "733 bb 06 04 00 00 00 32 00", "WAIT 13225", "733 f0 00 bc 00 00 00",

};
static const size_t SECUENCIA_N =
  sizeof(SECUENCIA_SEQ) / sizeof(SECUENCIA_SEQ[0]);

static uint32_t SECUENCIA_DEFAULT_DELAY_MS = 0;

// ============================
// ===== Serial helpers =======
// ============================
static String readLineNonBlocking() {
  static String buf;
  while (Serial.available()) {
    char c = (char)Serial.read();
    if (c == '\r') continue;
    if (c == '\n') {
      String line = buf;
      buf = "";
      line.trim();
      return line;
    }
    buf += c;
    if (buf.length() > 300) buf.remove(0, buf.length() - 300);
  }
  return "";
}

static bool parseHexByte(const String &s, uint8_t &out) {
  String t = s;
  t.trim();
  if (t.startsWith("0x") || t.startsWith("0X")) t = t.substring(2);
  char *endp = nullptr;
  long v = strtol(t.c_str(), &endp, 16);
  if (endp == t.c_str() || *endp != '\0') return false;
  if (v < 0 || v > 255) return false;
  out = (uint8_t)v;
  return true;
}

static bool parseHexId(const String &s, uint32_t &out) {
  String t = s;
  t.trim();
  if (t.startsWith("0x") || t.startsWith("0X")) t = t.substring(2);
  char *endp = nullptr;
  long v = strtol(t.c_str(), &endp, 16);
  if (endp == t.c_str() || *endp != '\0') return false;
  if (v < 0 || v > 0x7FF) return false;
  out = (uint32_t)v;
  return true;
}

// ============================
// ===== CAN core =============
// ============================
static void can_start() {
  // Mantener el transceiver CAN siempre activo antes de iniciar TWAI.
  pinMode((int)CAN_STBY, OUTPUT);
  digitalWrite((int)CAN_STBY, LOW);   // LOW = modo normal / activo

  if (can_started) return;

  twai_general_config_t g = TWAI_GENERAL_CONFIG_DEFAULT(CAN_TX, CAN_RX, TWAI_MODE_NORMAL);
  // Para banco sin ACK:
  // twai_general_config_t g = TWAI_GENERAL_CONFIG_DEFAULT(CAN_TX, CAN_RX, TWAI_MODE_NO_ACK);

  g.tx_queue_len = 20;
  g.rx_queue_len = 50;

  twai_filter_config_t f = TWAI_FILTER_CONFIG_ACCEPT_ALL();

  esp_err_t err = twai_driver_install(&g, &TIMING, &f);
  if (err != ESP_OK) { Serial.printf("twai_driver_install ERROR: %d\n", (int)err); return; }

  err = twai_start();
  if (err != ESP_OK) { Serial.printf("twai_start ERROR: %d\n", (int)err); twai_driver_uninstall(); return; }

  can_started = true;
  Serial.println("CAN/TWAI STARTED OK (1Mbps)");
}

static void can_stop() {
  if (!can_started) return;
  twai_stop();
  twai_driver_uninstall();
  can_started = false;
  Serial.println("CAN/TWAI STOPPED");
}

static void print_rx(const twai_message_t &m) {
  Serial.printf("[RX %lu us] ID=0x%03X %s DLC=%d DATA=",
                (unsigned long)micros(),
                (unsigned)m.identifier,
                m.rtr ? "RTR" : "DAT",
                (int)m.data_length_code);
  for (int i = 0; i < m.data_length_code; i++) {
    Serial.printf("%02X", m.data[i]);
    if (i + 1 < m.data_length_code) Serial.print(" ");
  }
  Serial.println();
}

static void print_tx(uint32_t id, const uint8_t *data, uint8_t dlc, bool ok, int err) {
  if (ok) {
    Serial.printf("[TX %lu us] ID=0x%03lX DLC=%u DATA=",
                  (unsigned long)micros(), (unsigned long)id, (unsigned)dlc);
    for (int i = 0; i < dlc; i++) {
      Serial.printf("%02X", data[i]);
      if (i + 1 < dlc) Serial.print(" ");
    }
    Serial.println();
  } else {
    Serial.printf("[TX FAIL %lu us] err=%d (sin ACK / bus / bitrate)\n",
                  (unsigned long)micros(), err);
  }
}

static void send_frame_std(uint32_t id, const uint8_t *data, uint8_t dlc) {
  if (!can_started) { Serial.println("CAN no iniciado."); return; }
  if (dlc > 8) dlc = 8;

  twai_message_t msg = {};
  msg.identifier = id;
  msg.extd = 0;
  msg.rtr  = 0;
  msg.data_length_code = dlc;
  for (int i = 0; i < dlc; i++) msg.data[i] = data[i];

  esp_err_t err = twai_transmit(&msg, pdMS_TO_TICKS(50));
  print_tx(id, data, dlc, (err == ESP_OK), (int)err);
}

// ============================================================================
// ====================== TESTEO v2 (ENCLAVADO + REARME) ======================
// ============================================================================
static constexpr uint8_t T_IDLE=0, T_RUNNING=1, T_DONE=2, T_ERROR=3;

struct TesteoCtx {
  uint8_t  st = T_IDLE;
  uint32_t run_id = 0;

  bool waiting = false;
  uint32_t next_ping_ms = 0;
  uint32_t wait_deadline_ms = 0;

  uint16_t tries = 0;
  uint16_t max_tries = 25;

  bool got_first = false;
  uint8_t first_code = 0;
  uint8_t last_code  = 0;

  uint16_t a2_cnt = 0;
  uint16_t a1_cnt = 0;

  char result[96] = "IDLE";
};

struct TesteoPublic {
  uint32_t run_id = 0;
  uint8_t  state = 0;
  uint8_t  armed = 1;
  uint16_t tries = 0;
  uint8_t  last_code = 0;
  uint32_t t_ms = 0;
  char result[96] = "IDLE";
};

static TesteoCtx g_t;
static TesteoPublic g_tpub;
static portMUX_TYPE g_tpubMux = portMUX_INITIALIZER_UNLOCKED;

static bool g_test_armed = true;
static bool g_test_latched = false;
static char g_test_latched_msg[96] = "SIN TEST";
static uint32_t g_last_reset_ms = 0;

static void testeo_pub_update() {
  portENTER_CRITICAL(&g_tpubMux);
  g_tpub.run_id = g_t.run_id;
  g_tpub.state  = g_t.st;
  g_tpub.armed  = g_test_armed ? 1 : 0;
  g_tpub.tries  = g_t.tries;
  g_tpub.last_code = g_t.last_code;
  g_tpub.t_ms   = millis();
  strlcpy(g_tpub.result, g_t.result, sizeof(g_tpub.result));
  portEXIT_CRITICAL(&g_tpubMux);
}

static void testeo_finish(uint8_t st, const char* msg) {
  g_t.st = st;
  g_t.waiting = false;
  g_t.next_ping_ms = 0;
  g_t.wait_deadline_ms = 0;

  strlcpy(g_t.result, msg ? msg : "DONE", sizeof(g_t.result));
  strlcpy(g_test_latched_msg, g_t.result, sizeof(g_test_latched_msg));
  g_test_latched = true;

  g_test_armed = false;

  testeo_pub_update();

  Serial.print("TESTEO => ");
  Serial.println(g_t.result);
}

static void testeo_send_ping_320_07() {
  uint8_t d[1] = { 0x07 };
  send_frame_std(0x320, d, 1);

  g_t.tries++;
  g_t.waiting = true;
  g_t.wait_deadline_ms = millis() + 300;
  testeo_pub_update();
}

static void testeo_start() {
  if (!can_started) { Serial.println("TESTEO: CAN no iniciado."); return; }

  if (!g_test_armed) {
    Serial.print("TESTEO (ENCLAVADO): ");
    Serial.println(g_test_latched ? g_test_latched_msg : "SIN RESULTADO");
    return;
  }

  if (g_t.st == T_RUNNING) {
    Serial.println("TESTEO: ya corriendo.");
    return;
  }

  static uint32_t rid = 0;
  g_t = TesteoCtx();
  g_t.st = T_RUNNING;
  g_t.run_id = ++rid;
  strlcpy(g_t.result, "RUNNING", sizeof(g_t.result));
  g_t.next_ping_ms = millis();
  testeo_pub_update();

  Serial.println("TESTEO: start (envia 320 07 y analiza 0x700...)");
}

static void testeo_watch_reset_702_fields(uint32_t id, uint8_t dlc, const uint8_t *data, uint8_t extd, uint8_t rtr) {
  if (extd || rtr) return;
  if (id != 0x702) return;
  if (dlc < 2) return;
  if (data[0] != 0x3F || data[1] != 0x00) return;

  uint32_t now = millis();
  if (now - g_last_reset_ms < 250) return;
  g_last_reset_ms = now;

  g_t = TesteoCtx();
  g_t.st = T_IDLE;
  strlcpy(g_t.result, "ARMED (reset 702 3F 00)", sizeof(g_t.result));

  g_test_armed = true;
  g_test_latched = false;
  strlcpy(g_test_latched_msg, "SIN TEST (rearmado)", sizeof(g_test_latched_msg));

  testeo_pub_update();
  Serial.println("TESTEO: reset 702 3F 00 -> habilitado nuevo test.");
}

static void testeo_on_rx_700_fields(uint32_t id, uint8_t dlc, const uint8_t *data, uint8_t extd, uint8_t rtr) {
  if (g_t.st != T_RUNNING) return;
  if (extd || rtr) return;
  if (id != 0x700) return;
  if (dlc < 1) return;

  uint8_t code = data[0];
  g_t.last_code = code;

  if (!g_t.got_first) { g_t.got_first = true; g_t.first_code = code; }
  if (code == 0xA2) g_t.a2_cnt++;
  if (code == 0xA1) g_t.a1_cnt++;

  if (code == 0xCB) { testeo_finish(T_DONE, "OK: placas del cabezal presentes (CB)"); return; }
  if (code == 0xBC) { testeo_finish(T_DONE, "FALTA: placa 3 de expansion (BC)"); return; }

  if (code == 0xBF) {
    if (g_t.first_code == 0xA2) {
      if (g_t.a2_cnt <= 1) testeo_finish(T_DONE, "FALTA: ambas placas de fuerza (A2->BF)");
      else                 testeo_finish(T_DONE, "FALTA: placa 2 (A2 repetido -> BF)");
    } else if (g_t.first_code == 0xA1) {
      if (g_t.a1_cnt <= 1) testeo_finish(T_DONE, "FALTA: ambas placas de fuerza (A1->BF)");
      else                 testeo_finish(T_DONE, "FALTA: placa 1 (A1 repetido -> BF)");
    } else {
      testeo_finish(T_DONE, "BF: faltan placas (patron no clasificado)");
    }
    return;
  }

  g_t.waiting = false;
  g_t.next_ping_ms = millis() + 60;
  testeo_pub_update();
}

static void testeo_on_any_rx_fields(uint32_t id, uint8_t dlc, const uint8_t *data, uint8_t extd, uint8_t rtr) {
  testeo_watch_reset_702_fields(id, dlc, data, extd, rtr);
  testeo_on_rx_700_fields(id, dlc, data, extd, rtr);
}

static void testeo_tick() {
  if (g_t.st != T_RUNNING) return;

  uint32_t now = millis();

  if (g_t.waiting) {
    if ((int32_t)(now - g_t.wait_deadline_ms) >= 0) {
      testeo_finish(T_ERROR, "ERROR: sin respuesta 0x700 (timeout).");
    }
    return;
  }

  if ((int32_t)(now - g_t.next_ping_ms) < 0) return;

  if (g_t.tries >= g_t.max_tries) {
    if (g_t.a2_cnt && g_t.a1_cnt)       testeo_finish(T_DONE, "INCONCLUSO: A1/A2 sin BC/BF.");
    else if (g_t.a2_cnt && !g_t.a1_cnt) testeo_finish(T_DONE, "INCONCLUSO: A2 repetido sin BF/BC.");
    else                                testeo_finish(T_DONE, "INCONCLUSO: sin patron valido.");
    return;
  }

  testeo_send_ping_320_07();
}

// ======== Parser para líneas tipo: "370 FD 06 11 00" o "WAIT 2000" ========
static bool is_wait_line(const char* line, uint32_t &ms_out) {
  if (!line) return false;
  while (*line == ' ') line++;

  if (!((line[0]=='W'||line[0]=='w') &&
        (line[1]=='A'||line[1]=='a') &&
        (line[2]=='I'||line[2]=='i') &&
        (line[3]=='T'||line[3]=='t'))) return false;

  line += 4;
  while (*line == ' ') line++;

  char *endp = nullptr;
  long v = strtol(line, &endp, 10);
  if (endp == line) return false;
  if (v < 0) v = 0;
  ms_out = (uint32_t)v;
  return true;
}

static bool send_line_as_frame(const char* line) {
  if (!line) return false;

  uint32_t wms = 0;
  if (is_wait_line(line, wms)) {
    delay(wms);
    return true;
  }

  char buf[128];
  size_t L = strnlen(line, sizeof(buf) - 1);
  memcpy(buf, line, L);
  buf[L] = '\0';

  char* tok[12] = {0};
  int n = 0;

  char* p = buf;
  while (*p && n < 12) {
    while (*p == ' ') p++;
    if (!*p) break;
    tok[n++] = p;
    while (*p && *p != ' ') p++;
    if (*p) { *p = '\0'; p++; }
  }
  if (n < 1) return false;

  uint32_t id = 0;
  { String sid(tok[0]); if (!parseHexId(sid, id)) return false; }

  uint8_t data[8] = {0};
  uint8_t dlc = 0;
  for (int i = 1; i < n && dlc < 8; i++) {
    String sb(tok[i]);
    uint8_t b;
    if (!parseHexByte(sb, b)) return false;
    data[dlc++] = b;
  }

  // Protección del Yarn 1: este cabezal solo tiene canales CAN 0x00..0x05.
  // El dashboard envía los botones manuales como tramas CAN directas, por eso
  // la validación también se aplica aquí y no solo dentro de yarn_send_state().
  const bool isYarn1Frame =
      id == YARN1_CAN_ID &&
      dlc >= 6 &&
      data[0] == 0x05 &&
      data[1] == 0x01 &&
      data[2] == 0x00;

  if (isYarn1Frame && data[3] >= YARN_CHANNEL_COUNT) {
    Serial.printf("YARN1 CANAL INVALIDO: 0x%02X (permitidos 0x00..0x%02X)\n",
                  data[3],
                  (uint8_t)(YARN_CHANNEL_COUNT - 1));
    return false;
  }

  send_frame_std(id, data, dlc);
  return true;
}

// ===================== INIT RUNNER (bloqueante OK) =====================
static bool run_list_with_default_delay(const char* const *seq, size_t n,
                                       uint32_t default_delay_ms,
                                       const char* tag,
                                       uint16_t offset,
                                       uint16_t total) {
  Serial.printf("%s START: %u items, default delay=%lu ms\n",
                tag, (unsigned)n, (unsigned long)default_delay_ms);

  for (size_t i = 0; i < n; i++) {
    const char* ln = seq[i];

    // ✅ publica progreso GLOBAL (offset+i+1 / total)
    char mini[96];
    snprintf(mini, sizeof(mini), "%s", ln ? ln : "");
    init_pub_step(tag, (uint16_t)(offset + i + 1), total, mini);

    uint32_t wms = 0;
    if (is_wait_line(ln, wms)) {
      Serial.printf("%s WAIT %lu ms (item %u)\n", tag, (unsigned long)wms, (unsigned)i);
      delay(wms);
      continue;
    }

    if (!send_line_as_frame(ln)) {
      Serial.printf("%s PARSE ERROR en item %u: %s\n", tag, (unsigned)i, ln);
      init_pub_finish(I_ERROR, tag, "PARSE ERROR");
      return false;
    }
    if (default_delay_ms) delay(default_delay_ms);
  }

  Serial.printf("%s DONE.\n", tag);
  return true;
}

static void run_init_sequence() {
  if (!can_started) { Serial.println("CAN no iniciado."); init_pub_finish(I_ERROR, "INIT", "CAN NOT STARTED"); return; }

  const uint16_t total = (uint16_t)(INIT1_N + INIT2_N);
  init_pub_begin(total);

  // INIT1
  if (!run_list_with_default_delay(INIT1_SEQ, INIT1_N, INIT1_DELAY_MS, "INIT1", 0, total)) {
    init_pub_finish(I_ERROR, "INIT1", "FAILED");
    return;
  }

  // GAP
  {
    char msg[96];
    snprintf(msg, sizeof(msg), "GAP WAIT %lu ms", (unsigned long)INIT_GAP_MS);
    init_pub_step("GAP", (uint16_t)INIT1_N, total, msg);
  }
  Serial.printf("GAP WAIT %lu ms...\n", (unsigned long)INIT_GAP_MS);
  delay(INIT_GAP_MS);

  // INIT2
  if (!run_list_with_default_delay(INIT2_SEQ, INIT2_N, INIT2_DELAY_MS, "INIT2", (uint16_t)INIT1_N, total)) {
    init_pub_finish(I_ERROR, "INIT2", "FAILED");
    return;
  }

  Serial.println("INIT DONE.");
  init_pub_finish(I_DONE, "INIT", "DONE");
}

// ============================
// ===== SCRIPT/ANIM ENGINE ====  (NO bloqueante)
// ============================
enum ScriptOpType : uint8_t { OP_SEND = 0, OP_WAIT = 1 };

struct ScriptOp {
  ScriptOpType type;
  const char*  line;
  uint32_t     wait_ms;
};

static constexpr size_t SCRIPT_MAX = 2048;
static ScriptOp script[SCRIPT_MAX];
static size_t script_len = 0;

static bool script_running = false;
static bool script_loop = false;
static size_t script_i = 0;
static uint32_t script_wait_until_ms = 0;

static void scriptClear() { script_len = 0; }
static void scriptBegin() { scriptClear(); }
static void scriptEnd()   {}

static bool scriptAddSend(const char* line) {
  if (script_len >= SCRIPT_MAX) { Serial.printf("SCRIPT OVERFLOW (%u). No entra: %s\n", (unsigned)SCRIPT_MAX, line ? line : "(null)"); return false; }
  script[script_len++] = { OP_SEND, line, 0 };
  return true;
}
static bool scriptAddWait(uint32_t ms) {
  if (script_len >= SCRIPT_MAX) { Serial.printf("SCRIPT OVERFLOW (%u). No entra WAIT %lu\n", (unsigned)SCRIPT_MAX, (unsigned long)ms); return false; }
  script[script_len++] = { OP_WAIT, nullptr, ms };
  return true;
}

static void scriptStart(bool loop) {
  if (script_len == 0) { Serial.println("SCRIPT vacio."); return; }
  script_running = true;
  script_loop = loop;
  script_i = 0;
  script_wait_until_ms = 0;
  Serial.printf("SCRIPT START (len=%u) loop=%d\n", (unsigned)script_len, (int)loop);
}

static void scriptStop() {
  script_running = false;
  Serial.println("SCRIPT STOP");
}

static bool build_sequence_script() {
  scriptBegin();

  for (size_t i = 0; i < SECUENCIA_N; i++) {
    const char* line = SECUENCIA_SEQ[i];

    uint32_t wait_ms = 0;
    if (is_wait_line(line, wait_ms)) {
      if (!scriptAddWait(wait_ms)) {
        Serial.printf("SECUENCIA: no se pudo agregar WAIT en item %u\n",
                      (unsigned)i);
        scriptClear();
        return false;
      }
      continue;
    }

    if (!scriptAddSend(line)) {
      Serial.printf("SECUENCIA: no se pudo agregar trama en item %u: %s\n",
                    (unsigned)i,
                    line ? line : "(null)");
      scriptClear();
      return false;
    }

    if (SECUENCIA_DEFAULT_DELAY_MS > 0) {
      if (!scriptAddWait(SECUENCIA_DEFAULT_DELAY_MS)) {
        Serial.printf("SECUENCIA: no se pudo agregar delay automatico en item %u\n",
                      (unsigned)i);
        scriptClear();
        return false;
      }
    }
  }

  scriptEnd();

  Serial.printf(
    "SECUENCIA CARGADA: %u items de arreglo -> %u operaciones; delay automatico=%lu ms\n",
    (unsigned)SECUENCIA_N,
    (unsigned)script_len,
    (unsigned long)SECUENCIA_DEFAULT_DELAY_MS
  );

  return script_len > 0;
}

static void sequenceStartOnce() {
  if (!can_started) {
    Serial.println("SECUENCIA: CAN no iniciado.");
    return;
  }

  // Si había otra animación/script activo, se reemplaza de forma explícita.
  if (script_running) {
    scriptStop();
  }

  if (!build_sequence_script()) {
    Serial.println("SECUENCIA: arreglo vacío o error al cargar.");
    return;
  }

  // false = ejecutar una sola vez, sin bucle.
  scriptStart(false);
  Serial.println("SECUENCIA: ejecución única iniciada.");
}

static void scriptTick() {
  if (!script_running) return;
  if (!can_started) return;

  uint32_t now = millis();
  if (script_wait_until_ms && (int32_t)(now - script_wait_until_ms) < 0) return;
  script_wait_until_ms = 0;

  while (script_running) {
    if (script_i >= script_len) {
      if (script_loop) { script_i = 0; continue; }
      script_running = false;
      Serial.println("SCRIPT DONE");
      return;
    }

    ScriptOp &op = script[script_i++];

    if (op.type == OP_SEND) {
      if (!send_line_as_frame(op.line)) {
        Serial.print("SCRIPT PARSE ERROR: ");
        Serial.println(op.line ? op.line : "(null)");
        script_running = false;
        return;
      }
      continue;
    }

    if (op.type == OP_WAIT) {
      script_wait_until_ms = now + op.wait_ms;
      return;
    }
  }
}

static void build_my_animation_script() {
  scriptBegin();
  // tu anim acá si quieres
  scriptEnd();
}

// ============================
// ===== CORE SPLIT + QUEUE ====
// ============================
TaskHandle_t canTaskHandle = nullptr;

enum CmdType : uint8_t { CMD_DO = 0, CMD_SEND_LINE = 1 };

struct CmdMsg {
  CmdType type;
  char    payload[180];
};

static QueueHandle_t cmdQ = nullptr;

static bool enqueueDo(const char* s) {
  if (!cmdQ) return false;
  CmdMsg m = {};
  m.type = CMD_DO;
  strlcpy(m.payload, s ? s : "", sizeof(m.payload));
  return (xQueueSend(cmdQ, &m, 0) == pdTRUE);
}

static bool enqueueSendLine(const char* line) {
  if (!cmdQ) return false;
  CmdMsg m = {};
  m.type = CMD_SEND_LINE;
  strlcpy(m.payload, line ? line : "", sizeof(m.payload));
  return (xQueueSend(cmdQ, &m, 0) == pdTRUE);
}

// ============================
// ===== RUN ENGINE (NO bloqueante) =====
// ============================

// J regs (invertido): bit=0 ON, bit=1 OFF
static uint8_t j_regs[8] = {0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF};

// Mapa físico usado por la interfaz actual:
// Visual J1 -> físico J1
// Visual J2 -> físico J2
// Visual J3 -> físico J5
// Visual J4 -> físico J6
static const uint8_t ACTIVE_J[] = { 1, 2, 5, 6 };
static constexpr size_t ACTIVE_J_N = sizeof(ACTIVE_J) / sizeof(ACTIVE_J[0]);

static bool is_active_j(uint8_t j){
  for (size_t i = 0; i < ACTIVE_J_N; i++) {
    if (ACTIVE_J[i] == j) return true;
  }
  return false;
}

static inline void j_set_pin(uint8_t j1to8, uint8_t pin1to8, bool on){
  if (j1to8 < 1 || j1to8 > 8) return;
  if (pin1to8 < 1 || pin1to8 > 8) return;
  uint8_t &r = j_regs[j1to8-1];
  uint8_t b = pin1to8 - 1;
  if (on) r &= ~(1u<<b); else r |= (1u<<b);
}

static inline void can_send_j(uint8_t j1to8) {
  if (j1to8 < 1 || j1to8 > 8) return;

  // Mapa real confirmado de J1..J8
  static const uint32_t J_CAN_ID[8] = {
    0x363,  // J1
    0x363,  // J2
    0x364,  // J3
    0x364,  // J4
    0x363,  // J5
    0x363,  // J6
    0x364,  // J7
    0x364   // J8
  };

  static const uint8_t J_SELECTOR[8] = {
    0x00,  // J1
    0x01,  // J2
    0x00,  // J3
    0x01,  // J4
    0x02,  // J5
    0x03,  // J6
    0x02,  // J7
    0x03   // J8
  };

  const uint8_t idx = j1to8 - 1;
  const uint8_t xx  = j_regs[idx];

  // Formato real:
  // ID 06 03 SELECTOR 00 MASCARA 00
  uint8_t data[6] = {
    0x06,
    0x03,
    J_SELECTOR[idx],
    0x00,
    xx,
    0x00
  };

  send_frame_std(J_CAN_ID[idx], data, 6);
}

// ==========================================================
// YARN 1: actuador de seis canales
//
// Formato real:
//   ID 05 01 00 CANAL 00 ESTADO
//
// Mapa activo:
//   Yarn 1 -> ID 0x363
//
// Yarn 2 queda deshabilitado en este cabezal.
//
// CANAL:
//   0x00..0x05
//
// ESTADO:
//   0x01 = activado
//   0x00 = desactivado
// ==========================================================

static void yarn_send_state(uint8_t yarn1to2, uint8_t channel1to6, bool on){
  // Este cabezal solo admite Yarn 1.
  if (yarn1to2 != ACTIVE_YARN) return;
  if (channel1to6 < 1 || channel1to6 > YARN_CHANNEL_COUNT) return;

  const uint8_t channel = channel1to6 - 1;

  uint8_t data[6] = {
    0x05,
    0x01,
    0x00,
    channel,
    0x00,
    (uint8_t)(on ? 0x01 : 0x00)
  };

  send_frame_std(YARN1_CAN_ID, data, 6);
}

// ==========================================================
// DEN 1..8: motores de densidad con cinco posiciones
//
// Formato real:
//   ID 03 03 SELECTOR 00 POS_L POS_H
//
// Mapa confirmado:
//   DEN 1 -> ID 0x363, selector 0x00
//   DEN 2 -> ID 0x363, selector 0x01
//   DEN 3 -> ID 0x364, selector 0x00
//   DEN 4 -> ID 0x364, selector 0x01
//   DEN 5 -> ID 0x363, selector 0x03
//   DEN 6 -> ID 0x363, selector 0x02
//   DEN 7 -> ID 0x364, selector 0x03
//   DEN 8 -> ID 0x364, selector 0x02
//
// Posiciones int16 little-endian usadas por el HTML actual:
//   POS 1 =   0 -> 00 00
//   POS 2 = 162 -> A2 00
//   POS 3 = 325 -> 45 01
//   POS 4 = 487 -> E7 01
//   POS 5 = 650 -> 8A 02
//
// RUN:
//   0 -> 162 -> 325 -> 487 -> 650 -> repetir
// ==========================================================
static const uint32_t DEN_CAN_ID[8] = {
  0x363,  // DEN 1
  0x363,  // DEN 2
  0x364,  // DEN 3
  0x364,  // DEN 4
  0x363,  // DEN 5
  0x363,  // DEN 6
  0x364,  // DEN 7
  0x364   // DEN 8
};

static const uint8_t DEN_SELECTOR[8] = {
  0x00,  // DEN 1
  0x01,  // DEN 2
  0x00,  // DEN 3
  0x01,  // DEN 4
  0x03,  // DEN 5
  0x02,  // DEN 6
  0x03,  // DEN 7
  0x02   // DEN 8
};

// Mapa físico usado por la interfaz actual:
// Visual DEN1 -> físico DEN1
// Visual DEN2 -> físico DEN2
// Visual DEN3 -> físico DEN5
// Visual DEN4 -> físico DEN6
static const uint8_t ACTIVE_DEN[] = { 1, 2, 5, 6 };
static constexpr size_t ACTIVE_DEN_N = sizeof(ACTIVE_DEN) / sizeof(ACTIVE_DEN[0]);

static bool is_active_den(uint8_t den){
  for (size_t i = 0; i < ACTIVE_DEN_N; i++) {
    if (ACTIVE_DEN[i] == den) return true;
  }
  return false;
}

static const int16_t DEN_POSITIONS[5] = {
    0,
  162,
  325,
  487,
  650
};

// Debe coincidir con DEN_RUN_MS del HTML.
static constexpr uint16_t DEN_RUN_DELAY_MS = 300;

static void den_send_position(uint8_t den1to8, uint8_t position1to5){
  if (den1to8 < 1 || den1to8 > 8) return;
  if (position1to5 < 1 || position1to5 > 5) return;

  const uint8_t denIdx = den1to8 - 1;
  const int16_t position = DEN_POSITIONS[position1to5 - 1];

  const uint16_t raw = (uint16_t)position;
  const uint8_t lo = (uint8_t)(raw & 0xFF);
  const uint8_t hi = (uint8_t)((raw >> 8) & 0xFF);

  uint8_t data[6] = {
    0x03,
    0x03,
    DEN_SELECTOR[denIdx],
    0x00,
    lo,
    hi
  };

  send_frame_std(DEN_CAN_ID[denIdx], data, 6);
}

// ==========================================================
// SIC 1..2: motores de sincronismo con cuatro posiciones
//
// Formato posición:
//   ID 04 01 00 SELECTOR POS_L POS_H 00 00
//
// Formato sincronismo especial:
//   ID 04 01 00 SELECTOR 00 00 10 00
//
// Mapa:
//   SIC 1 -> ID 0x363, selector 0x00
//   SIC 2 -> ID 0x364, selector 0x01
//
// Posiciones int16 little-endian:
//   POS 1 = +180  -> B4 00
//   POS 2 = +360  -> 68 01
//   POS 3 = -180  -> 4C FF
//   POS 4 = -360  -> 98 FE
//
// RUN observado:
//   POS1,POS2,POS3,POS4 x 3 vueltas
//   -> comando SYNC
//   -> repetir
// ==========================================================
static const uint32_t SIC_CAN_ID[2] = {
  0x363,  // SIC 1
  0x364   // SIC 2
};

static const uint8_t SIC_SELECTOR[2] = {
  0x00,  // SIC 1
  0x01   // SIC 2
};

static const int16_t SIC_POSITIONS[4] = {
   180,
   360,
  -180,
  -360
};

static constexpr uint16_t SIC_RUN_DELAY_MS = 300;
static constexpr uint8_t SIC_CYCLES_BEFORE_SYNC = 3;

static void sic_send_position(uint8_t sic1to2, uint8_t position1to4){
  if (sic1to2 < 1 || sic1to2 > 2) return;
  if (position1to4 < 1 || position1to4 > 4) return;

  const uint8_t sicIdx = sic1to2 - 1;
  const int16_t position = SIC_POSITIONS[position1to4 - 1];

  const uint16_t raw = (uint16_t)position;
  const uint8_t lo = (uint8_t)(raw & 0xFF);
  const uint8_t hi = (uint8_t)((raw >> 8) & 0xFF);

  uint8_t data[8] = {
    0x04,
    0x01,
    0x00,
    SIC_SELECTOR[sicIdx],
    lo,
    hi,
    0x00,
    0x00
  };

  send_frame_std(SIC_CAN_ID[sicIdx], data, 8);
}

static void sic_send_sync(uint8_t sic1to2){
  if (sic1to2 < 1 || sic1to2 > 2) return;

  const uint8_t sicIdx = sic1to2 - 1;

  uint8_t data[8] = {
    0x04,
    0x01,
    0x00,
    SIC_SELECTOR[sicIdx],
    0x00,
    0x00,
    0x10,
    0x00
  };

  send_frame_std(SIC_CAN_ID[sicIdx], data, 8);
}

// ==========================================================
// STITCH 1..2: motores rotatorios de cinco posiciones
//
// Secuencia RUN:
//   POS1 -> POS2 -> POS3 -> POS4 -> POS5 -> RESET -> repetir
//
// Mapa activo:
//   Stitch 1 -> ID 0x363, selector 0x00
//   Stitch 2 -> ID 0x363, selector 0x01
//
// Stitch 3 y Stitch 4 quedan deshabilitados en este cabezal.
// ==========================================================
static constexpr uint8_t ACTIVE_STITCH_N = 2;

static const uint32_t STITCH_CAN_ID[ACTIVE_STITCH_N] = {
  0x363,  // Stitch 1
  0x363   // Stitch 2
};

static const uint8_t STITCH_SELECTOR[ACTIVE_STITCH_N] = {
  0x00,  // Stitch 1
  0x01   // Stitch 2
};

static bool is_active_stitch(uint8_t stitch){
  return stitch >= 1 && stitch <= ACTIVE_STITCH_N;
}

// Cada fila contiene:
// POS_L, POS_H, TIPO_L, TIPO_H
static const uint8_t STITCH_MOVES[5][4] = {
  { 0x00, 0x00, 0x04, 0x00 },  // POS 1 =   0
  { 0x40, 0x01, 0x04, 0x00 },  // POS 2 = 320
  { 0xE0, 0x01, 0x04, 0x00 },  // POS 3 = 480
  { 0xA0, 0x00, 0x04, 0x00 },  // POS 4 = 160
  { 0x80, 0x02, 0x05, 0x00 }   // POS 5 = 640
};

// Velocidad de la rutina Stitch.
// Debe coincidir con el periodo visual utilizado por el HTML.
static constexpr uint16_t STITCH_RUN_DELAY_MS = 120;

static void stitch_send_reset(uint8_t stitch1to4){
  if (!is_active_stitch(stitch1to4)) return;

  const uint8_t idx = stitch1to4 - 1;

  uint8_t data[6] = {
    0x04,
    0x02,
    0x01,
    STITCH_SELECTOR[idx],
    0x01,
    0x00
  };

  send_frame_std(STITCH_CAN_ID[idx], data, 6);
}

static void stitch_send_position(uint8_t stitch1to4, uint8_t position1to5){
  if (!is_active_stitch(stitch1to4)) return;
  if (position1to5 < 1 || position1to5 > 5) return;

  const uint8_t stitchIdx = stitch1to4 - 1;
  const uint8_t moveIdx   = position1to5 - 1;

  uint8_t data[8] = {
    0x04,
    0x01,
    0x01,
    STITCH_SELECTOR[stitchIdx],
    STITCH_MOVES[moveIdx][0],
    STITCH_MOVES[moveIdx][1],
    STITCH_MOVES[moveIdx][2],
    STITCH_MOVES[moveIdx][3]
  };

  send_frame_std(STITCH_CAN_ID[stitchIdx], data, 8);
}

// Cascades
static Cascade runJ[8];
static Cascade runY1;
static Cascade runDen[8];
static Cascade runSic[2];
static Cascade runS[ACTIVE_STITCH_N];

static void cascade_start(Cascade &c, uint16_t delay_ms){
  if (c.running) return;
  c.running = true;
  c.phase = 0;
  c.p = 1;
  c.delay_ms = delay_ms;
  c.next_ms = millis();
}

static void cascade_stop(Cascade &c){
  c.running = false;
}

static void tick_run_j(uint8_t j1to8){
  Cascade &c = runJ[j1to8-1];
  if (!c.running) return;

  uint32_t now = millis();
  if ((int32_t)(now - c.next_ms) < 0) return;
  c.next_ms = now + c.delay_ms;

  if (c.phase == 0) {
    j_set_pin(j1to8, c.p, true);
    can_send_j(j1to8);
    c.p++;
    if (c.p > 8) { c.phase = 1; c.p = 1; }
  } else {
    j_set_pin(j1to8, c.p, false);
    can_send_j(j1to8);
    c.p++;
    if (c.p > 8) { c.phase = 0; c.p = 1; }
  }
}

static void tick_run_yarn(Cascade &c, uint8_t yarn1to2){
  if (!c.running) return;
  if (yarn1to2 != ACTIVE_YARN) return;

  const uint32_t now = millis();
  if ((int32_t)(now - c.next_ms) < 0) return;

  c.next_ms = now + c.delay_ms;

  if (c.phase == 0) {
    // Activa canales 1..6 de manera secuencial.
    yarn_send_state(yarn1to2, c.p, true);
    c.p++;

    if (c.p > YARN_CHANNEL_COUNT) {
      c.phase = 1;
      c.p = 1;
    }
  } else {
    // Desactiva canales 1..6 de manera secuencial.
    yarn_send_state(yarn1to2, c.p, false);
    c.p++;

    if (c.p > YARN_CHANNEL_COUNT) {
      c.phase = 0;
      c.p = 1;
    }
  }
}

static void den_run_start(uint8_t den1to8){
  if (den1to8 < 1 || den1to8 > 8) return;

  Cascade &c = runDen[den1to8 - 1];

  cascade_stop(c);
  c.running = true;
  c.phase = 0;
  c.p = 1;  // El primer paso es POS 1 = 0.
  c.delay_ms = DEN_RUN_DELAY_MS;
  c.next_ms = millis();
}

static void tick_run_den(uint8_t den1to8){
  if (den1to8 < 1 || den1to8 > 8) return;

  Cascade &c = runDen[den1to8 - 1];
  if (!c.running) return;

  const uint32_t now = millis();
  if ((int32_t)(now - c.next_ms) < 0) return;

  c.next_ms = now + c.delay_ms;

  den_send_position(den1to8, c.p);

  c.p++;

  if (c.p > 5) {
    c.p = 1;
  }
}

static void sic_run_start(uint8_t sic1to2){
  if (sic1to2 < 1 || sic1to2 > 2) return;

  Cascade &c = runSic[sic1to2 - 1];

  cascade_stop(c);
  c.running = true;

  // phase = número de vueltas completas realizadas: 0..3
  c.phase = 0;

  // p = posición actual: 1..4
  c.p = 1;

  c.delay_ms = SIC_RUN_DELAY_MS;
  c.next_ms = millis();
}

static void tick_run_sic(uint8_t sic1to2){
  if (sic1to2 < 1 || sic1to2 > 2) return;

  Cascade &c = runSic[sic1to2 - 1];
  if (!c.running) return;

  const uint32_t now = millis();
  if ((int32_t)(now - c.next_ms) < 0) return;

  c.next_ms = now + c.delay_ms;

  // Tres vueltas completas de POS1..POS4.
  if (c.phase < SIC_CYCLES_BEFORE_SYNC) {
    sic_send_position(sic1to2, c.p);

    c.p++;

    if (c.p > 4) {
      c.p = 1;
      c.phase++;
    }

    return;
  }

  // Después de las tres vueltas se manda el sincronismo especial.
  sic_send_sync(sic1to2);

  c.phase = 0;
  c.p = 1;
}

static void stitch_run_start(uint8_t stitch1to4){
  if (!is_active_stitch(stitch1to4)) return;

  Cascade &c = runS[stitch1to4 - 1];

  cascade_stop(c);
  c.running = true;
  c.phase = 0;
  c.p = 1;  // El primer paso del RUN es POS 1.
  c.delay_ms = STITCH_RUN_DELAY_MS;
  c.next_ms = millis();
}

static void tick_run_stitch(uint8_t stitch1to4){
  if (!is_active_stitch(stitch1to4)) return;

  Cascade &c = runS[stitch1to4 - 1];
  if (!c.running) return;

  const uint32_t now = millis();
  if ((int32_t)(now - c.next_ms) < 0) return;

  c.next_ms = now + c.delay_ms;

  // p=1..5 -> posiciones físicas.
  // p=6    -> RESET.
  if (c.p >= 1 && c.p <= 5) {
    stitch_send_position(stitch1to4, c.p);
  } else {
    stitch_send_reset(stitch1to4);
  }

  c.p++;

  if (c.p > 6) {
    c.p = 1;
  }
}

// ====================== SERVO DRIVES 1 y 2 ======================
//
// SERVO 1 = POSICIONAMIENTO ABSOLUTO + SECUENCIA EN FIRMWARE
//   GPIO17 -> PULSE
//   GPIO18 -> DIR / SIGN
//   GPIO10 -> SERVO ON
//
// ARQUITECTURA SERVO 1:
//   - Dashboard SOLO configura P1, P2, P3, secuencia y Hz de secuencia.
//   - Firmware interpreta y ejecuta la secuencia.
//   - Velocidad física del motor NO viene del dashboard.
//   - SON ON  -> posición lógica = 0.
//   - SON OFF -> paro inmediato + cancelación + reset del estado.
//
// SERVO 2 = CONTROL MANUAL LEDC (sin cambios funcionales)
//   GPIO8  -> PULSE
//   GPIO9  -> DIR / SIGN
//   GPIO11 -> SERVO ON
//
// Polaridad conservada:
//   SON HIGH = ON
//   SON LOW  = OFF

static constexpr uint8_t SERVO1 = 0;
static constexpr uint8_t SERVO2 = 1;
static constexpr uint8_t SERVO_COUNT = 2;

static const int SERVO_PULSE[SERVO_COUNT] = {
  17,  // SERVO 1
  8    // SERVO 2
};

static const int SERVO_DIR[SERVO_COUNT] = {
  18,  // SERVO 1
  9    // SERVO 2
};

static const int SERVO_SON[SERVO_COUNT] = {
  10,  // SERVO 1
  11   // SERVO 2
};

static const uint8_t SERVO_LEDC_RES = 6;

// ==========================================================
// SERVO 1 - PARÁMETROS FÍSICOS INTERNOS (NO DASHBOARD)
// ==========================================================
// Modificar aquí únicamente si cambia el hardware.
//
// 1 vuelta física = 256 pasos.
// Velocidad física del motor = 240 RPM.
//
// Conversión interna:
//   frecuencia PULSE [Hz] = RPM * pasos_por_vuelta / 60
//   240 RPM * 256 pasos/vuelta / 60 = 1024 Hz
static constexpr uint32_t SERVO1_STEPS_PER_REV = 256;

// Escala usada por el operador/dashboard:
// 1000 unidades = 1 vuelta física.
static constexpr int32_t SERVO1_OPERATOR_UNITS_PER_REV = 1000;

static constexpr uint32_t SERVO1_MOTOR_SPEED_RPM = 480;

static constexpr uint32_t SERVO1_PHYSICAL_FREQ_HZ =
  (SERVO1_MOTOR_SPEED_RPM * SERVO1_STEPS_PER_REV) / 60UL;

// Posición creciente -> HIGH. Posición decreciente -> LOW.
// Si físicamente queda invertido, cambiar SOLO esta constante.
static constexpr uint8_t SERVO1_POSITIVE_DIR_LEVEL = HIGH;

// ==========================================================
// SERVO 1 - ESTADO DE POSICIÓN / MOVIMIENTO
// ==========================================================
static volatile bool     servo1_position_valid = false;
static volatile bool     servo1_motion_active = false;
static volatile bool     servo1_finish_pending = false;
static volatile bool     servo1_pulse_level = false;

// Posición y target INTERNOS en pasos físicos del motor.
static volatile int32_t  servo1_position = 0;
static volatile int32_t  servo1_target = 0;

// Target equivalente en escala de operador (1000 unidades/vuelta).
static volatile int32_t  servo1_target_units = 0;

static volatile int8_t   servo1_move_sign = 0;

static volatile uint32_t servo1_steps_total = 0;
static volatile uint32_t servo1_steps_done = 0;
static volatile uint32_t servo1_steps_remaining = 0;

// Periodo fijo de PULSE derivado de 240 RPM y 256 pasos/vuelta.
// Resultado actual: 1024 Hz -> medio periodo ~488 us.
static constexpr uint32_t SERVO1_HALF_PERIOD_US =
  (1000000UL / (2UL * SERVO1_PHYSICAL_FREQ_HZ));

static esp_timer_handle_t servo1_pulse_timer = nullptr;
static volatile bool servo1_timer_running = false;

// ==========================================================
// SERVO 1 - CONFIGURACIÓN DE SECUENCIA
// ==========================================================
// P1/P2/P3 recibidos desde dashboard son posiciones ABSOLUTAS
// en escala de OPERADOR: 1000 unidades = 1 vuelta.
// Antes de mover, firmware los convierte a pasos físicos:
// pasos = (unidades * 256) / 1000.
// La secuencia admite solamente caracteres '1', '2' y '3'.
// Ejemplos: 123, 132, 1213, 321.
//
// La frecuencia de secuencia define la ESPERA DESPUÉS de que termina
// completamente cada movimiento.
//
// Ejemplo a 1 Hz:
//   mover P1 -> terminar P1 -> esperar 1 s -> mover P2
//   -> terminar P2 -> esperar 1 s -> mover P3
//
// El tiempo NO corre mientras el motor está moviéndose.
//
// La secuencia es CÍCLICA Y CONTINUA:
//   123 -> P1 -> P2 -> P3 -> P1 -> P2 -> P3 -> ...
// y solo termina al recibir STOP SECUENCIA, SON OFF o una cancelación.
static constexpr size_t SERVO1_SEQUENCE_MAX = 64;

// Posiciones usadas internamente por el generador de pulsos: pasos físicos.
static int32_t servo1_positions[3] = {0, 0, 0};

// Valores exactos introducidos por el operador/dashboard: 1000 unidades/vuelta.
static int32_t servo1_positions_units[3] = {0, 0, 0};

static char servo1_sequence[SERVO1_SEQUENCE_MAX + 1] = "123";
static uint8_t servo1_sequence_len = 3;

static double servo1_sequence_hz = 1.0;
static uint64_t servo1_sequence_period_us = 1000000ULL;

static volatile bool servo1_sequence_active = false;
static volatile uint8_t servo1_sequence_index = 0;
static volatile uint64_t servo1_sequence_next_us = 0;

// ==========================================================
// SERVO 2 - estado manual LEDC
// ==========================================================
static volatile uint32_t servo_hz[SERVO_COUNT] = {
  SERVO1_PHYSICAL_FREQ_HZ,
  1000
};

static volatile uint8_t servo_duty[SERVO_COUNT] = {32, 32};
static volatile bool servo_running[SERVO_COUNT] = {false, false};
static volatile bool servo_son_on[SERVO_COUNT] = {false, false};

static bool servo_valid(uint8_t s) {
  return s < SERVO_COUNT;
}

// ==========================================================
// SERVO 1 - CONVERSIÓN ESCALA OPERADOR <-> PASOS FÍSICOS
// ==========================================================
//
// Dashboard / operador:
//   1000 unidades = 1 vuelta
//
// Motor:
//   256 pasos físicos = 1 vuelta
//
// Conversión solicitada:
//   pasos_fisicos = (unidades_operador * 256) / 1000
//
// Se usa int64_t en el cálculo intermedio para evitar overflow.
static bool servo1_units_to_physical_steps(
  int32_t units,
  int32_t &physical_steps
) {
  const int64_t num =
    (int64_t)units * (int64_t)SERVO1_STEPS_PER_REV;

  const int64_t result =
    num / (int64_t)SERVO1_OPERATOR_UNITS_PER_REV;

  if (result > INT32_MAX || result < INT32_MIN) {
    return false;
  }

  physical_steps = (int32_t)result;
  return true;
}

static double servo1_physical_steps_to_units(
  int32_t physical_steps
) {
  return
    ((double)physical_steps *
     (double)SERVO1_OPERATOR_UNITS_PER_REV) /
    (double)SERVO1_STEPS_PER_REV;
}

static double servo1_physical_hz() {
  return (double)SERVO1_PHYSICAL_FREQ_HZ;
}

static void servo1_stop_timer_only() {
  servo1_motion_active = false;

  if (servo1_pulse_timer && servo1_timer_running) {
    esp_timer_stop(servo1_pulse_timer);
  }

  servo1_timer_running = false;
  servo1_pulse_level = false;
  gpio_set_level((gpio_num_t)SERVO_PULSE[SERVO1], 0);
}

static void servo1_cancel_sequence() {
  servo1_sequence_active = false;
  servo1_sequence_index = 0;
  servo1_sequence_next_us = 0;
}

static void servo1_reset_runtime_state() {
  servo1_stop_timer_only();
  servo1_cancel_sequence();

  servo1_position_valid = false;
  servo1_position = 0;
  servo1_target = 0;
  servo1_target_units = 0;
  servo1_move_sign = 0;

  servo1_steps_total = 0;
  servo1_steps_done = 0;
  servo1_steps_remaining = 0;
  servo1_finish_pending = false;

  digitalWrite(SERVO_DIR[SERVO1], LOW);
}

static void servo1_reset_sequence_config() {
  servo1_positions[0] = 0;
  servo1_positions[1] = 0;
  servo1_positions[2] = 0;

  servo1_positions_units[0] = 0;
  servo1_positions_units[1] = 0;
  servo1_positions_units[2] = 0;

  strlcpy(servo1_sequence, "123", sizeof(servo1_sequence));
  servo1_sequence_len = 3;
  servo1_sequence_hz = 1.0;
  servo1_sequence_period_us = 1000000ULL;
}

static void servo1_son_on_cmd() {
  // Solo una transición real OFF -> ON establece un nuevo cero.
  if (servo_son_on[SERVO1]) return;

  servo1_reset_runtime_state();

  digitalWrite(SERVO_SON[SERVO1], HIGH);
  servo_son_on[SERVO1] = true;

  servo1_position = 0;
  servo1_target = 0;
  servo1_target_units = 0;
  servo1_position_valid = true;

  Serial.println("SERVO1 SON ON -> POSICION CERO = 0");
}

static void servo1_son_off_cmd() {
  // Paro inmediato + cancelación + reset completo del estado Servo 1.
  servo1_stop_timer_only();

  digitalWrite(SERVO_SON[SERVO1], LOW);
  servo_son_on[SERVO1] = false;

  servo1_reset_runtime_state();
  servo1_reset_sequence_config();

  Serial.println("SERVO1 SON OFF -> STOP INMEDIATO + RESET TOTAL");
}

static void servo1_cancel_keep_position() {
  // STOP auxiliar: corta movimiento y secuencia, conserva posición alcanzada.
  servo1_stop_timer_only();
  servo1_cancel_sequence();

  servo1_target = servo1_position;
  servo1_target_units =
    (int32_t)llround(servo1_physical_steps_to_units(servo1_position));

  servo1_move_sign = 0;
  servo1_steps_total = 0;
  servo1_steps_done = 0;
  servo1_steps_remaining = 0;
  servo1_finish_pending = false;

  Serial.printf("SERVO1 STOP -> posicion conservada=%ld\n", (long)servo1_position);
}

static void servo1_stop_sequence_cmd() {
  // STOP SECUENCIA:
  // - detiene inmediatamente la generación de pulsos si está moviéndose;
  // - cancela el ciclo;
  // - conserva la posición lógica alcanzada;
  // - conserva P1/P2/P3, secuencia y Hz para poder reiniciar.
  servo1_cancel_keep_position();

  Serial.printf(
    "SERVO1 STOP SECUENCIA -> ciclo detenido; posicion=%ld\n",
    (long)servo1_position
  );
}

static void servo1_pulse_timer_cb(void* arg) {
  (void)arg;

  if (!servo1_motion_active || !servo_son_on[SERVO1]) {
    servo1_pulse_level = false;
    gpio_set_level((gpio_num_t)SERVO_PULSE[SERVO1], 0);
    return;
  }

  if (!servo1_pulse_level) {
    servo1_pulse_level = true;
    gpio_set_level((gpio_num_t)SERVO_PULSE[SERVO1], 1);
    return;
  }

  // Flanco de bajada = un pulso completo.
  servo1_pulse_level = false;
  gpio_set_level((gpio_num_t)SERVO_PULSE[SERVO1], 0);

  if (servo1_steps_remaining == 0) {
    servo1_motion_active = false;
    servo1_finish_pending = true;
    return;
  }

  servo1_position += servo1_move_sign;
  servo1_steps_done++;

  if (servo1_steps_remaining > 0) servo1_steps_remaining--;

  if (servo1_steps_remaining == 0) {
    servo1_position = servo1_target;
    servo1_motion_active = false;
    servo1_finish_pending = true;
  }
}

static void servo1_position_tick() {
  if (!servo1_finish_pending) return;

  servo1_finish_pending = false;

  if (servo1_pulse_timer && servo1_timer_running) {
    esp_timer_stop(servo1_pulse_timer);
  }

  servo1_timer_running = false;
  servo1_pulse_level = false;
  gpio_set_level((gpio_num_t)SERVO_PULSE[SERVO1], 0);
  servo1_move_sign = 0;

  Serial.printf(
    "SERVO1 MOVE DONE -> posicion=%ld pasos=%lu\n",
    (long)servo1_position,
    (unsigned long)servo1_steps_done
  );

  // IMPORTANTE:
  // El periodo de secuencia empieza a contarse RECIÉN cuando
  // el movimiento físico terminó por completo.
  //
  // Como la secuencia es cíclica, esto aplica también al último
  // elemento: al terminar P3, por ejemplo, espera el periodo y
  // luego vuelve a P1.
  if (servo1_sequence_active) {

    const uint64_t now =
      (uint64_t)esp_timer_get_time();

    servo1_sequence_next_us =
      now + servo1_sequence_period_us;

    Serial.printf(
      "SERVO1 SEQ WAIT -> %.4f Hz, espera=%.3f ms antes del siguiente elemento\n",
      servo1_sequence_hz,
      (double)servo1_sequence_period_us / 1000.0
    );
  }
}

static bool servo1_move_absolute(int32_t target, String &msg) {
  if (!servo_son_on[SERVO1] || !servo1_position_valid) {
    msg = "ERROR: SERVO1 SON OFF / posicion no inicializada";
    return false;
  }

  if (servo1_motion_active || servo1_timer_running) {
    msg = "BUSY: SERVO1 ya esta moviendo";
    return false;
  }

  const int64_t delta64 = (int64_t)target - (int64_t)servo1_position;

  if (delta64 > 2147483647LL || delta64 < -2147483647LL) {
    msg = "ERROR: desplazamiento fuera de rango";
    return false;
  }

  const int32_t delta = (int32_t)delta64;
  servo1_target = target;

  if (delta == 0) {
    servo1_move_sign = 0;
    servo1_steps_total = 0;
    servo1_steps_done = 0;
    servo1_steps_remaining = 0;
    msg = "OK: ya esta en la posicion objetivo";
    return true;
  }

  const uint32_t steps =
    (delta > 0) ? (uint32_t)delta : (uint32_t)(-(int64_t)delta);

  servo1_move_sign = (delta > 0) ? 1 : -1;
  servo1_steps_total = steps;
  servo1_steps_done = 0;
  servo1_steps_remaining = steps;

  const uint8_t positiveLevel = SERVO1_POSITIVE_DIR_LEVEL;
  const uint8_t negativeLevel = (positiveLevel == HIGH) ? LOW : HIGH;
  const uint8_t dirLevel = (servo1_move_sign > 0) ? positiveLevel : negativeLevel;

  digitalWrite(SERVO_DIR[SERVO1], dirLevel);
  delay(2); // estabilización de DIR

  if (!servo1_pulse_timer) {
    msg = "ERROR: timer SERVO1 no inicializado";
    return false;
  }

  servo1_pulse_level = false;
  gpio_set_level((gpio_num_t)SERVO_PULSE[SERVO1], 0);
  servo1_finish_pending = false;
  servo1_motion_active = true;

  esp_err_t err = esp_timer_start_periodic(
    servo1_pulse_timer,
    SERVO1_HALF_PERIOD_US
  );

  if (err != ESP_OK) {
    servo1_motion_active = false;
    servo1_steps_total = 0;
    servo1_steps_done = 0;
    servo1_steps_remaining = 0;
    msg = "ERROR: esp_timer_start_periodic=" + String((int)err);
    return false;
  }

  servo1_timer_running = true;

  Serial.printf(
    "SERVO1 MOVE: actual=%ld objetivo=%ld delta=%ld velocidad=%luRPM pulse=%luHz\n",
    (long)servo1_position,
    (long)servo1_target,
    (long)delta,
    (unsigned long)SERVO1_MOTOR_SPEED_RPM,
    (unsigned long)SERVO1_PHYSICAL_FREQ_HZ
  );

  msg = "OK: moviendo a " + String((long)target) + " pasos";
  return true;
}

static bool servo1_validate_sequence(const String &seq, String &msg) {
  if (seq.length() == 0) {
    msg = "ERROR: secuencia vacia";
    return false;
  }

  if (seq.length() > SERVO1_SEQUENCE_MAX) {
    msg = "ERROR: secuencia demasiado larga (max " +
          String((unsigned)SERVO1_SEQUENCE_MAX) + ")";
    return false;
  }

  for (size_t i = 0; i < seq.length(); i++) {
    const char c = seq.charAt(i);
    if (c < '1' || c > '3') {
      msg = "ERROR: secuencia solo admite 1, 2 y 3";
      return false;
    }
  }

  return true;
}

static bool servo1_configure_and_start_sequence(
  int32_t p1_units,
  int32_t p2_units,
  int32_t p3_units,
  const String &seq,
  double seq_hz,
  String &msg
) {
  if (!servo_son_on[SERVO1] || !servo1_position_valid) {
    msg = "ERROR: SERVO1 SON OFF / posicion no inicializada";
    return false;
  }

  if (servo1_motion_active || servo1_timer_running || servo1_sequence_active) {
    msg = "BUSY: SERVO1 ya esta ejecutando una secuencia";
    return false;
  }

  if (!isfinite(seq_hz) || seq_hz <= 0.0) {
    msg = "ERROR: frecuencia de secuencia debe ser > 0 Hz";
    return false;
  }

  String seq_clean = seq;
  seq_clean.trim();

  if (!servo1_validate_sequence(seq_clean, msg)) return false;

  // Convertir las posiciones ingresadas por el operador
  // (1000 unidades/vuelta) a pasos físicos (256 pasos/vuelta).
  int32_t p1_steps = 0;
  int32_t p2_steps = 0;
  int32_t p3_steps = 0;

  if (!servo1_units_to_physical_steps(p1_units, p1_steps) ||
      !servo1_units_to_physical_steps(p2_units, p2_steps) ||
      !servo1_units_to_physical_steps(p3_units, p3_steps)) {

    msg = "ERROR: P1/P2/P3 fuera de rango al convertir a pasos fisicos";
    return false;
  }

  // Guardar los valores exactos del operador para el dashboard.
  servo1_positions_units[0] = p1_units;
  servo1_positions_units[1] = p2_units;
  servo1_positions_units[2] = p3_units;

  // Guardar las posiciones convertidas usadas internamente por el motor.
  servo1_positions[0] = p1_steps;
  servo1_positions[1] = p2_steps;
  servo1_positions[2] = p3_steps;

  strlcpy(servo1_sequence, seq_clean.c_str(), sizeof(servo1_sequence));
  servo1_sequence_len = (uint8_t)seq_clean.length();
  servo1_sequence_hz = seq_hz;

  double period = 1000000.0 / servo1_sequence_hz;
  if (period < 1.0) period = 1.0;
  servo1_sequence_period_us = (uint64_t)(period + 0.5);

  servo1_sequence_index = 0;
  servo1_sequence_active = true;

  // El primer elemento se ejecuta inmediatamente.
  // A partir del segundo, el tiempo se contará DESPUÉS
  // de terminar el movimiento anterior.
  servo1_sequence_next_us =
    (uint64_t)esp_timer_get_time();

  Serial.printf(
    "SERVO1 SEQ START: "
    "P1=%ldu->%ldstep "
    "P2=%ldu->%ldstep "
    "P3=%ldu->%ldstep "
    "seq=%s seq_hz=%.4f velocidad=%luRPM pulse=%luHz\n",
    (long)servo1_positions_units[0],
    (long)servo1_positions[0],
    (long)servo1_positions_units[1],
    (long)servo1_positions[1],
    (long)servo1_positions_units[2],
    (long)servo1_positions[2],
    servo1_sequence,
    servo1_sequence_hz,
    (unsigned long)SERVO1_MOTOR_SPEED_RPM,
    (unsigned long)SERVO1_PHYSICAL_FREQ_HZ
  );

  msg =
    "OK: escala operador 1000u/vuelta; "
    "secuencia ciclica iniciada " +
    String(servo1_sequence) +
    " (STOP SECUENCIA para detener)";

  return true;
}

static void servo1_sequence_tick() {
  if (!servo1_sequence_active) return;

  if (!servo_son_on[SERVO1] || !servo1_position_valid) {
    servo1_cancel_sequence();
    return;
  }

  // Nunca se solapan movimientos.
  // Mientras el motor esté generando pulsos o cerrando el movimiento,
  // no se evalúa el siguiente elemento de la secuencia.
  if (servo1_motion_active ||
      servo1_timer_running ||
      servo1_finish_pending) {

    return;
  }

  const uint64_t now =
    (uint64_t)esp_timer_get_time();

  // El reloj de secuencia empieza a contar únicamente al terminar
  // el movimiento anterior. Para el primer elemento se habilita
  // inmediatamente al iniciar la secuencia.
  if (now < servo1_sequence_next_us) {
    return;
  }

  // CICLO CONTINUO:
  // al terminar el último elemento, volver al primero.
  if (servo1_sequence_index >= servo1_sequence_len) {
    servo1_sequence_index = 0;

    Serial.printf(
      "SERVO1 SEQ LOOP -> reiniciando ciclo %s\n",
      servo1_sequence
    );
  }

  const char c =
    servo1_sequence[servo1_sequence_index];

  const uint8_t pos_idx =
    (uint8_t)(c - '1');

  if (pos_idx > 2) {
    servo1_cancel_sequence();
    Serial.println(
      "SERVO1 SEQ ERROR: indice invalido"
    );
    return;
  }

  const int32_t target =
    servo1_positions[pos_idx];

  // Valor equivalente en escala de operador para status/dashboard.
  servo1_target_units =
    servo1_positions_units[pos_idx];

  // Guardamos la posición anterior para distinguir el caso
  // target == posición actual (cero pulsos).
  const int32_t position_before =
    servo1_position;

  String msg;

  const bool ok =
    servo1_move_absolute(
      target,
      msg
    );

  if (!ok) {
    servo1_cancel_sequence();

    Serial.print(
      "SERVO1 SEQ ERROR: "
    );

    Serial.println(msg);
    return;
  }

  Serial.printf(
    "SERVO1 SEQ ITEM %u/%u -> P%c = %ld unidades -> %ld pasos fisicos\n",
    (unsigned)(servo1_sequence_index + 1),
    (unsigned)servo1_sequence_len,
    c,
    (long)servo1_target_units,
    (long)target
  );

  servo1_sequence_index++;

  // CASO NORMAL:
  // Si realmente empezó un movimiento, NO arrancamos aquí el reloj.
  // servo1_position_tick() lo hará cuando termine el último pulso.
  if (servo1_motion_active || servo1_timer_running) {
    return;
  }

  // CASO target == posición actual:
  // No hubo pulsos que esperar, así que este elemento terminó
  // instantáneamente. Como el ciclo es continuo, siempre se programa
  // la espera antes del siguiente elemento (incluso al cerrar el ciclo).
  if (target == position_before && servo1_sequence_active) {

    servo1_sequence_next_us =
      now + servo1_sequence_period_us;

    Serial.printf(
      "SERVO1 SEQ ITEM SIN MOVIMIENTO -> espera=%.3f ms antes del siguiente\n",
      (double)servo1_sequence_period_us / 1000.0
    );
  }
}

// ==========================================================
// Funciones comunes / SERVO 2
// ==========================================================
static void servo_pwm_init(uint8_t s, const char* name) {
  if (!servo_valid(s)) return;

  pinMode(SERVO_DIR[s], OUTPUT);
  digitalWrite(SERVO_DIR[s], LOW);

  pinMode(SERVO_SON[s], OUTPUT);
  digitalWrite(SERVO_SON[s], LOW);

  if (s == SERVO1) {
    gpio_reset_pin((gpio_num_t)SERVO_PULSE[SERVO1]);
    gpio_set_direction((gpio_num_t)SERVO_PULSE[SERVO1], GPIO_MODE_OUTPUT);
    gpio_set_level((gpio_num_t)SERVO_PULSE[SERVO1], 0);
    return;
  }

  bool ok = ledcAttach(SERVO_PULSE[s], (double)servo_hz[s], SERVO_LEDC_RES);

  if (!ok) {
    Serial.printf("LEDC attach FAIL en %s PULSE GPIO%d\n", name, SERVO_PULSE[s]);
  }

  ledcWrite(SERVO_PULSE[s], 0);
}

static void servo_son(uint8_t s, bool on) {
  if (!servo_valid(s)) return;

  if (s == SERVO1) {
    if (on) servo1_son_on_cmd();
    else    servo1_son_off_cmd();
    return;
  }

  servo_son_on[s] = on;
  digitalWrite(SERVO_SON[s], on ? HIGH : LOW);
}

static void servo_run(uint8_t s, bool on) {
  if (!servo_valid(s)) return;

  if (s == SERVO1) {
    if (!on) servo1_cancel_keep_position();
    return;
  }

  servo_running[s] = on;
  ledcWrite(SERVO_PULSE[s], on ? servo_duty[s] : 0);
}

static void servo_set_dir(uint8_t s, int dir) {
  if (!servo_valid(s)) return;

  if (s == SERVO1) return; // dirección automática

  const bool wasRunning = servo_running[s];
  if (wasRunning) ledcWrite(SERVO_PULSE[s], 0);

  delay(2);
  digitalWrite(SERVO_DIR[s], dir ? HIGH : LOW);
  delay(2);

  if (wasRunning) ledcWrite(SERVO_PULSE[s], servo_duty[s]);
}

static void servo_set_hz(uint8_t s, uint32_t hz, const char* name) {
  if (!servo_valid(s)) return;

  if (s == SERVO1) return; // frecuencia física fija en firmware

  if (hz < 1) hz = 1;
  if (hz > 160000) hz = 160000;

  servo_hz[s] = hz;
  ledcChangeFrequency(SERVO_PULSE[s], (double)servo_hz[s], SERVO_LEDC_RES);

  if (servo_running[s]) ledcWrite(SERVO_PULSE[s], servo_duty[s]);

  Serial.printf(
    "%s hz req=%lu real=%.1f\n",
    name,
    (unsigned long)servo_hz[s],
    ledcReadFreq(SERVO_PULSE[s])
  );
}

static void servo_set_duty_pct(uint8_t s, uint8_t pct) {
  if (!servo_valid(s)) return;
  if (s == SERVO1) return;

  if (pct > 100) pct = 100;

  const uint32_t maxv = (1u << SERVO_LEDC_RES) - 1u;
  servo_duty[s] = (uint8_t)(((uint32_t)pct * maxv) / 100u);

  if (servo_running[s]) ledcWrite(SERVO_PULSE[s], servo_duty[s]);
}

// ====================== STEPPER PULSE / DIR ======================
//
// Integración siguiendo la misma lógica general del Servo 2:
//   - inicialización
//   - frecuencia configurable
//   - DIR 0 / DIR 1
//   - RUN / STOP
//   - REV seguro
//   - estado disponible por /status
//
// Hardware actual:
//   STEP / PULSE -> GPIO12
//   DIR          -> GPIO13
//
// No existe señal ENABLE en esta tarjeta.
//
// El PULSE se genera con esp_timer. Se conserva este generador porque
// ya formaba parte del firmware y mantiene el stepper aislado del LEDC
// utilizado por Servo 2.
// ================================================================

// ------------------- PARÁMETROS FÁCILES DE MODIFICAR -------------------
static constexpr int STEP_PIN_STEP = 12;
static constexpr int STEP_PIN_DIR  = 13;

static constexpr uint32_t STEP_MIN_HZ     = 1;
static constexpr uint32_t STEP_MAX_HZ     = 200000;
static constexpr uint32_t STEP_DEFAULT_HZ = 200;

static constexpr uint32_t STEP_DIR_SETTLE_MS = 3;

// ------------------- ESTADO -------------------
static volatile uint32_t step_hz = STEP_DEFAULT_HZ;
static volatile bool step_running = false;
static volatile bool step_level = false;

static esp_timer_handle_t step_timer = nullptr;
static portMUX_TYPE step_mux = portMUX_INITIALIZER_UNLOCKED;

static inline uint32_t clamp_u32(uint32_t v, uint32_t lo, uint32_t hi){
  if (v < lo) return lo;
  if (v > hi) return hi;
  return v;
}

static uint64_t step_half_period_us_from_hz(uint32_t hz) {
  hz = clamp_u32(hz, STEP_MIN_HZ, STEP_MAX_HZ);

  uint64_t half_us =
    1000000ULL / (2ULL * (uint64_t)hz);

  if (half_us < 2) half_us = 2;

  return half_us;
}

static double step_real_hz() {
  const uint64_t half_us =
    step_half_period_us_from_hz(step_hz);

  if (half_us == 0) return 0.0;

  return
    1000000.0 /
    (2.0 * (double)half_us);
}

// ------------------- GENERADOR PULSE -------------------
static void step_timer_cb(void* arg) {
  (void)arg;

  if (!step_running) return;

  step_level = !step_level;

  gpio_set_level(
    (gpio_num_t)STEP_PIN_STEP,
    step_level ? 1 : 0
  );
}

// ------------------- INICIALIZACIÓN -------------------
static void stepper_init() {
  pinMode(STEP_PIN_DIR, OUTPUT);
  digitalWrite(STEP_PIN_DIR, LOW);

  gpio_reset_pin((gpio_num_t)STEP_PIN_STEP);
  gpio_set_direction(
    (gpio_num_t)STEP_PIN_STEP,
    GPIO_MODE_OUTPUT
  );
  gpio_set_level(
    (gpio_num_t)STEP_PIN_STEP,
    0
  );

  esp_timer_create_args_t args = {};
  args.callback = &step_timer_cb;
  args.arg = nullptr;
  args.name = "stepper_pulse";

  const esp_err_t err =
    esp_timer_create(
      &args,
      &step_timer
    );

  if (err != ESP_OK) {
    step_timer = nullptr;

    Serial.printf(
      "STEPPER ERROR: esp_timer_create=%d\n",
      (int)err
    );

    return;
  }

  step_hz = STEP_DEFAULT_HZ;
  step_running = false;
  step_level = false;

  Serial.printf(
    "STEPPER INIT OK: PULSE GPIO%d DIR GPIO%d hz=%lu\n",
    STEP_PIN_STEP,
    STEP_PIN_DIR,
    (unsigned long)step_hz
  );
}

// ------------------- FRECUENCIA -------------------
static void step_apply_from_freq(uint32_t hz) {
  hz =
    clamp_u32(
      hz,
      STEP_MIN_HZ,
      STEP_MAX_HZ
    );

  portENTER_CRITICAL(&step_mux);
  step_hz = hz;
  portEXIT_CRITICAL(&step_mux);

  const uint64_t half_us =
    step_half_period_us_from_hz(step_hz);

  if (step_timer && step_running) {
    esp_timer_stop(step_timer);

    const esp_err_t err =
      esp_timer_start_periodic(
        step_timer,
        half_us
      );

    if (err != ESP_OK) {
      step_running = false;
      step_level = false;

      gpio_set_level(
        (gpio_num_t)STEP_PIN_STEP,
        0
      );

      Serial.printf(
        "STEPPER ERROR: cambio frecuencia err=%d\n",
        (int)err
      );

      return;
    }
  }

  Serial.printf(
    "STEPPER HZ: req=%lu real=%.2f\n",
    (unsigned long)step_hz,
    step_real_hz()
  );
}

// ------------------- RUN / STOP -------------------
static void step_start() {
  if (!step_timer) {
    Serial.println(
      "STEPPER RUN ERROR: timer no inicializado"
    );
    return;
  }

  if (step_running) return;

  step_level = false;

  gpio_set_level(
    (gpio_num_t)STEP_PIN_STEP,
    0
  );

  const uint64_t half_us =
    step_half_period_us_from_hz(step_hz);

  const esp_err_t err =
    esp_timer_start_periodic(
      step_timer,
      half_us
    );

  if (err != ESP_OK) {
    step_running = false;

    Serial.printf(
      "STEPPER RUN ERROR: esp_timer_start_periodic=%d\n",
      (int)err
    );

    return;
  }

  step_running = true;

  Serial.printf(
    "STEPPER RUN ON: hz=%lu real=%.2f dir=%d\n",
    (unsigned long)step_hz,
    step_real_hz(),
    digitalRead(STEP_PIN_DIR) ? 1 : 0
  );
}

static void step_stop() {
  step_running = false;

  if (step_timer) {
    esp_timer_stop(step_timer);
  }

  step_level = false;

  gpio_set_level(
    (gpio_num_t)STEP_PIN_STEP,
    0
  );

  Serial.println("STEPPER RUN OFF");
}

// ------------------- DIRECCIÓN -------------------
static void step_set_dir_safe(int dir) {
  const bool was_running =
    step_running;

  if (was_running) {
    step_stop();
  }

  delay(STEP_DIR_SETTLE_MS);

  digitalWrite(
    STEP_PIN_DIR,
    dir ? HIGH : LOW
  );

  delay(STEP_DIR_SETTLE_MS);

  if (was_running) {
    step_start();
  }

  Serial.printf(
    "STEPPER DIR=%d\n",
    dir ? 1 : 0
  );
}

// REV = invertir DIR conservando RUN si estaba activo.
static void step_rev_start() {
  const int dir_now =
    digitalRead(STEP_PIN_DIR) ? 1 : 0;

  step_set_dir_safe(
    dir_now ? 0 : 1
  );
}

// ============================
// ===== processDo (CAN) =======
// ============================
static void processDo(const char* d_in) {
  if (!d_in) return;
  String d = d_in;
  d.trim();
  d.toLowerCase();

  if      (d == "start")      can_start();
  else if (d == "stop")       can_stop();
  else if (d == "init")       run_init_sequence();
  else if (d == "testeo")     testeo_start();

  else if (d == "j_run_all") {
    for (size_t i = 0; i < ACTIVE_J_N; i++) {
      const uint8_t j = ACTIVE_J[i];

      cascade_stop(runJ[j - 1]);

      // Cada RUN comienza desde todo apagado.
      j_regs[j - 1] = 0xFF;
      can_send_j(j);

      cascade_start(runJ[j - 1], 80);
    }
    Serial.println("OK j_run_all -> físicos J1,J2,J5,J6");
  }
  else if (d == "j_stop_all") {
    for (size_t i = 0; i < ACTIVE_J_N; i++) {
      const uint8_t j = ACTIVE_J[i];

      // STOP corta la rutina y conserva la última máscara enviada.
      cascade_stop(runJ[j - 1]);
    }
    Serial.println("OK j_stop_all -> secuencias detenidas; estado conservado");
  }
  else if (d.startsWith("j_run_")) {
    int n = d.substring(6).toInt();

    if (n >= 1 && n <= 8 && is_active_j((uint8_t)n)) {
      cascade_stop(runJ[n - 1]);

      // Comenzar siempre desde todo apagado.
      j_regs[n - 1] = 0xFF;
      can_send_j((uint8_t)n);

      cascade_start(runJ[n - 1], 80);
      Serial.printf("OK j_run_%d\n", n);
    } else {
      Serial.println("BAD j_run_n (activos: 1,2,5,6)");
    }
  }
  else if (d.startsWith("j_stop_")) {
    int n = d.substring(7).toInt();

    if (n >= 1 && n <= 8 && is_active_j((uint8_t)n)) {
      // STOP individual corta la rutina y conserva
      // la última máscara que ya fue enviada al hardware.
      cascade_stop(runJ[n - 1]);

      Serial.printf("OK j_stop_%d -> estado conservado\n", n);
    } else {
      Serial.println("BAD j_stop_n (activos: 1,2,5,6)");
    }
  }

  else if (d == "y_run_all") {
    // En este cabezal ALL equivale únicamente a Yarn 1.
    runY1.running = false;
    cascade_start(runY1, 80);
    Serial.println("OK y_run_all -> solo Yarn 1");
  }
  else if (d == "y_stop_all") {
    cascade_stop(runY1);
    Serial.println("OK y_stop_all -> solo Yarn 1");
  }
  else if (d == "y1_run") {
    runY1.running = false;
    cascade_start(runY1, 80);
    Serial.println("OK y1_run");
  }
  else if (d == "y1_stop") {
    cascade_stop(runY1);
    Serial.println("OK y1_stop");
  }
  else if (d == "y2_run" || d == "y2_stop") {
    Serial.println("DISABLED: Yarn 2 no existe en este cabezal");
  }

  else if (d == "den_run_all") {
    for (size_t i = 0; i < ACTIVE_DEN_N; i++) {
      den_run_start(ACTIVE_DEN[i]);
    }
    Serial.println("OK den_run_all -> físicos DEN1,DEN2,DEN5,DEN6");
  }
  else if (d == "den_stop_all") {
    for (int i = 0; i < 8; i++) {
      cascade_stop(runDen[i]);
    }
    Serial.println("OK den_stop_all");
  }
  else if (d.startsWith("den_run_")) {
    int n = d.substring(8).toInt();

    if (n >= 1 && n <= 8 && is_active_den((uint8_t)n)) {
      den_run_start((uint8_t)n);
      Serial.printf("OK den_run_%d -> POS1..POS5\n", n);
    } else {
      Serial.println("BAD den_run_n (activos: 1,2,5,6)");
    }
  }
  else if (d.startsWith("den_stop_")) {
    int n = d.substring(9).toInt();

    if (n >= 1 && n <= 8 && is_active_den((uint8_t)n)) {
      // STOP solo corta el RUN.
      // No manda otra posición: conserva la última orden enviada.
      cascade_stop(runDen[n - 1]);
      Serial.printf("OK den_stop_%d\n", n);
    } else {
      Serial.println("BAD den_stop_n (activos: 1,2,5,6)");
    }
  }
  else if (d.startsWith("den_pos_")) {
    int den = 0;
    int pos = 0;

    if (sscanf(d.c_str(), "den_pos_%d_%d", &den, &pos) == 2 &&
        den >= 1 && den <= 8 &&
        is_active_den((uint8_t)den) &&
        pos >= 1 && pos <= 5) {

      cascade_stop(runDen[den - 1]);
      den_send_position((uint8_t)den, (uint8_t)pos);

      Serial.printf("OK den_pos_%d_%d\n", den, pos);
    } else {
      Serial.println("BAD den_pos_den_pos (DEN activos: 1,2,5,6; POS: 1..5)");
    }
  }

  else if (d == "sic_run_all") {
    sic_run_start(1);
    sic_run_start(2);
    Serial.println("OK sic_run_all");
  }
  else if (d == "sic_stop_all") {
    cascade_stop(runSic[0]);
    cascade_stop(runSic[1]);
    Serial.println("OK sic_stop_all");
  }
  else if (d.startsWith("sic_run_")) {
    int n = d.substring(8).toInt();

    if (n >= 1 && n <= 2) {
      sic_run_start((uint8_t)n);
      Serial.printf("OK sic_run_%d\n", n);
    } else {
      Serial.println("BAD sic_run_n");
    }
  }
  else if (d.startsWith("sic_stop_")) {
    int n = d.substring(9).toInt();

    if (n >= 1 && n <= 2) {
      // STOP solo corta el RUN.
      // No manda otra posición ni sincronismo.
      cascade_stop(runSic[n - 1]);
      Serial.printf("OK sic_stop_%d\n", n);
    } else {
      Serial.println("BAD sic_stop_n");
    }
  }
  else if (d.startsWith("sic_sync_")) {
    int n = d.substring(9).toInt();

    if (n >= 1 && n <= 2) {
      cascade_stop(runSic[n - 1]);
      sic_send_sync((uint8_t)n);
      Serial.printf("OK sic_sync_%d\n", n);
    } else {
      Serial.println("BAD sic_sync_n");
    }
  }
  else if (d.startsWith("sic_pos_")) {
    int sic = 0;
    int pos = 0;

    if (sscanf(d.c_str(), "sic_pos_%d_%d", &sic, &pos) == 2 &&
        sic >= 1 && sic <= 2 &&
        pos >= 1 && pos <= 4) {

      cascade_stop(runSic[sic - 1]);
      sic_send_position((uint8_t)sic, (uint8_t)pos);

      Serial.printf("OK sic_pos_%d_%d\n", sic, pos);
    } else {
      Serial.println("BAD sic_pos_sic_pos");
    }
  }

  else if (d == "s_run_all") {
    for (uint8_t stitch = 1; stitch <= ACTIVE_STITCH_N; stitch++) {
      stitch_run_start(stitch);
    }
    Serial.println("OK s_run_all -> solo Stitch 1 y Stitch 2");
  }
  else if (d == "s_stop_all") {
    for (uint8_t i = 0; i < ACTIVE_STITCH_N; i++) {
      cascade_stop(runS[i]);
    }
    Serial.println("OK s_stop_all -> solo Stitch 1 y Stitch 2");
  }
  else if (d.startsWith("s_run_")) {
    int n = d.substring(6).toInt();

    if (is_active_stitch((uint8_t)n)) {
      stitch_run_start((uint8_t)n);
      Serial.printf("OK s_run_%d -> POS1..POS5 + RESET\n", n);
    } else {
      Serial.println("BAD s_run_n (activos: 1,2)");
    }
  }
  else if (d.startsWith("s_stop_")) {
    int n = d.substring(7).toInt();

    if (is_active_stitch((uint8_t)n)) {
      // STOP solo corta la rutina.
      // No manda RESET ni otra posición: conserva la última orden enviada.
      cascade_stop(runS[n - 1]);
      Serial.printf("OK s_stop_%d\n", n);
    } else {
      Serial.println("BAD s_stop_n (activos: 1,2)");
    }
  }

  else if (d == "secuencia" || d == "secuencia_once") sequenceStartOnce();
  else if (d == "secuencia_stop") scriptStop();

  else if (d == "rebuild")    { build_my_animation_script(); Serial.println("ANIM rebuilt."); }
  else if (d == "anim_on")    scriptStart(true);
  else if (d == "anim_once")  scriptStart(false);
  else if (d == "anim_off")   scriptStop();

  else {
    Serial.print("DO desconocido: ");
    Serial.println(d);
  }
}

// ============================
// ===== CAN TASK (CORE1) =====
// ============================
static void can_task(void* pv) {
  (void)pv;

  for (;;) {
    // 1) procesar cola (web)
    CmdMsg msg;
    while (cmdQ && xQueueReceive(cmdQ, &msg, 0) == pdTRUE) {
      if (msg.type == CMD_DO) {
        processDo(msg.payload);
      } else if (msg.type == CMD_SEND_LINE) {
        String p = msg.payload;
        p.trim(); p.toLowerCase();

        if (p == "testeo") {
          testeo_start();
        } else {
          bool ok = send_line_as_frame(msg.payload);
          if (!ok) {
            Serial.print("WEB SEND PARSE FAIL: ");
            Serial.println(msg.payload);
          }
        }
      }
    }

    // 2) RX
    if (can_started) {
      twai_message_t rx;
      if (twai_receive(&rx, pdMS_TO_TICKS(1)) == ESP_OK) {
        portENTER_CRITICAL(&g_lastRxMux);
        g_lastRx.count++;
        g_lastRx.t_ms = millis();
        g_lastRx.id   = rx.identifier;
        g_lastRx.dlc  = rx.data_length_code;
        g_lastRx.rtr  = rx.rtr ? 1 : 0;
        g_lastRx.extd = rx.extd ? 1 : 0;
        for (int i=0;i<8;i++) g_lastRx.data[i] = (i < rx.data_length_code) ? rx.data[i] : 0;
        portEXIT_CRITICAL(&g_lastRxMux);

        print_rx(rx);
        testeo_on_any_rx_fields(rx.identifier, rx.data_length_code, rx.data, rx.extd, rx.rtr);
      }
    }

    // 3) script tick
    scriptTick();

    // 3.2) testeo tick
    testeo_tick();

    // 3.5) RUN ticks
    for (uint8_t j = 1; j <= 8; j++) tick_run_j(j);
    tick_run_yarn(runY1, 1);

    for (uint8_t den = 1; den <= 8; den++) {
      tick_run_den(den);
    }

    tick_run_sic(1);
    tick_run_sic(2);

    for (uint8_t stitch = 1; stitch <= ACTIVE_STITCH_N; stitch++) {
      tick_run_stitch(stitch);
    }

    // 4) Serial básico (solo CAN)
    String line = readLineNonBlocking();
    if (line.length()) {
      String raw = line;
      raw.trim();

      String lo = raw;
      lo.toLowerCase();

      if (lo == "start") can_start();
      else if (lo == "stop") can_stop();
      else if (lo == "init") run_init_sequence();
      else if (lo == "testeo") testeo_start();
      else if (lo == "secuencia") sequenceStartOnce();
      else if (lo == "secuencia stop") scriptStop();

      else if (lo == "anim rebuild") { build_my_animation_script(); Serial.println("ANIM script rebuilt."); }
      else if (lo == "anim on") scriptStart(true);
      else if (lo == "anim once") scriptStart(false);
      else if (lo == "anim off") scriptStop();

      else if (lo.startsWith("send ")) {
        String payload = raw.substring(5);
        payload.trim();

        String pl = payload; pl.toLowerCase();
        if (pl == "testeo") {
          testeo_start();
        } else {
          bool ok = send_line_as_frame(payload.c_str());
          if (!ok) {
            Serial.print("SEND PARSE FAIL: ");
            Serial.println(payload);
          }
        }
      }
      else {
        bool ok = send_line_as_frame(raw.c_str());
        if (!ok) Serial.println("Comando desconocido.");
      }
    }

    vTaskDelay(pdMS_TO_TICKS(1));
  }
}


// ============================
// ===== Servo1 WEB helpers ====
// ============================
static bool parse_i32_dec(const String &src, int32_t &out) {
  String t = src;
  t.trim();
  if (t.length() == 0) return false;

  char *endp = nullptr;
  long v = strtol(t.c_str(), &endp, 10);

  if (endp == t.c_str() || *endp != '\0') return false;

  out = (int32_t)v;
  return true;
}

static bool parse_u32_dec(const String &src, uint32_t &out) {
  String t = src;
  t.trim();
  if (t.length() == 0) return false;
  if (t.startsWith("-")) return false;

  char *endp = nullptr;
  unsigned long v = strtoul(t.c_str(), &endp, 10);

  if (endp == t.c_str() || *endp != '\0') return false;

  out = (uint32_t)v;
  return true;
}

static bool parse_double_dec(const String &src, double &out) {
  String t = src;
  t.trim();
  if (t.length() == 0) return false;

  char *endp = nullptr;
  double v = strtod(t.c_str(), &endp);

  if (endp == t.c_str() || *endp != '\0' || !isfinite(v)) return false;

  out = v;
  return true;
}

// ============================
// ===== WEB / API (HTML/JS) ===
// ============================
static void addCors() {
  server.sendHeader("Access-Control-Allow-Origin", "*");
  server.sendHeader("Access-Control-Allow-Methods", "GET,POST,OPTIONS");
  server.sendHeader("Access-Control-Allow-Headers", "Content-Type");
  server.sendHeader("Access-Control-Max-Age", "86400");
  server.sendHeader("Cache-Control", "no-store");
}

static void handleOptions() {
  addCors();
  server.send(204, "text/plain", "");
}

static bool mount_spiffs() {
  if (SPIFFS.begin(false)) {
    Serial.println("SPIFFS OK");
    Serial.printf("SPIFFS total=%u used=%u\n",
                  (unsigned)SPIFFS.totalBytes(),
                  (unsigned)SPIFFS.usedBytes());
    return true;
  }
  Serial.println("SPIFFS FAIL (no se formatea). Revisa Partition Scheme y Data Upload.");
  return false;
}

static const char* guessMime(const String& path) {
  if (path.endsWith(".html") || path.endsWith(".htm")) return "text/html; charset=utf-8";
  if (path.endsWith(".css"))  return "text/css; charset=utf-8";
  if (path.endsWith(".js"))   return "application/javascript; charset=utf-8";
  if (path.endsWith(".json")) return "application/json; charset=utf-8";
  if (path.endsWith(".png"))  return "image/png";
  if (path.endsWith(".jpg") || path.endsWith(".jpeg")) return "image/jpeg";
  if (path.endsWith(".svg"))  return "image/svg+xml";
  if (path.endsWith(".ico"))  return "image/x-icon";
  return "application/octet-stream";
}

static bool serveFromFS(const String& path) {
  String p = path;
  if (!p.startsWith("/")) p = "/" + p;
  if (!SPIFFS.exists(p)) return false;

  File f = SPIFFS.open(p, "r");
  if (!f) return false;

  addCors();
  server.streamFile(f, guessMime(p));
  f.close();
  return true;
}

static void setup_wifi_ap_and_server() {
  WiFi.mode(WIFI_AP);
  bool ok = WiFi.softAP(AP_SSID, AP_PASS);
  Serial.printf("AP %s: %s\n", AP_SSID, ok ? "OK" : "FAIL");
  Serial.print("AP IP: ");
  Serial.println(WiFi.softAPIP());

  // /fs
  server.on("/fs", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }
    addCors();

    String out = "SPIFFS:\n";
    File root = SPIFFS.open("/", "r");
    if (!root) {
      server.send(500, "text/plain; charset=utf-8", "SPIFFS open('/') FAIL");
      return;
    }
    File file = root.openNextFile();
    while (file) {
      out += String(file.name()) + "  (" + String((unsigned)file.size()) + " bytes)\n";
      file = root.openNextFile();
    }
    server.send(200, "text/plain; charset=utf-8", out);
  });

  // /cmd (unificado)
  server.on("/cmd", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }
    if (!server.hasArg("do")) { addCors(); server.send(400, "text/plain", "Missing do"); return; }

    String d = server.arg("do");
    d.trim(); d.toLowerCase();

    // Servo/Stepper: directo (Core0)
    if (d.startsWith("servo1_") || d.startsWith("servo2_") ||
        d.startsWith("servo_")  || d.startsWith("step_")) {
#if CAN_ONLY_MODE
      addCors();
      server.send(200, "text/plain", "DISABLED: CAN_ONLY_MODE");
      return;
#else
      // SERVO 1 - secuencia gestionada por firmware
      if (d == "servo1_son_on") {
        servo_son(SERVO1, true);
      }
      else if (d == "servo1_son_off") {
        servo_son(SERVO1, false);
      }
      else if (d == "servo1_sequence_stop") {
        servo1_stop_sequence_cmd();
      }
      else if (d == "servo1_run_off") {
        servo1_cancel_keep_position();
      }
      else if (d == "servo1_run_on" ||
               d == "servo1_dir_0" ||
               d == "servo1_dir_1") {
        addCors();
        server.send(
          409,
          "text/plain",
          "SERVO1 se controla por configuracion de secuencia en firmware"
        );
        return;
      }

      // SERVO 2
      else if (d == "servo2_son_on")       servo_son(SERVO2, true);
      else if (d == "servo2_son_off")      servo_son(SERVO2, false);
      else if (d == "servo2_run_on")       servo_run(SERVO2, true);
      else if (d == "servo2_run_off")      servo_run(SERVO2, false);
      else if (d == "servo2_dir_0")        servo_set_dir(SERVO2, 0);
      else if (d == "servo2_dir_1")        servo_set_dir(SERVO2, 1);

      // Compatibilidad con el HTML/cliente anterior:
      // servo_* sigue apuntando al SERVO 2.
      else if (d == "servo_son_on")       servo_son(SERVO2, true);
      else if (d == "servo_son_off")      servo_son(SERVO2, false);
      else if (d == "servo_run_on")       servo_run(SERVO2, true);
      else if (d == "servo_run_off")      servo_run(SERVO2, false);
      else if (d == "servo_dir_0")        servo_set_dir(SERVO2, 0);
      else if (d == "servo_dir_1")        servo_set_dir(SERVO2, 1);

      // STEPPER
#if STEPPER_ENABLED
      else if (d == "step_run_on")   step_start();
      else if (d == "step_run_off")  step_stop();
      else if (d == "step_dir_0")    step_set_dir_safe(0);
      else if (d == "step_dir_1")    step_set_dir_safe(1);
      else if (d == "step_rev")      step_rev_start();
#else
      else if (d.startsWith("step_")) {
        addCors();
        server.send(200, "text/plain", "DISABLED: STEPPER_ENABLED=0");
        return;
      }
#endif

      addCors();
      server.send(200, "text/plain", "OK");
      return;
#endif
    }

    // CAN: por cola
    bool okq = enqueueDo(d.c_str());
    addCors();
    server.send(okq ? 200 : 500, "text/plain", okq ? "QUEUED" : "QUEUE FAIL");
  });

  // /servo1/config?p1=&p2=&p3=&seq=&hz=
  // p1/p2/p3 llegan en ESCALA OPERADOR: 1000 unidades = 1 vuelta.
  // Firmware convierte internamente a 256 pasos físicos/vuelta.
  // Dashboard configura/inicia. Firmware ejecuta el ciclo continuo.
  server.on("/servo1/config", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }
    addCors();

#if CAN_ONLY_MODE
    server.send(200, "text/plain", "DISABLED: CAN_ONLY_MODE");
    return;
#else
    if (!server.hasArg("p1") ||
        !server.hasArg("p2") ||
        !server.hasArg("p3") ||
        !server.hasArg("seq") ||
        !server.hasArg("hz")) {
      server.send(400, "text/plain", "Missing p1/p2/p3/seq/hz");
      return;
    }

    int32_t p1 = 0, p2 = 0, p3 = 0;
    double seq_hz = 0.0;

    if (!parse_i32_dec(server.arg("p1"), p1) ||
        !parse_i32_dec(server.arg("p2"), p2) ||
        !parse_i32_dec(server.arg("p3"), p3)) {
      server.send(400, "text/plain", "ERROR: P1/P2/P3 invalidas");
      return;
    }

    if (!parse_double_dec(server.arg("hz"), seq_hz) || seq_hz <= 0.0) {
      server.send(400, "text/plain", "ERROR: frecuencia de secuencia debe ser > 0 Hz");
      return;
    }

    String seq = server.arg("seq");
    String msg;

    bool ok = servo1_configure_and_start_sequence(
      p1, p2, p3, seq, seq_hz, msg
    );

    if (!ok) {
      const int code = msg.startsWith("BUSY") ? 409 : 400;
      server.send(code, "text/plain", msg);
      return;
    }

    server.send(200, "text/plain", msg);
#endif
  });

  // /send (CAN)
  server.on("/send", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }
    if (!server.hasArg("line")) { addCors(); server.send(400, "text/plain", "Missing line"); return; }

    String line = server.arg("line");
    line.replace("+", " ");
    line.trim();

    bool okq = enqueueSendLine(line.c_str());
    addCors();
    server.send(okq ? 200 : 500, "text/plain", okq ? "QUEUED" : "QUEUE FAIL");
  });

  // /set (servo/step)
  server.on("/set", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }
    addCors();

#if CAN_ONLY_MODE
    server.send(200, "text/plain", "DISABLED: CAN_ONLY_MODE");
#else
    // SERVO 1 no recibe parámetros físicos desde dashboard.
    // SERVO1_STEPS_PER_REV y SERVO1_PHYSICAL_FREQ_HZ
    // son parámetros internos hardcodeados en firmware.

    // SERVO 2
    if (server.hasArg("servo2_hz"))
      servo_set_hz(SERVO2, (uint32_t)server.arg("servo2_hz").toInt(), "SERVO2");

    if (server.hasArg("servo2_duty"))
      servo_set_duty_pct(SERVO2, (uint8_t)server.arg("servo2_duty").toInt());

    // Compatibilidad antigua -> SERVO 2
    if (server.hasArg("servo_hz"))
      servo_set_hz(SERVO2, (uint32_t)server.arg("servo_hz").toInt(), "SERVO2");

    if (server.hasArg("servo_duty"))
      servo_set_duty_pct(SERVO2, (uint8_t)server.arg("servo_duty").toInt());

#if STEPPER_ENABLED
    if (server.hasArg("step_hz"))    step_apply_from_freq((uint32_t)server.arg("step_hz").toInt());
#endif

    server.send(200, "text/plain", "OK");
#endif
  });

  // /status (JSON combinado)
  server.on("/status", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }

    String s = "{";
    s += "\"can_started\":" + String(can_started ? "true" : "false");
    s += ",\"script_running\":" + String(script_running ? "true" : "false");
    s += ",\"script_loop\":" + String(script_loop ? "true" : "false");

    TesteoPublic tp;
    portENTER_CRITICAL(&g_tpubMux);
    tp = g_tpub;
    portEXIT_CRITICAL(&g_tpubMux);

    uint32_t age = (tp.t_ms == 0) ? 0 : (uint32_t)(millis() - tp.t_ms);
    char lastHex[3]; snprintf(lastHex, sizeof(lastHex), "%02X", tp.last_code);

    s += ",\"test_state\":" + String(tp.state);
    s += ",\"test_armed\":" + String(tp.armed);
    s += ",\"test_tries\":" + String(tp.tries);
    s += ",\"test_age_ms\":" + String(age);
    s += ",\"test_last\":\"" + String(lastHex) + "\"";
    s += ",\"test_result\":\"" + String(tp.result) + "\"";

    // ✅ INIT state
    InitPublic ip;
    portENTER_CRITICAL(&g_ipubMux);
    ip = g_ipub;
    portEXIT_CRITICAL(&g_ipubMux);

    uint32_t init_age = (ip.t_ms == 0) ? 0 : (uint32_t)(millis() - ip.t_ms);
    s += ",\"init_state\":" + String(ip.state);
    s += ",\"init_run_id\":" + String((unsigned long)ip.run_id);
    s += ",\"init_i\":" + String((unsigned)ip.i);
    s += ",\"init_n\":" + String((unsigned)ip.n);
    s += ",\"init_age_ms\":" + String((unsigned long)init_age);
    s += ",\"init_tag\":\"" + String(ip.tag) + "\"";
    s += ",\"init_msg\":\"" + String(ip.msg) + "\"";

    // Servo/Stepper
#if CAN_ONLY_MODE
    s += ",\"servo1_disabled\":true";
    s += ",\"servo1_son\":false";
    s += ",\"servo1_run\":false";
    s += ",\"servo1_hz\":0";
    s += ",\"servo1_hz_real\":0";
    s += ",\"servo1_duty_pct\":0";
    s += ",\"servo1_dir\":0";
    s += ",\"servo1_moving\":false";
    s += ",\"servo1_position_valid\":false";
    s += ",\"servo1_position\":0";
    s += ",\"servo1_target\":0";
    s += ",\"servo1_steps_per_rev\":" + String((unsigned long)SERVO1_STEPS_PER_REV);
    s += ",\"servo1_operator_units_per_rev\":" + String((long)SERVO1_OPERATOR_UNITS_PER_REV);
    s += ",\"servo1_position_units\":0";
    s += ",\"servo1_target_units\":0";
    s += ",\"servo1_motor_speed_rpm\":" + String((unsigned long)SERVO1_MOTOR_SPEED_RPM);
    s += ",\"servo1_physical_freq_hz\":" + String((unsigned long)SERVO1_PHYSICAL_FREQ_HZ);
    s += ",\"servo1_steps_total\":0";
    s += ",\"servo1_steps_done\":0";
    s += ",\"servo1_steps_remaining\":0";
    s += ",\"servo1_seq_active\":false";
    s += ",\"servo1_seq_index\":0";
    s += ",\"servo1_seq_len\":0";
    s += ",\"servo1_seq_hz\":0";
    s += ",\"servo1_seq\":\"\"";
    s += ",\"servo1_p1\":0";
    s += ",\"servo1_p2\":0";
    s += ",\"servo1_p3\":0";
    s += ",\"servo1_p1_units\":0";
    s += ",\"servo1_p2_units\":0";
    s += ",\"servo1_p3_units\":0";
    s += ",\"servo1_p1_steps\":0";
    s += ",\"servo1_p2_steps\":0";
    s += ",\"servo1_p3_steps\":0";

    s += ",\"servo2_disabled\":true";
    s += ",\"servo2_son\":false";
    s += ",\"servo2_run\":false";
    s += ",\"servo2_hz\":0";
    s += ",\"servo2_hz_real\":0";
    s += ",\"servo2_duty_pct\":0";
    s += ",\"servo2_dir\":0";

    // Legacy = SERVO 2
    s += ",\"servo_disabled\":true";
    s += ",\"servo_son\":false";
    s += ",\"servo_run\":false";
    s += ",\"servo_hz\":0";
    s += ",\"servo_hz_real\":0";
    s += ",\"servo_duty_pct\":0";
    s += ",\"servo_dir\":0";
    s += ",\"step_disabled\":true";
    s += ",\"step_run\":false";
    s += ",\"step_hz\":0";
    s += ",\"step_hz_real\":0";
    s += ",\"step_dir\":0";
    s += ",\"step_pulse_gpio\":0";
    s += ",\"step_dir_gpio\":0";
    s += ",\"step_rev_busy\":false";
#else
    s += ",\"servo1_disabled\":false";
    s += ",\"servo1_son\":" + String(servo_son_on[SERVO1] ? "true":"false");
    s += ",\"servo1_run\":" + String(servo1_motion_active ? "true":"false");
    s += ",\"servo1_moving\":" + String(servo1_motion_active ? "true":"false");
    s += ",\"servo1_position_valid\":" + String(servo1_position_valid ? "true":"false");
    s += ",\"servo1_position\":" + String((long)servo1_position);
    s += ",\"servo1_target\":" + String((long)servo1_target);

    // Valores para dashboard/operador en escala 1000 unidades/vuelta.
    s += ",\"servo1_position_units\":" +
         String(servo1_physical_steps_to_units(servo1_position), 4);
    s += ",\"servo1_target_units\":" + String((long)servo1_target_units);
    s += ",\"servo1_operator_units_per_rev\":" +
         String((long)SERVO1_OPERATOR_UNITS_PER_REV);

    s += ",\"servo1_steps_per_rev\":" + String((unsigned long)SERVO1_STEPS_PER_REV);
    s += ",\"servo1_motor_speed_rpm\":" + String((unsigned long)SERVO1_MOTOR_SPEED_RPM);
    s += ",\"servo1_physical_freq_hz\":" + String((unsigned long)SERVO1_PHYSICAL_FREQ_HZ);
    s += ",\"servo1_steps_total\":" + String((unsigned long)servo1_steps_total);
    s += ",\"servo1_steps_done\":" + String((unsigned long)servo1_steps_done);
    s += ",\"servo1_steps_remaining\":" + String((unsigned long)servo1_steps_remaining);
    s += ",\"servo1_hz\":" + String(servo1_physical_hz(), 2);
    s += ",\"servo1_hz_real\":" + String(servo1_physical_hz(), 2);
    s += ",\"servo1_duty_pct\":50";
    s += ",\"servo1_dir\":" + String(digitalRead(SERVO_DIR[SERVO1]) ? 1:0);
    s += ",\"servo1_seq_active\":" + String(servo1_sequence_active ? "true":"false");
    s += ",\"servo1_seq_index\":" + String((unsigned)servo1_sequence_index);
    s += ",\"servo1_seq_len\":" + String((unsigned)servo1_sequence_len);
    s += ",\"servo1_seq_hz\":" + String(servo1_sequence_hz, 4);
    s += ",\"servo1_seq\":\"" + String(servo1_sequence) + "\"";
    // Compatibilidad/dashboard: los nombres genéricos P1/P2/P3
    // también reportan ESCALA OPERADOR, nunca pasos físicos.
    s += ",\"servo1_p1\":" + String((long)servo1_positions_units[0]);
    s += ",\"servo1_p2\":" + String((long)servo1_positions_units[1]);
    s += ",\"servo1_p3\":" + String((long)servo1_positions_units[2]);

    s += ",\"servo1_p1_units\":" + String((long)servo1_positions_units[0]);
    s += ",\"servo1_p2_units\":" + String((long)servo1_positions_units[1]);
    s += ",\"servo1_p3_units\":" + String((long)servo1_positions_units[2]);

    // Debug físico explícito.
    s += ",\"servo1_p1_steps\":" + String((long)servo1_positions[0]);
    s += ",\"servo1_p2_steps\":" + String((long)servo1_positions[1]);
    s += ",\"servo1_p3_steps\":" + String((long)servo1_positions[2]);

    s += ",\"servo2_disabled\":false";
    s += ",\"servo2_son\":" + String(servo_son_on[SERVO2] ? "true":"false");
    s += ",\"servo2_run\":" + String(servo_running[SERVO2] ? "true":"false");
    s += ",\"servo2_hz\":" + String((unsigned long)servo_hz[SERVO2]);
    s += ",\"servo2_hz_real\":" + String((double)ledcReadFreq(SERVO_PULSE[SERVO2]), 1);
    s += ",\"servo2_duty_pct\":" + String((unsigned)((uint32_t)servo_duty[SERVO2] * 100u / ((1u<<SERVO_LEDC_RES)-1u)));
    s += ",\"servo2_dir\":" + String(digitalRead(SERVO_DIR[SERVO2]) ? 1:0);

    // Legacy = SERVO 2
    s += ",\"servo_disabled\":false";
    s += ",\"servo_son\":" + String(servo_son_on[SERVO2] ? "true":"false");
    s += ",\"servo_run\":" + String(servo_running[SERVO2] ? "true":"false");
    s += ",\"servo_hz\":" + String((unsigned long)servo_hz[SERVO2]);
    s += ",\"servo_hz_real\":" + String((double)ledcReadFreq(SERVO_PULSE[SERVO2]), 1);
    s += ",\"servo_duty_pct\":" + String((unsigned)((uint32_t)servo_duty[SERVO2] * 100u / ((1u<<SERVO_LEDC_RES)-1u)));
    s += ",\"servo_dir\":" + String(digitalRead(SERVO_DIR[SERVO2]) ? 1:0);

#if STEPPER_ENABLED
    s += ",\"step_disabled\":false";
    s += ",\"step_run\":" + String(step_running ? "true":"false");
    s += ",\"step_hz\":" + String((unsigned long)step_hz);
    s += ",\"step_hz_real\":" + String(step_real_hz(), 2);
    s += ",\"step_dir\":" + String(digitalRead(STEP_PIN_DIR) ? 1:0);
    s += ",\"step_pulse_gpio\":" + String(STEP_PIN_STEP);
    s += ",\"step_dir_gpio\":" + String(STEP_PIN_DIR);
    s += ",\"step_rev_busy\":false";
#else
    s += ",\"step_disabled\":true";
    s += ",\"step_run\":false";
    s += ",\"step_hz\":0";
    s += ",\"step_hz_real\":0";
    s += ",\"step_dir\":0";
    s += ",\"step_pulse_gpio\":0";
    s += ",\"step_dir_gpio\":0";
    s += ",\"step_rev_busy\":false";
#endif
#endif

    s += "}";

    addCors();
    server.send(200, "application/json", s);
  });

  // /rx
  server.on("/rx", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }

    LastRxFrame local;
    portENTER_CRITICAL(&g_lastRxMux);
    local = g_lastRx;
    portEXIT_CRITICAL(&g_lastRxMux);

    uint32_t age = (uint32_t)(millis() - local.t_ms);

    char dataStr[3*8 + 1];
    int pos = 0;
    for (int i=0;i<(int)local.dlc && i<8;i++){
      pos += snprintf(&dataStr[pos], sizeof(dataStr)-pos, "%02X%s", local.data[i], (i+1<local.dlc)?" ":"");
      if (pos >= (int)sizeof(dataStr)) break;
    }
    if (local.dlc == 0) dataStr[0] = '\0';

    char json[256];
    snprintf(json, sizeof(json),
      "{\"count\":%lu,\"age_ms\":%lu,\"id\":\"%03lX\",\"dlc\":%u,\"rtr\":%u,\"ext\":%u,\"data\":\"%s\"}",
      (unsigned long)local.count,
      (unsigned long)age,
      (unsigned long)(local.id & 0x7FF),
      (unsigned)local.dlc,
      (unsigned)local.rtr,
      (unsigned)local.extd,
      dataStr
    );

    addCors();
    server.send(200, "application/json", json);
  });

  // ROOT "/"
  server.on("/", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }
    if (!serveFromFS("/index.html")) {
      addCors();
      server.send(404, "text/plain; charset=utf-8", "No /index.html en SPIFFS");
    }
  });

  // UI2 "/b" -> /ui2.html (opcional)
  server.on("/b", HTTP_ANY, []() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }
    if (!serveFromFS("/ui2.html")) {
      addCors();
      server.send(404, "text/plain; charset=utf-8", "No /ui2.html en SPIFFS");
    }
  });

  // fallback
  server.onNotFound([]() {
    if (server.method() == HTTP_OPTIONS) { handleOptions(); return; }
    if (serveFromFS(server.uri())) return;
    addCors();
    server.send(404, "text/plain; charset=utf-8", "Not found");
  });

  server.begin();
  Serial.println("HTTP server started");
}

// ============================
// =========== SETUP ==========
// ============================
void setup() {
  Serial.begin(115200);
  delay(200);

  Serial.println("\n--- FW: SERVO1 SECUENCIA + SERVO2 MANUAL + STEPPER PULSE/DIR + CAN + WiFi ---");
  Serial.println("Web: WiFi ESP32_TEST (12345678)");
  Serial.println("UI1(CAN):   http://192.168.4.1/");
  Serial.println("UI2(SERVOS 1+2): http://192.168.4.1/b");
  Serial.println("API: /cmd?do=...   /send?line=...   /set?...   /status   /rx   /fs");
  Serial.println("Prueba CAN: escribe secuencia (una vez) o secuencia stop.");

  mount_spiffs();

  // CAN STBY siempre activo: LOW = modo normal del transceiver.
  pinMode((int)CAN_STBY, OUTPUT);
  digitalWrite((int)CAN_STBY, LOW);

  // Cola para Core1
  cmdQ = xQueueCreate(32, sizeof(CmdMsg));

  // init pub idle
  init_pub_finish(I_IDLE, "IDLE", "IDLE");

  // CAN start
  can_start();
  build_my_animation_script();

#if CAN_ONLY_MODE
  Serial.println("CAN_ONLY_MODE: servo/stepper desactivados.");
#else
  // SERVO 1 init - posicionamiento absoluto
  servo_pwm_init(SERVO1, "SERVO1");

  esp_timer_create_args_t servo1_timer_args = {};
  servo1_timer_args.callback = &servo1_pulse_timer_cb;
  servo1_timer_args.arg = nullptr;
  servo1_timer_args.name = "servo1_pulse";

  if (esp_timer_create(&servo1_timer_args, &servo1_pulse_timer) != ESP_OK) {
    Serial.println("ERROR: no pude crear esp_timer de SERVO1");
  }

  servo1_reset_sequence_config();
  servo1_son_off_cmd();

  // SERVO 2 init
  servo_pwm_init(SERVO2, "SERVO2");
  servo_set_hz(SERVO2, 1000, "SERVO2");
  servo_set_duty_pct(SERVO2, 50);
  servo_son(SERVO2, false);
  servo_run(SERVO2, false);

#if STEPPER_ENABLED
  // STEPPER PULSE/DIR
  stepper_init();
  step_apply_from_freq(STEP_DEFAULT_HZ);
  step_stop();
#else
  Serial.println("STEPPER deshabilitado por STEPPER_ENABLED=0.");
#endif
#endif

  // Web
  setup_wifi_ap_and_server();

  // Core1 task (CAN)
  xTaskCreatePinnedToCore(
    can_task,
    "CAN_TASK",
    8192,
    nullptr,
    2,
    &canTaskHandle,
    1
  );
}

void loop() {
  server.handleClient();

#if !CAN_ONLY_MODE
  servo1_position_tick();
  servo1_sequence_tick();
#endif

  delay(1);
}
