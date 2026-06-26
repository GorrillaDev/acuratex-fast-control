#pragma once

#include <stdbool.h>
#include <stddef.h>

#include "esp_err.h"

// [ACURATEX] Limites de SSID/PASS usados al leer wifi.txt.
#define APP_WIFI_MAX_SSID_LEN 32
#define APP_WIFI_MAX_PASS_LEN 63

// [ACURATEX] Configuracion minima que permite abrir la ruta app -> WiFi -> TCP.
// Se carga desde /fs/wifi.txt con claves SSID, PASS y PORT.
typedef struct
{
    // [ACURATEX] Nombre de la red WiFi donde el ESP32-S3 se conecta como STA.
    char ssid[APP_WIFI_MAX_SSID_LEN + 1];
    // [ACURATEX] Password de la red. Se evita imprimirlo en logs.
    char pass[APP_WIFI_MAX_PASS_LEN + 1];
    // [ACURATEX] Puerto TCP donde la app enviara comandos cuando no use USB.
    int port;
} app_wifi_settings_t;

/**
 * [POR QUE EXISTE]
 * Inicializa NVS, esp_netif, event loop y driver WiFi en modo estacion.
 *
 * [QUIEN LA LLAMA]
 * app_wifi_manager_start().
 *
 * [CUANDO SE EJECUTA]
 * La primera vez que se intenta habilitar red desde app_main.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve esp_err_t.
 *
 * [ESTADO QUE MODIFICA]
 * Crea netif STA, registra callbacks y genera hostname.
 *
 * [CONCURRENCIA]
 * Desde aqui quedan registrados callbacks que luego corren en el event loop.
 *
 * [FLUJO ACURATEX]
 * wifi.txt valido -> init WiFi -> driver listo para conectar.
 *
 * [EQUIVALENCIA MCU]
 * Es la parte de ESP-IDF que Arduino oculta antes de WiFi.begin().
 *
 * [SI NO EXISTIERA]
 * No existiria interfaz WiFi para abrir TCP.
 */
esp_err_t app_wifi_manager_init(void);

/**
 * [POR QUE EXISTE]
 * Lee /fs/wifi.txt y valida SSID/PASS/PORT.
 *
 * [QUIEN LA LLAMA]
 * app_main, a traves de app_poll_network_without_usb().
 *
 * [CUANDO SE EJECUTA]
 * Cuando USB no esta montado y WiFi aun no arranco.
 *
 * [ENTRADAS]
 * Recibe estructura de salida y buffer opcional para razon.
 *
 * [SALIDAS]
 * Devuelve true si hay configuracion completa.
 *
 * [ESTADO QUE MODIFICA]
 * Llena settings y reason; no arranca WiFi por si misma.
 *
 * [CONCURRENCIA]
 * Lee LittleFS mediante stdio desde app_main.
 *
 * [FLUJO ACURATEX]
 * LittleFS wifi.txt -> settings -> app_wifi_manager_start().
 *
 * [EQUIVALENCIA MCU]
 * Es cargar parametros de red desde memoria no volatil antes de conectar.
 *
 * [SI NO EXISTIERA]
 * SSID/PASS/PORT no serian configurables por archivo.
 */
bool app_wifi_manager_load_settings(app_wifi_settings_t *settings,
                                    char *reason,
                                    size_t reason_size);

bool app_wifi_manager_save_settings(const app_wifi_settings_t *settings,
                                    char *reason,
                                    size_t reason_size);

/**
 * [POR QUE EXISTE]
 * Aplica SSID/PASS y arranca WiFi en modo STA solamente.
 *
 * [QUIEN LA LLAMA]
 * app_poll_network_without_usb().
 *
 * [CUANDO SE EJECUTA]
 * Despues de validar wifi.txt y mientras USB no esta activo.
 *
 * [ENTRADAS]
 * Recibe app_wifi_settings_t ya validado.
 *
 * [SALIDAS]
 * Devuelve esp_err_t.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza estado global de conexion y habilita reconexion.
 *
 * [CONCURRENCIA]
 * El driver generara eventos asincronos WIFI_EVENT/IP_EVENT.
 *
 * [FLUJO ACURATEX]
 * settings -> esp_wifi_start -> evento IP -> TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es comparable a WiFi.begin(ssid, pass), pero con pasos ESP-IDF explicitos.
 *
 * [SI NO EXISTIERA]
 * La configuracion no llegaria al driver WiFi.
 */
esp_err_t app_wifi_manager_start(const app_wifi_settings_t *settings);
/**
 * [POR QUE EXISTE]
 * Detiene WiFi STA cuando USB toma prioridad o se reinicia la ruta de red.
 *
 * [QUIEN LA LLAMA]
 * app_stop_network_for_usb() y flujos de reinicio de WiFi.
 *
 * [CUANDO SE EJECUTA]
 * Al montar USB o cambiar configuracion.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Deshabilita reconexion, limpia conectado/IP y marca WiFi como detenido.
 *
 * [CONCURRENCIA]
 * Puede coincidir con eventos WiFi; la bandera de reconexion evita reconectar.
 *
 * [FLUJO ACURATEX]
 * USB activo -> detener discovery/TCP -> detener WiFi.
 *
 * [EQUIVALENCIA MCU]
 * Es similar a WiFi.disconnect() y apagar el modulo.
 *
 * [SI NO EXISTIERA]
 * WiFi podria seguir activo mientras USB controla el equipo.
 */
void app_wifi_manager_stop(void);

// [ACURATEX] Consultas usadas por app_main y por el comando status.
/**
 * [POR QUE EXISTE]
 * Informa si el driver WiFi fue arrancado por el manager.
 *
 * [QUIEN LA LLAMA]
 * app_main al decidir si debe iniciar o detener red.
 *
 * [CUANDO SE EJECUTA]
 * En el bucle principal.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve bool.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee una bandera global escrita por start/stop.
 *
 * [FLUJO ACURATEX]
 * app_main -> consultar WiFi iniciado -> habilitar o parar servicios.
 *
 * [EQUIVALENCIA MCU]
 * Es una bandera de "modulo WiFi encendido".
 *
 * [SI NO EXISTIERA]
 * app_main no sabria si debe arrancar o apagar WiFi.
 */
bool app_wifi_manager_is_started(void);
/**
 * [POR QUE EXISTE]
 * Informa si WiFi STA tiene conexion/IP.
 *
 * [QUIEN LA LLAMA]
 * app_main, status y discovery indirectamente.
 *
 * [CUANDO SE EJECUTA]
 * Antes de arrancar TCP o reportar estado de red.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve bool.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee bandera actualizada por el callback IP_EVENT_STA_GOT_IP.
 *
 * [FLUJO ACURATEX]
 * WiFi conectado -> TCP puede arrancar.
 *
 * [EQUIVALENCIA MCU]
 * Es similar a consultar WiFi.status().
 *
 * [SI NO EXISTIERA]
 * TCP podria intentarse antes de tener IP.
 */
bool app_wifi_manager_is_connected(void);

/**
 * [POR QUE EXISTE]
 * Expone la IP actual para status y discovery UDP.
 *
 * [QUIEN LA LLAMA]
 * app_main y command_processor por medio del ambiente de comandos.
 *
 * [CUANDO SE EJECUTA]
 * Al publicar estado o anunciar discovery.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve const char* al buffer interno.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * El buffer puede actualizarse desde eventos WiFi; los lectores solo lo consultan.
 *
 * [FLUJO ACURATEX]
 * DHCP/IP_EVENT -> s_ip -> status/discovery.
 *
 * [EQUIVALENCIA MCU]
 * Es equivalente a exponer WiFi.localIP() como texto.
 *
 * [SI NO EXISTIERA]
 * La app no podria ver la IP reportada por el firmware.
 */
const char *app_wifi_manager_get_ip(void);
/**
 * [POR QUE EXISTE]
 * Expone el hostname usado por DHCP/discovery.
 *
 * [QUIEN LA LLAMA]
 * app_main al llenar app_discovery_info_t.
 *
 * [CUANDO SE EJECUTA]
 * Al iniciar discovery UDP.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve const char* al buffer interno.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * El hostname se genera durante init y luego se lee.
 *
 * [FLUJO ACURATEX]
 * MAC -> hostname -> discovery UDP.
 *
 * [EQUIVALENCIA MCU]
 * Es el nombre visible del dispositivo en la red.
 *
 * [SI NO EXISTIERA]
 * Discovery tendria menos datos para identificar el equipo.
 */
const char *app_wifi_manager_get_hostname(void);
/**
 * [POR QUE EXISTE]
 * Expone el SSID configurado sin revelar PASS.
 *
 * [QUIEN LA LLAMA]
 * app_main y command_processor para status/discovery.
 *
 * [CUANDO SE EJECUTA]
 * Al publicar estado de red.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve const char* al SSID o string vacio.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee s_settings, cargado por start().
 *
 * [FLUJO ACURATEX]
 * wifi.txt SSID -> status/discovery.
 *
 * [EQUIVALENCIA MCU]
 * Es consultar el nombre de red configurado.
 *
 * [SI NO EXISTIERA]
 * La app no podria mostrar que SSID usa el firmware.
 */
const char *app_wifi_manager_get_ssid(void);
/**
 * [POR QUE EXISTE]
 * Expone el puerto TCP cargado desde wifi.txt.
 *
 * [QUIEN LA LLAMA]
 * app_main, status y discovery.
 *
 * [CUANDO SE EJECUTA]
 * Antes de arrancar TCP y al anunciar discovery.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve puerto positivo o 0 si no hay configuracion valida.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee s_settings.port.
 *
 * [FLUJO ACURATEX]
 * wifi.txt PORT -> TCP server -> discovery anuncia tcp_port.
 *
 * [EQUIVALENCIA MCU]
 * Es exponer una constante de configuracion cargada desde archivo.
 *
 * [SI NO EXISTIERA]
 * app_main no sabria que puerto TCP abrir.
 */
int app_wifi_manager_get_port(void);
