#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#include "esp_event.h"
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_netif.h"
#include "esp_wifi.h"
#include "nvs_flash.h"

#include "line_codec.h"
#include "wifi_manager.h"

// [ACURATEX] Ruta LittleFS desde donde se lee la configuracion de red.
#define APP_WIFI_FILE_PATH "/fs/wifi.txt"
// [ACURATEX] IP mostrada cuando todavia no hay conexion STA con DHCP.
#define APP_WIFI_DEFAULT_IP "0.0.0.0"

// [ESP-IDF] TAG usado por los macros ESP_LOG* de este modulo.
static const char *TAG = "wifi_manager";

// [ACURATEX] Estado global del manager WiFi. Se guarda aqui para que app_main,
// status y TCP consulten si la ruta de red esta disponible.
static bool s_initialized = false;
static bool s_started = false;
static bool s_reconnect_enabled = false;
static bool s_connected = false;
// [ESP-IDF] esp_netif_t representa la interfaz de red STA creada por ESP-IDF.
static esp_netif_t *s_sta_netif = NULL;
static app_wifi_settings_t s_settings = {};
static char s_ip[16] = APP_WIFI_DEFAULT_IP;
static char s_hostname[32] = "acuratex";

/**
 * [POR QUE EXISTE]
 * Esta funcion escribe una razon textual de error o estado en un buffer
 * opcional.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_copy_value(), app_parse_port() y
 * app_wifi_manager_load_settings().
 *
 * [CUANDO SE EJECUTA]
 * Al validar `wifi.txt` o reportar por que no puede usarse.
 *
 * [ENTRADAS]
 * Recibe buffer, tamano y mensaje literal.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Modifica solo el buffer `reason` si existe y tiene tamano.
 *
 * [CONCURRENCIA]
 * No usa estado global ni bloquea.
 *
 * [FLUJO ACURATEX]
 * app_main -> load_settings -> reason -> log "WiFi inactivo".
 *
 * [EQUIVALENCIA MCU]
 * Es como llenar un buffer de diagnostico para mostrar por Serial.
 *
 * [SI NO EXISTIERA]
 * Las fallas de wifi.txt tendrian menos detalle para diagnostico.
 */
static void app_set_reason(char *reason, size_t reason_size, const char *message)
{
    if (reason != NULL && reason_size > 0)
    {
        snprintf(reason, reason_size, "%s", message);
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion copia y valida campos de texto de `wifi.txt`, como SSID y PASS.
 *
 * [QUIEN LA LLAMA]
 * La llama app_wifi_manager_load_settings().
 *
 * [CUANDO SE EJECUTA]
 * Al encontrar claves SSID o PASS en el archivo de configuracion.
 *
 * [ENTRADAS]
 * Recibe destino, tamano, valor leido, nombre de campo y buffer de razon.
 *
 * [SALIDAS]
 * Devuelve true si el valor existe, no esta vacio y cabe en destino.
 *
 * [ESTADO QUE MODIFICA]
 * Copia en `dest` y puede escribir `reason`.
 *
 * [CONCURRENCIA]
 * No usa estado global.
 *
 * [FLUJO ACURATEX]
 * wifi.txt -> valor SSID/PASS -> app_wifi_settings_t -> esp_wifi_set_config.
 *
 * [EQUIVALENCIA MCU]
 * Es validacion de tamano antes de copiar a un buffer fijo.
 *
 * [SI NO EXISTIERA]
 * Un campo vacio o demasiado largo podria entrar a la configuracion WiFi.
 */
static bool app_copy_value(char *dest,
                           size_t dest_size,
                           const char *value,
                           const char *field,
                           char *reason,
                           size_t reason_size)
{
    size_t len;

    if (dest == NULL || value == NULL || field == NULL)
    {
        app_set_reason(reason, reason_size, "campo invalido");
        return false;
    }

    len = strlen(value);
    if (len == 0)
    {
        char msg[64];
        snprintf(msg, sizeof(msg), "%s vacio", field);
        app_set_reason(reason, reason_size, msg);
        return false;
    }

    if (len >= dest_size)
    {
        char msg[64];
        snprintf(msg, sizeof(msg), "%s demasiado largo", field);
        app_set_reason(reason, reason_size, msg);
        return false;
    }

    // [C/C++] strlcpy limita la copia y termina el string en cero.
    strlcpy(dest, value, dest_size);
    return true;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion convierte el campo PORT de `wifi.txt` a entero TCP valido.
 *
 * [QUIEN LA LLAMA]
 * La llama app_wifi_manager_load_settings().
 *
 * [CUANDO SE EJECUTA]
 * Al encontrar la clave PORT en wifi.txt.
 *
 * [ENTRADAS]
 * Recibe valor textual, puntero de salida y buffer de razon.
 *
 * [SALIDAS]
 * Devuelve true si el puerto esta entre 1 y 65535.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe `*port` y posiblemente `reason`.
 *
 * [CONCURRENCIA]
 * No comparte estado.
 *
 * [FLUJO ACURATEX]
 * wifi.txt PORT -> app_wifi_settings_t.port -> app_tcp_server_start(port).
 *
 * [EQUIVALENCIA MCU]
 * Es convertir ASCII decimal a un registro/configuracion numerica.
 *
 * [SI NO EXISTIERA]
 * El servidor TCP podria recibir un puerto invalido.
 */
static bool app_parse_port(const char *value, int *port, char *reason, size_t reason_size)
{
    char *endptr = NULL;
    long parsed;

    if (value == NULL || port == NULL || value[0] == '\0')
    {
        app_set_reason(reason, reason_size, "PORT vacio");
        return false;
    }

    errno = 0;
    // [C/C++] strtol permite detectar errores con errno y endptr.
    parsed = strtol(value, &endptr, 10);
    if (errno != 0 || endptr == value || *endptr != '\0' || parsed < 1 || parsed > 65535)
    {
        app_set_reason(reason, reason_size, "PORT invalido");
        return false;
    }

    *port = (int)parsed;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion es el callback de eventos WiFi/IP de ESP-IDF. Mantiene el estado
 * local actualizado y dispara reconexiones.
 *
 * [QUIEN LA LLAMA]
 * La llama el event loop de ESP-IDF despues de registrarla con
 * esp_event_handler_instance_register().
 *
 * [CUANDO SE EJECUTA]
 * Ante WIFI_EVENT_STA_START, WIFI_EVENT_STA_DISCONNECTED e IP_EVENT_STA_GOT_IP.
 *
 * [ENTRADAS]
 * Recibe base de evento, id de evento y datos especificos del evento.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_connected, s_ip y usa s_settings/s_reconnect_enabled.
 *
 * [CONCURRENCIA]
 * Corre en contexto del loop de eventos de ESP-IDF, concurrente con app_main.
 * No usa mutex; el estado es simple y consultado como flags/strings.
 *
 * [FLUJO ACURATEX]
 * WiFi driver -> evento -> estado conectado/IP -> app_main puede abrir TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es parecido a un callback de interrupcion/controlador que avisa conexion o
 * desconexion de red.
 *
 * [SI NO EXISTIERA]
 * app_main no sabria cuando WiFi obtuvo IP ni se reintentaria conexion.
 */
static void app_wifi_event_handler(void *arg,
                                   esp_event_base_t event_base,
                                   int32_t event_id,
                                   void *event_data)
{
    (void)arg;

    if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_START)
    {
        if (s_reconnect_enabled)
        {
            ESP_LOGI(TAG, "Estado WiFi: conectando");
            ESP_LOGI(TAG, "Intentando conectar por STA");
            ESP_LOGI(TAG, "SSID: %s", s_settings.ssid);
            ESP_LOGI(TAG, "PASS: <redacted>");
            ESP_LOGI(TAG, "Puerto TCP configurado: %d", s_settings.port);
            // [ESP-IDF] esp_wifi_connect() inicia la conexion STA con la
            // configuracion cargada por esp_wifi_set_config().
            esp_wifi_connect();
        }
        return;
    }

    if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_DISCONNECTED)
    {
        // [ESP-IDF] event_data para desconexion apunta a
        // wifi_event_sta_disconnected_t.
        wifi_event_sta_disconnected_t *event = (wifi_event_sta_disconnected_t *)event_data;

        s_connected = false;
        strlcpy(s_ip, APP_WIFI_DEFAULT_IP, sizeof(s_ip));

        if (s_reconnect_enabled)
        {
            ESP_LOGW(TAG,
                     "Estado WiFi: desconectado reason=%d, reintentando SSID=%s PASS=<redacted>",
                     event != NULL ? event->reason : -1,
                     s_settings.ssid);
            // [ACURATEX] Mientras reconnect este habilitado, la ruta de red se
            // intenta recuperar automaticamente.
            esp_wifi_connect();
        }
        else
        {
            ESP_LOGI(TAG, "Estado WiFi: detenido");
        }
        return;
    }

    if (event_base == IP_EVENT && event_id == IP_EVENT_STA_GOT_IP)
    {
        // [ESP-IDF] Al obtener IP, event_data trae ip_event_got_ip_t.
        ip_event_got_ip_t *event = (ip_event_got_ip_t *)event_data;

        if (event != NULL)
        {
            // [ESP-IDF] esp_ip4addr_ntoa convierte direccion IPv4 a texto.
            esp_ip4addr_ntoa(&event->ip_info.ip, s_ip, sizeof(s_ip));
        }
        else
        {
            strlcpy(s_ip, APP_WIFI_DEFAULT_IP, sizeof(s_ip));
        }

        s_connected = true;
        ESP_LOGI(TAG, "Estado WiFi: conectado");
        ESP_LOGI(TAG, "IP obtenida: %s", s_ip);
        ESP_LOGI(TAG, "Puerto TCP listo: %d", s_settings.port);
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion genera un hostname reconocible para el ESP32-S3 en la red.
 *
 * [QUIEN LA LLAMA]
 * La llama app_wifi_manager_init().
 *
 * [CUANDO SE EJECUTA]
 * Una vez durante la inicializacion del manager WiFi.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe s_hostname.
 *
 * [CONCURRENCIA]
 * No bloquea de forma significativa; consulta MAC con ESP-IDF.
 *
 * [FLUJO ACURATEX]
 * init WiFi -> hostname -> discovery/status pueden mostrar nombre.
 *
 * [EQUIVALENCIA MCU]
 * Es como formar un nombre de dispositivo usando los ultimos bytes de MAC.
 *
 * [SI NO EXISTIERA]
 * El equipo tendria un hostname generico menos util para identificarlo.
 */
static void app_wifi_generate_hostname(void)
{
    uint8_t mac[6] = {0};

    // [ESP-IDF] esp_read_mac lee la MAC asociada a la interfaz WiFi STA.
    if (esp_read_mac(mac, ESP_MAC_WIFI_STA) == ESP_OK)
    {
        snprintf(s_hostname,
                 sizeof(s_hostname),
                 "Acuratex-Wireless-%02x%02x%02x",
                 mac[3],
                 mac[4],
                 mac[5]);
    }
    else
    {
        strlcpy(s_hostname, "acuratex-esp32s3", sizeof(s_hostname));
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion inicializa los servicios ESP-IDF necesarios para usar WiFi STA:
 * NVS, netif, event loop, interfaz STA, driver y callbacks.
 *
 * [QUIEN LA LLAMA]
 * La llama app_wifi_manager_start() antes de iniciar WiFi.
 *
 * [CUANDO SE EJECUTA]
 * La primera vez que app_main habilita red porque no hay USB activo y existe
 * wifi.txt valido.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve ESP_OK o el error ESP-IDF que impide inicializar WiFi.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_initialized, s_sta_netif, s_hostname y s_ip.
 *
 * [CONCURRENCIA]
 * Registra callbacks que luego ejecutara el event loop de ESP-IDF.
 *
 * [FLUJO ACURATEX]
 * app_main sin USB -> load wifi.txt -> app_wifi_manager_start ->
 * app_wifi_manager_init -> driver WiFi listo.
 *
 * [EQUIVALENCIA MCU]
 * Es lo que Arduino oculta dentro de WiFi.begin(), pero separado en pasos.
 *
 * [SI NO EXISTIERA]
 * No habria pila de red inicializada para abrir TCP.
 */
esp_err_t app_wifi_manager_init(void)
{
    esp_err_t err;

    if (s_initialized)
    {
        return ESP_OK;
    }

    // [ESP-IDF] NVS guarda datos internos del WiFi. Si la particion esta llena
    // o tiene version incompatible, se borra y se inicializa otra vez.
    err = nvs_flash_init();
    if (err == ESP_ERR_NVS_NO_FREE_PAGES || err == ESP_ERR_NVS_NEW_VERSION_FOUND)
    {
        ESP_ERROR_CHECK(nvs_flash_erase());
        err = nvs_flash_init();
    }
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "nvs_flash_init fallo: %s", esp_err_to_name(err));
        return err;
    }

    // [ESP-IDF] esp_netif_init prepara la capa de red comun usada por WiFi/LwIP.
    err = esp_netif_init();
    if (err != ESP_OK && err != ESP_ERR_INVALID_STATE)
    {
        ESP_LOGE(TAG, "esp_netif_init fallo: %s", esp_err_to_name(err));
        return err;
    }

    // [ESP-IDF] El event loop por defecto entrega eventos WIFI_EVENT/IP_EVENT a
    // callbacks registrados.
    err = esp_event_loop_create_default();
    if (err != ESP_OK && err != ESP_ERR_INVALID_STATE)
    {
        ESP_LOGE(TAG, "esp_event_loop_create_default fallo: %s", esp_err_to_name(err));
        return err;
    }

    // [ESP-IDF] Crea interfaz de red WiFi en modo estacion.
    s_sta_netif = esp_netif_create_default_wifi_sta();
    if (s_sta_netif == NULL)
    {
        ESP_LOGE(TAG, "No se pudo crear netif STA");
        return ESP_FAIL;
    }

    app_wifi_generate_hostname();
    // [ESP-IDF] Hostname visible para DHCP/red local.
    err = esp_netif_set_hostname(s_sta_netif, s_hostname);
    if (err != ESP_OK)
    {
        ESP_LOGW(TAG, "No se pudo fijar hostname %s: %s", s_hostname, esp_err_to_name(err));
    }
    else
    {
        ESP_LOGI(TAG, "Hostname WiFi: %s", s_hostname);
    }

    // [ESP-IDF] WIFI_INIT_CONFIG_DEFAULT() llena parametros recomendados del
    // driver WiFi.
    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    err = esp_wifi_init(&cfg);
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "esp_wifi_init fallo: %s", esp_err_to_name(err));
        return err;
    }

    // [ESP-IDF] Registra un mismo callback para todos los eventos WiFi.
    err = esp_event_handler_instance_register(WIFI_EVENT,
                                              ESP_EVENT_ANY_ID,
                                              app_wifi_event_handler,
                                              NULL,
                                              NULL);
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "handler WIFI_EVENT fallo: %s", esp_err_to_name(err));
        return err;
    }

    // [ESP-IDF] Registra el callback para el evento especifico de IP obtenida.
    err = esp_event_handler_instance_register(IP_EVENT,
                                              IP_EVENT_STA_GOT_IP,
                                              app_wifi_event_handler,
                                              NULL,
                                              NULL);
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "handler IP_EVENT fallo: %s", esp_err_to_name(err));
        return err;
    }

    s_initialized = true;
    strlcpy(s_ip, APP_WIFI_DEFAULT_IP, sizeof(s_ip));
    ESP_LOGI(TAG, "WiFi manager listo en modo STA solamente");
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion lee `/fs/wifi.txt` desde LittleFS y valida SSID, PASS y PORT.
 *
 * [QUIEN LA LLAMA]
 * La llama app_poll_network_without_usb() desde app_main cuando USB no esta
 * activo y toca revisar configuracion.
 *
 * [CUANDO SE EJECUTA]
 * Periodicamente mientras no haya USB y WiFi no este iniciado.
 *
 * [ENTRADAS]
 * Recibe puntero de salida `settings` y buffer opcional `reason`.
 *
 * [SALIDAS]
 * Devuelve true si el archivo existe y contiene SSID/PASS/PORT validos.
 *
 * [ESTADO QUE MODIFICA]
 * Llena `*settings` y `reason`; no modifica el estado global del manager.
 *
 * [CONCURRENCIA]
 * Usa stdio sobre LittleFS. No usa mutex propio.
 *
 * [FLUJO ACURATEX]
 * Sin USB -> leer wifi.txt -> configurar WiFi -> habilitar TCP para app.
 *
 * [EQUIVALENCIA MCU]
 * Es como leer configuracion de una EEPROM/FS antes de llamar WiFi.begin().
 *
 * [SI NO EXISTIERA]
 * La ruta TCP no tendria SSID, password ni puerto configurables por archivo.
 */
bool app_wifi_manager_load_settings(app_wifi_settings_t *settings,
                                    char *reason,
                                    size_t reason_size)
{
    FILE *fp;
    char line[160];
    bool has_ssid = false;
    bool has_pass = false;
    bool has_port = false;
    app_wifi_settings_t parsed = {};

    if (settings == NULL)
    {
        app_set_reason(reason, reason_size, "settings NULL");
        return false;
    }

    // [LITTLEFS] /fs es el punto de montaje usado por el firmware para archivos
    // de configuracion y programas.
    fp = fopen(APP_WIFI_FILE_PATH, "rb");
    if (fp == NULL)
    {
        app_set_reason(reason, reason_size, "wifi.txt no existe");
        return false;
    }

    while (fgets(line, sizeof(line), fp) != NULL)
    {
        char *eq;
        char *key;
        char *value;

        // [ACURATEX] Se aceptan lineas vacias y comentarios con # o ;.
        app_trim_line(line);
        if (line[0] == '\0' || line[0] == '#' || line[0] == ';')
        {
            continue;
        }

        // [ACURATEX] Formato esperado: CLAVE=valor.
        eq = strchr(line, '=');
        if (eq == NULL)
        {
            fclose(fp);
            app_set_reason(reason, reason_size, "linea sin '=' en wifi.txt");
            return false;
        }

        // [C/C++] Partir la linea reemplaza '=' por terminador nulo y deja dos
        // strings dentro del mismo buffer: key y value.
        *eq = '\0';
        key = line;
        value = eq + 1;
        app_trim_line(key);
        app_trim_line(value);

        if (strcasecmp(key, "SSID") == 0)
        {
            if (!app_copy_value(parsed.ssid, sizeof(parsed.ssid), value, "SSID", reason, reason_size))
            {
                fclose(fp);
                return false;
            }
            has_ssid = true;
        }
        else if (strcasecmp(key, "PASS") == 0)
        {
            if (!app_copy_value(parsed.pass, sizeof(parsed.pass), value, "PASS", reason, reason_size))
            {
                fclose(fp);
                return false;
            }
            has_pass = true;
        }
        else if (strcasecmp(key, "PORT") == 0)
        {
            if (!app_parse_port(value, &parsed.port, reason, reason_size))
            {
                fclose(fp);
                return false;
            }
            has_port = true;
        }
    }

    fclose(fp);

    // [ACURATEX] Los tres campos son obligatorios para habilitar la ruta TCP.
    if (!has_ssid || !has_pass || !has_port)
    {
        app_set_reason(reason, reason_size, "wifi.txt incompleto: requiere SSID, PASS y PORT");
        return false;
    }

    *settings = parsed;
    app_set_reason(reason, reason_size, "wifi.txt valido");
    return true;
}

bool app_wifi_manager_save_settings(const app_wifi_settings_t *settings,
                                    char *reason,
                                    size_t reason_size)
{
    if (settings == NULL) {
        app_set_reason(reason, reason_size, "settings NULL");
        return false;
    }

    if (settings->ssid[0] == '\0' || settings->pass[0] == '\0' || settings->port < 1 || settings->port > 65535) {
        app_set_reason(reason, reason_size, "configuracion invalida");
        return false;
    }

    FILE *fp = fopen(APP_WIFI_FILE_PATH, "wb");
    if (fp == NULL) {
        app_set_reason(reason, reason_size, "no se pudo abrir wifi.txt");
        return false;
    }

    int wrote = fprintf(fp,
                        "SSID=%s\nPASS=%s\nPORT=%d\n",
                        settings->ssid,
                        settings->pass,
                        settings->port);
    if (fclose(fp) != 0 || wrote <= 0) {
        app_set_reason(reason, reason_size, "no se pudo escribir wifi.txt");
        return false;
    }

    s_settings = *settings;
    ESP_LOGI(TAG, "WIFI_CONFIG_SAVE_OK|SSID=%s|PORT=%d", settings->ssid, settings->port);
    app_set_reason(reason, reason_size, "wifi.txt guardado");
    return true;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion aplica la configuracion WiFi y arranca el driver en modo STA.
 *
 * [QUIEN LA LLAMA]
 * La llama app_poll_network_without_usb() desde app_main.
 *
 * [CUANDO SE EJECUTA]
 * Cuando no hay USB, wifi.txt es valido y WiFi aun no esta iniciado.
 *
 * [ENTRADAS]
 * Recibe settings con SSID, PASS y PORT ya validados.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si el driver se inicio o ya estaba iniciado con la misma
 * configuracion.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_settings, s_connected, s_reconnect_enabled, s_ip y s_started.
 *
 * [CONCURRENCIA]
 * Inicia el driver WiFi; despues los eventos llegaran al callback
 * app_wifi_event_handler().
 *
 * [FLUJO ACURATEX]
 * wifi.txt valido -> app_wifi_manager_start -> ESP-IDF WiFi STA -> IP -> TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es equivalente a WiFi.begin(ssid, pass), pero con control explicito de modo,
 * config y start.
 *
 * [SI NO EXISTIERA]
 * La configuracion leida no llegaria al hardware WiFi y no habria TCP.
 */
esp_err_t app_wifi_manager_start(const app_wifi_settings_t *settings)
{
    esp_err_t err;
    // [ESP-IDF] wifi_config_t contiene SSID/password para la interfaz STA.
    wifi_config_t wifi_config = {};

    if (settings == NULL)
    {
        return ESP_ERR_INVALID_ARG;
    }

    err = app_wifi_manager_init();
    if (err != ESP_OK)
    {
        return err;
    }

    if (s_started)
    {
        // [ACURATEX] Si la configuracion es identica, no se reinicia WiFi.
        if (strcmp(s_settings.ssid, settings->ssid) == 0 &&
            strcmp(s_settings.pass, settings->pass) == 0 &&
            s_settings.port == settings->port)
        {
            return ESP_OK;
        }

        app_wifi_manager_stop();
    }

    s_settings = *settings;
    s_connected = false;
    s_reconnect_enabled = true;
    strlcpy(s_ip, APP_WIFI_DEFAULT_IP, sizeof(s_ip));

    // [C/C++] memcpy copia SSID sin agregar terminador manual aqui porque
    // wifi_config_t usa buffer fijo para el driver.
    memcpy(wifi_config.sta.ssid, s_settings.ssid, strlen(s_settings.ssid));
    strlcpy((char *)wifi_config.sta.password, s_settings.pass, sizeof(wifi_config.sta.password));

    ESP_LOGI(TAG, "wifi.txt valido, se habilita WiFi STA");
    ESP_LOGI(TAG, "Intentando conectar por WiFi");
    ESP_LOGI(TAG, "SSID: %s", s_settings.ssid);
    ESP_LOGI(TAG, "PASS: <redacted>");
    ESP_LOGI(TAG, "Puerto TCP: %d", s_settings.port);

    // [ESP-IDF] Este firmware usa modo estacion, no AP.
    err = esp_wifi_set_mode(WIFI_MODE_STA);
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "esp_wifi_set_mode STA fallo: %s", esp_err_to_name(err));
        s_reconnect_enabled = false;
        return err;
    }

    // [ESP-IDF] Se entrega SSID/PASS al driver WiFi para WIFI_IF_STA.
    err = esp_wifi_set_config(WIFI_IF_STA, &wifi_config);
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "esp_wifi_set_config fallo: %s", esp_err_to_name(err));
        s_reconnect_enabled = false;
        return err;
    }

    // [ESP-IDF] esp_wifi_start() dispara eventos; al recibir STA_START el
    // callback llama esp_wifi_connect().
    err = esp_wifi_start();
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "esp_wifi_start fallo: %s", esp_err_to_name(err));
        s_reconnect_enabled = false;
        return err;
    }

    s_started = true;
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion apaga WiFi STA cuando USB esta activo o se necesita reiniciar la
 * ruta de red.
 *
 * [QUIEN LA LLAMA]
 * La llama app_stop_network_for_usb() y otros flujos de control de red.
 *
 * [CUANDO SE EJECUTA]
 * Al montar USB o al cambiar configuracion.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Deshabilita reconexion, limpia conectado/IP y actualiza s_started.
 *
 * [CONCURRENCIA]
 * Puede provocar eventos WiFi posteriores; s_reconnect_enabled evita reconectar.
 *
 * [FLUJO ACURATEX]
 * USB activo -> parar TCP/discovery -> app_wifi_manager_stop.
 *
 * [EQUIVALENCIA MCU]
 * Es como llamar WiFi.disconnect() y apagar el modulo para entregar prioridad a
 * otro transporte.
 *
 * [SI NO EXISTIERA]
 * WiFi podria seguir intentando conectar mientras USB tiene prioridad.
 */
void app_wifi_manager_stop(void)
{
    if (!s_initialized)
    {
        return;
    }

    s_reconnect_enabled = false;
    s_connected = false;
    strlcpy(s_ip, APP_WIFI_DEFAULT_IP, sizeof(s_ip));

    if (s_started)
    {
        ESP_LOGI(TAG, "USB activo: deteniendo WiFi STA");
        // [ESP-IDF] disconnect corta asociacion; stop apaga el driver WiFi.
        esp_wifi_disconnect();
        esp_wifi_stop();
        s_started = false;
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion informa si el driver WiFi fue arrancado por el manager.
 *
 * [QUIEN LA LLAMA]
 * La llama app_poll_network_without_usb() y app_stop_network_for_usb().
 *
 * [CUANDO SE EJECUTA]
 * En el bucle principal de app_main.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve s_started.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee una bandera global actualizada por start/stop.
 *
 * [FLUJO ACURATEX]
 * app_main -> consultar WiFi iniciado -> decidir leer wifi.txt o detener red.
 *
 * [EQUIVALENCIA MCU]
 * Es una bandera de "modulo encendido".
 *
 * [SI NO EXISTIERA]
 * app_main no sabria si debe iniciar o detener WiFi.
 */
bool app_wifi_manager_is_started(void)
{
    return s_started;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion informa si WiFi STA ya obtuvo conexion/IP.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_poll_network_without_usb(), app_fill_command_env() y el comando
 * status indirectamente.
 *
 * [CUANDO SE EJECUTA]
 * En el bucle principal y al preparar respuestas.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve s_connected.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee bandera escrita por el callback de eventos ESP-IDF.
 *
 * [FLUJO ACURATEX]
 * IP_EVENT_STA_GOT_IP -> s_connected true -> app_main puede iniciar TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es consultar si `WiFi.status() == WL_CONNECTED`.
 *
 * [SI NO EXISTIERA]
 * TCP podria intentar arrancar antes de tener IP.
 */
bool app_wifi_manager_is_connected(void)
{
    return s_connected;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion expone la IP actual como texto para status, discovery y logs.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_fill_command_env(), app_poll_network_without_usb() y otros
 * modulos de red.
 *
 * [CUANDO SE EJECUTA]
 * Al construir estado o informacion de discovery.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve puntero a s_ip.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Devuelve puntero a buffer global actualizado por eventos WiFi.
 *
 * [FLUJO ACURATEX]
 * WiFi obtiene IP -> status/discovery muestran IP.
 *
 * [EQUIVALENCIA MCU]
 * Es como devolver WiFi.localIP().toString().
 *
 * [SI NO EXISTIERA]
 * La app no podria conocer la IP desde el firmware.
 */
const char *app_wifi_manager_get_ip(void)
{
    return s_ip;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion expone el hostname generado para discovery/red.
 *
 * [QUIEN LA LLAMA]
 * La llama app_poll_network_without_usb() al armar app_discovery_info_t.
 *
 * [CUANDO SE EJECUTA]
 * Cuando TCP esta activo y se inicia discovery UDP.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve puntero a s_hostname.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Solo lee buffer global generado durante init.
 *
 * [FLUJO ACURATEX]
 * hostname -> discovery UDP -> app identifica el equipo.
 *
 * [EQUIVALENCIA MCU]
 * Es exponer el nombre de dispositivo en red.
 *
 * [SI NO EXISTIERA]
 * Discovery tendria menos informacion del equipo.
 */
const char *app_wifi_manager_get_hostname(void)
{
    return s_hostname;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion expone el SSID activo para status/discovery sin permitir modificar
 * la configuracion interna.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_fill_command_env() y app_poll_network_without_usb().
 *
 * [CUANDO SE EJECUTA]
 * Al preparar estado para la aplicacion.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve SSID o string vacio si aun no hay configuracion.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee s_settings, actualizada al iniciar WiFi.
 *
 * [FLUJO ACURATEX]
 * wifi.txt SSID -> s_settings -> status/discovery.
 *
 * [EQUIVALENCIA MCU]
 * Es devolver el nombre de red configurado.
 *
 * [SI NO EXISTIERA]
 * `status` no podria informar el SSID configurado.
 */
const char *app_wifi_manager_get_ssid(void)
{
    return s_settings.ssid[0] != '\0' ? s_settings.ssid : "";
}

/**
 * [POR QUE EXISTE]
 * Esta funcion expone el puerto TCP configurado.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_fill_command_env() y app_poll_network_without_usb().
 *
 * [CUANDO SE EJECUTA]
 * Al iniciar TCP o responder status.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve el puerto si es positivo, si no 0.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee s_settings.port.
 *
 * [FLUJO ACURATEX]
 * wifi.txt PORT -> app_tcp_server_start(PORT) -> app se conecta por TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es exponer una constante de configuracion cargada desde archivo.
 *
 * [SI NO EXISTIERA]
 * app_main no sabria en que puerto abrir el servidor TCP.
 */
int app_wifi_manager_get_port(void)
{
    return s_settings.port > 0 ? s_settings.port : 0;
}
