// =======================================================
// FW UNIFICADO:
// (A) ESP32 CAN (TWAI) + INIT (bloqueante) + SCRIPT ENGINE (NO bloqueante)
//     + WiFi AP + HTTP API
//     + Core split: Core1 (CAN/script/serial + ejecuta comandos), Core0 (WiFi/HTTP)
//     IMPORTANTE: el HTTP NO toca TWAI directo -> TODO va por COLA (queue) al Core1
//     + SPIFFS: sirve /index.html desde el ESP32 (UI estable en celular)
//     + RX->UI: endpoint /rx (último frame recibido) SIN LENTEAR
//     + TESTEO v2 ENCLAVADO + REARME por 0x702 DLC=2 DATA=3F 00
//
// (B) SERVO DRIVE (LEDC PULSE/DIR/SON) + STEPPER (esp_timer STEP/DIR)
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

#pragma once
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

// ------------------- CAN PINS -------------------
static const gpio_num_t CAN_TX = GPIO_NUM_4;
static const gpio_num_t CAN_RX = GPIO_NUM_34;

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
  "320 07", "WAIT 2000", "320 30", "WAIT 2000", "370 fd 06 10 00", "WAIT 2000",
  "370 fd 06 11 00", "WAIT 2000", "320 2d 00 bf ff", "WAIT 2000", "320 00", "320 07",
  "320 07", "320 07", "320 07", "320 02", "320 07", "320 25 07",
  "320 05", "320 19 04", "320 1a 19", "320 4d 18 0d 00", "320 4d 19 0d 00", "320 2b 00 03",
  "320 2c 00 03", "320 43 00", "320 2d 00 bf ff", "320 4c 02 32 00", "320 5a 08 b0 04", "320 5a 09 b0 04",
  "320 48 00 01 00", "320 00", "320 53 00 ff 03", "320 38 00 5a 01", "320 54 00", "320 58 00 00",
  "320 54 01", "320 58 01 00", "320 54 02", "320 58 02 00", "320 54 03", "320 58 03 00",
  "320 54 04", "320 58 04 00", "320 54 05", "320 58 05 00", "320 54 06", "320 58 06 00",
  "320 54 07", "320 58 07 00", "320 54 08", "320 58 08 00", "320 54 09", "320 58 09 00",
  "320 07", "320 1e 18 01", "320 1e 19 01", "320 1e 1a 01", "320 1e 1b 01", "320 1e 1c 01",
  "320 1e 1d 01", "320 1e 1e 01", "320 1e 1f 01", "320 1e 20 01", "320 1e 21 01", "320 1e 22 01",
  "320 1e 23 01", "320 1e 24 01", "320 1e 25 01", "320 1e 26 01", "320 1e 27 01",
};
static const size_t INIT1_N = sizeof(INIT1_SEQ) / sizeof(INIT1_SEQ[0]);
static uint32_t INIT1_DELAY_MS = 80;

static uint32_t INIT_GAP_MS = 5000;

static const char* INIT2_SEQ[] = {
  "320 30","WAIT 2000","320 30","WAIT 2000",
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
  "320 1c 09 08 00","WAIT 200","320 1c 09 00 00","WAIT 200","320 07","320 0e 09",
};
static const size_t INIT2_N = sizeof(INIT2_SEQ) / sizeof(INIT2_SEQ[0]);
static uint32_t INIT2_DELAY_MS = 200;

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

static inline void j_set_pin(uint8_t j1to8, uint8_t pin1to8, bool on){
  if (j1to8 < 1 || j1to8 > 8) return;
  if (pin1to8 < 1 || pin1to8 > 8) return;
  uint8_t &r = j_regs[j1to8-1];
  uint8_t b = pin1to8 - 1;
  if (on) r &= ~(1u<<b); else r |= (1u<<b);
}

static inline void can_send_j(uint8_t j1to8){
  if (j1to8 < 1 || j1to8 > 8) return;
  uint8_t sub = (uint8_t)(j1to8 - 1);         // 0..7
  uint8_t xx  = j_regs[j1to8-1];
  uint8_t data[3] = { 0x1D, sub, xx };         // 320 1D sub xx
  send_frame_std(0x320, data, 3);
}

// Yarn/Stitch ADDR de 1E
static const uint8_t YARN1_ADDR[8]  = {0x18,0x19,0x1A,0x1B,0x1C,0x1D,0x1E,0x1F};
static const uint8_t YARN2_ADDR[8]  = {0x24,0x25,0x26,0x27,0x20,0x21,0x22,0x23};

static const uint8_t ST1_ADDR[4] = {0x00,0x01,0x02,0x05};
static const uint8_t ST2_ADDR[4] = {0x06,0x07,0x08,0x0B};
static const uint8_t ST3_ADDR[4] = {0x0C,0x0D,0x0E,0x11};
static const uint8_t ST4_ADDR[4] = {0x12,0x13,0x14,0x17};

static inline void can_send_1e(uint8_t addr, uint8_t val){
  uint8_t data[3] = { 0x1E, addr, val };       // 320 1E addr val
  send_frame_std(0x320, data, 3);
}

// Cascades
static Cascade runJ[8];
static Cascade runY1, runY2;
static Cascade runS[4];

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

static void tick_run_yarn(Cascade &c, const uint8_t *addr, uint8_t n){
  if (!c.running) return;

  uint32_t now = millis();
  if ((int32_t)(now - c.next_ms) < 0) return;
  c.next_ms = now + c.delay_ms;

  if (c.phase == 0) {
    can_send_1e(addr[c.p-1], 0x01);
    c.p++;
    if (c.p > n) { c.phase = 1; c.p = 1; }
  } else {
    can_send_1e(addr[c.p-1], 0x00);
    c.p++;
    if (c.p > n) { c.phase = 0; c.p = 1; }
  }
}

static void tick_run_stitch(uint8_t idx0to3){
  Cascade &c = runS[idx0to3];
  const uint8_t *addr = (idx0to3==0)?ST1_ADDR:(idx0to3==1)?ST2_ADDR:(idx0to3==2)?ST3_ADDR:ST4_ADDR;
  tick_run_yarn(c, addr, 4);
}

// ====================== SERVO DRIVE (LEDC) ======================
static const int SERVO_PULSE = 25;
static const int SERVO_DIR   = 26;
static const int SERVO_SON   = 27;

static const uint8_t SERVO_LEDC_RES = 6;
static volatile uint32_t servo_hz   = 1000;
static volatile uint8_t  servo_duty = 32;
static volatile bool servo_running  = false;
static volatile bool servo_son_on   = false;

static void servo_pwm_init() {
  pinMode(SERVO_DIR, OUTPUT);
  digitalWrite(SERVO_DIR, LOW);

  pinMode(SERVO_SON, OUTPUT);
  digitalWrite(SERVO_SON, LOW);

  bool ok = ledcAttach(SERVO_PULSE, (double)servo_hz, SERVO_LEDC_RES);
  if (!ok) Serial.println("LEDC attach FAIL en SERVO_PULSE");

  ledcWrite(SERVO_PULSE, 0);
}

static void servo_son(bool on) {
  servo_son_on = on;
  digitalWrite(SERVO_SON, on ? HIGH : LOW);
}

static void servo_run(bool on) {
  servo_running = on;
  ledcWrite(SERVO_PULSE, on ? servo_duty : 0);
}

static void servo_set_hz(uint32_t hz) {
  if (hz < 1) hz = 1;
  if (hz > 160000) hz = 160000;

  servo_hz = hz;
  ledcChangeFrequency(SERVO_PULSE, (double)servo_hz, SERVO_LEDC_RES);

  if (servo_running) ledcWrite(SERVO_PULSE, servo_duty);

  Serial.printf("SERVO hz req=%lu  real=%.1f\n",
                (unsigned long)servo_hz,
                ledcReadFreq(SERVO_PULSE));
}

static void servo_set_duty_pct(uint8_t pct) {
  if (pct > 100) pct = 100;
  uint32_t maxv = (1u << SERVO_LEDC_RES) - 1u;
  servo_duty = (uint8_t)((pct * maxv) / 100u);
  if (servo_running) ledcWrite(SERVO_PULSE, servo_duty);
}

// ====================== STEPPER (esp_timer) ======================
static const int STEP_PIN_STEP = 32;
static const int STEP_PIN_DIR  = 33;

static volatile uint32_t step_hz = 200;
static volatile bool step_running = false;
static volatile bool step_level = false;

static esp_timer_handle_t step_timer = nullptr;
static portMUX_TYPE step_mux = portMUX_INITIALIZER_UNLOCKED;

static inline uint32_t clamp_u32(uint32_t v, uint32_t lo, uint32_t hi){
  if (v < lo) return lo;
  if (v > hi) return hi;
  return v;
}

static void step_timer_cb(void* arg) {
  (void)arg;
  if (!step_running) return;
  step_level = !step_level;
  gpio_set_level((gpio_num_t)STEP_PIN_STEP, step_level ? 1 : 0);
}

static void step_apply_from_freq(uint32_t hz) {
  hz = clamp_u32(hz, 1, 200000);
  portENTER_CRITICAL(&step_mux);
  step_hz = hz;
  portEXIT_CRITICAL(&step_mux);

  uint64_t half_us = 1000000ULL / (2ULL * (uint64_t)step_hz);
  if (half_us < 2) half_us = 2;

  if (step_timer && step_running) {
    esp_timer_stop(step_timer);
    esp_timer_start_periodic(step_timer, half_us);
  }
}

static void step_start() {
  if (!step_timer) return;

  step_level = false;
  gpio_set_level((gpio_num_t)STEP_PIN_STEP, 0);

  step_running = true;

  uint64_t half_us = 1000000ULL / (2ULL * (uint64_t)step_hz);
  if (half_us < 2) half_us = 2;

  esp_timer_stop(step_timer);
  esp_timer_start_periodic(step_timer, half_us);
}

static void step_stop() {
  step_running = false;
  if (step_timer) esp_timer_stop(step_timer);
  step_level = false;
  gpio_set_level((gpio_num_t)STEP_PIN_STEP, 0);
}

static void step_set_dir_safe(int dir) {
  bool was = step_running;
  step_stop();
  delay(3);
  digitalWrite(STEP_PIN_DIR, dir ? HIGH : LOW);
  delay(3);
  if (was) step_start();
}

struct StepRevSM {
  bool active = false;
  uint32_t f_hold = 0;
  uint32_t f_low  = 200;
  int dir_target = 0;
  uint32_t next_ms = 0;
  uint16_t df = 200;
  uint16_t ms = 15;
  uint8_t phase = 0;
};

static StepRevSM stepRev;

static void step_rev_start() {
  int dir_now = digitalRead(STEP_PIN_DIR);

  if (!step_running) {
    step_set_dir_safe(!dir_now);
    return;
  }

  stepRev.active = true;
  stepRev.phase = 0;
  stepRev.f_hold = step_hz;
  stepRev.f_low = (stepRev.f_hold < 400) ? 1 : 200;
  stepRev.dir_target = !dir_now;
  stepRev.next_ms = millis();
}

static void step_rev_tick() {
  if (!stepRev.active) return;

  uint32_t now = millis();
  if ((int32_t)(now - stepRev.next_ms) < 0) return;
  stepRev.next_ms = now + stepRev.ms;

  if (stepRev.phase == 0) {
    uint32_t f = step_hz;
    if (f <= stepRev.f_low + stepRev.df) {
      step_apply_from_freq(stepRev.f_low);
      stepRev.phase = 1;
      stepRev.next_ms = now + 5;
      return;
    }
    step_apply_from_freq(f - stepRev.df);
    return;
  }

  if (stepRev.phase == 1) {
    step_stop();
    delay(3);
    digitalWrite(STEP_PIN_DIR, stepRev.dir_target ? HIGH : LOW);
    delay(3);
    step_start();
    step_apply_from_freq(stepRev.f_low);
    stepRev.phase = 2;
    return;
  }

  if (stepRev.phase == 2) {
    uint32_t f = step_hz;
    if (f + stepRev.df >= stepRev.f_hold) {
      step_apply_from_freq(stepRev.f_hold);
      stepRev.phase = 3;
      return;
    }
    step_apply_from_freq(f + stepRev.df);
    return;
  }

  stepRev.active = false;
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
    for (int i=0;i<8;i++) { runJ[i].running = false; cascade_start(runJ[i], 80); }
    Serial.println("OK j_run_all");
  }
  else if (d == "j_stop_all") {
    for (int i=0;i<8;i++) cascade_stop(runJ[i]);
    Serial.println("OK j_stop_all");
  }
  else if (d.startsWith("j_run_")) {
    int n = d.substring(6).toInt();
    if (n>=1 && n<=8) { runJ[n-1].running = false; cascade_start(runJ[n-1], 80); Serial.printf("OK j_run_%d\n", n); }
    else Serial.println("BAD j_run_n");
  }
  else if (d.startsWith("j_stop_")) {
    int n = d.substring(7).toInt();
    if (n>=1 && n<=8) { cascade_stop(runJ[n-1]); Serial.printf("OK j_stop_%d\n", n); }
    else Serial.println("BAD j_stop_n");
  }

  else if (d == "y_run_all") {
    runY1.running = false; cascade_start(runY1, 80);
    runY2.running = false; cascade_start(runY2, 80);
    Serial.println("OK y_run_all");
  }
  else if (d == "y_stop_all") {
    cascade_stop(runY1);
    cascade_stop(runY2);
    Serial.println("OK y_stop_all");
  }
  else if (d == "y1_run") { runY1.running = false; cascade_start(runY1, 80); Serial.println("OK y1_run"); }
  else if (d == "y1_stop"){ cascade_stop(runY1);      Serial.println("OK y1_stop"); }
  else if (d == "y2_run") { runY2.running = false; cascade_start(runY2, 80); Serial.println("OK y2_run"); }
  else if (d == "y2_stop"){ cascade_stop(runY2);      Serial.println("OK y2_stop"); }

  else if (d == "s_run_all") {
    for (int i=0;i<4;i++) { runS[i].running = false; cascade_start(runS[i], 80); }
    Serial.println("OK s_run_all");
  }
  else if (d == "s_stop_all") {
    for (int i=0;i<4;i++) cascade_stop(runS[i]);
    Serial.println("OK s_stop_all");
  }
  else if (d.startsWith("s_run_")) {
    int n = d.substring(6).toInt();
    if (n>=1 && n<=4) { runS[n-1].running = false; cascade_start(runS[n-1], 80); Serial.printf("OK s_run_%d\n", n); }
    else Serial.println("BAD s_run_n");
  }
  else if (d.startsWith("s_stop_")) {
    int n = d.substring(7).toInt();
    if (n>=1 && n<=4) { cascade_stop(runS[n-1]); Serial.printf("OK s_stop_%d\n", n); }
    else Serial.println("BAD s_stop_n");
  }

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
    tick_run_yarn(runY1, YARN1_ADDR, 8);
    tick_run_yarn(runY2, YARN2_ADDR, 8);
    for (uint8_t s = 0; s < 4; s++) tick_run_stitch(s);

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
    if (d.startsWith("servo_") || d.startsWith("step_")) {

      // SERVO
      if (d == "servo_son_on")       servo_son(true);
      else if (d == "servo_son_off") servo_son(false);
      else if (d == "servo_run_on")  servo_run(true);
      else if (d == "servo_run_off") servo_run(false);
      else if (d == "servo_dir_0")   digitalWrite(SERVO_DIR, LOW);
      else if (d == "servo_dir_1")   digitalWrite(SERVO_DIR, HIGH);

      // STEPPER
      else if (d == "step_run_on")   step_start();
      else if (d == "step_run_off")  step_stop();
      else if (d == "step_dir_0")    step_set_dir_safe(0);
      else if (d == "step_dir_1")    step_set_dir_safe(1);
      else if (d == "step_rev")      step_rev_start();

      addCors();
      server.send(200, "text/plain", "OK");
      return;
    }

    // CAN: por cola
    bool okq = enqueueDo(d.c_str());
    addCors();
    server.send(okq ? 200 : 500, "text/plain", okq ? "QUEUED" : "QUEUE FAIL");
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

    if (server.hasArg("servo_hz"))   servo_set_hz((uint32_t)server.arg("servo_hz").toInt());
    if (server.hasArg("servo_duty")) servo_set_duty_pct((uint8_t)server.arg("servo_duty").toInt());
    if (server.hasArg("step_hz"))    step_apply_from_freq((uint32_t)server.arg("step_hz").toInt());

    server.send(200, "text/plain", "OK");
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
    s += ",\"servo_son\":" + String(servo_son_on ? "true":"false");
    s += ",\"servo_run\":" + String(servo_running ? "true":"false");
    s += ",\"servo_hz\":" + String((unsigned long)servo_hz);
    s += ",\"servo_hz_real\":" + String((double)ledcReadFreq(SERVO_PULSE), 1);
    s += ",\"servo_duty_pct\":" + String((unsigned)((uint32_t)servo_duty * 100u / ((1u<<SERVO_LEDC_RES)-1u)));
    s += ",\"servo_dir\":" + String(digitalRead(SERVO_DIR) ? 1:0);

    s += ",\"step_run\":" + String(step_running ? "true":"false");
    s += ",\"step_hz\":" + String((unsigned long)step_hz);
    s += ",\"step_dir\":" + String(digitalRead(STEP_PIN_DIR) ? 1:0);
    s += ",\"step_rev_busy\":" + String(stepRev.active ? "true":"false");

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

  Serial.println("\n--- FW UNIFICADO: CAN + SERVO + STEPPER + WiFi AP + SPIFFS ---");
  Serial.println("Web: WiFi ESP32_TEST (12345678)");
  Serial.println("UI1(CAN):   http://192.168.4.1/");
  Serial.println("UI2(SERVO): http://192.168.4.1/b");
  Serial.println("API: /cmd?do=...   /send?line=...   /set?...   /status   /rx   /fs");

  mount_spiffs();

  // Cola para Core1
  cmdQ = xQueueCreate(32, sizeof(CmdMsg));

  // init pub idle
  init_pub_finish(I_IDLE, "IDLE", "IDLE");

  // CAN start
  can_start();
  build_my_animation_script();

  // SERVO init
  servo_pwm_init();
  servo_set_hz(1000);
  servo_set_duty_pct(50);
  servo_son(false);
  servo_run(false);

  // STEPPER init
  pinMode(STEP_PIN_DIR, OUTPUT);
  digitalWrite(STEP_PIN_DIR, LOW);

  gpio_reset_pin((gpio_num_t)STEP_PIN_STEP);
  gpio_set_direction((gpio_num_t)STEP_PIN_STEP, GPIO_MODE_OUTPUT);
  gpio_set_level((gpio_num_t)STEP_PIN_STEP, 0);

  esp_timer_create_args_t args = {};
  args.callback = &step_timer_cb;
  args.arg = nullptr;
  args.name = "step_timer";

  if (esp_timer_create(&args, &step_timer) != ESP_OK) {
    Serial.println("ERROR: no pude crear esp_timer (stepper)");
  }
  step_apply_from_freq(200);
  step_stop();

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
  step_rev_tick();  // REV suave
  delay(1);
}
