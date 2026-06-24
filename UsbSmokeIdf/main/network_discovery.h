#pragma once

#include <stdbool.h>

#include "esp_err.h"

// [ACURATEX] Puerto UDP fijo donde la app busca equipos Acuratex en la red local.
// No es el puerto TCP de comandos; solo sirve para discovery.
#define APP_DISCOVERY_UDP_PORT 3334

// [ACURATEX] Informacion que discovery anuncia a la app para que luego abra TCP.
typedef struct
{
    // [C/C++] Estos campos son punteros a strings; start() copia su contenido a
    // buffers internos antes de crear la tarea UDP.
    const char *hostname;
    const char *ip;
    const char *ssid;
    // [ACURATEX] Puerto TCP configurado desde wifi.txt.
    int tcp_port;
} app_discovery_info_t;

/**
 * [POR QUE EXISTE]
 * Expone el arranque del servicio UDP que responde ACURATEX_DISCOVER.
 *
 * [QUIEN LA LLAMA]
 * app_main, cuando WiFi ya obtuvo IP y el servidor TCP esta activo.
 *
 * [CUANDO SE EJECUTA]
 * Sin USB activo, despues de iniciar TCP.
 *
 * [ENTRADAS]
 * Recibe hostname/IP/SSID/puerto TCP a publicar.
 *
 * [SALIDAS]
 * Devuelve esp_err_t para indicar ESP_OK o causa de fallo.
 *
 * [ESTADO QUE MODIFICA]
 * Inicia una tarea FreeRTOS y guarda una copia de la informacion anunciada.
 *
 * [CONCURRENCIA]
 * La tarea UDP corre en paralelo con app_main y tcp_server.
 *
 * [FLUJO ACURATEX]
 * WiFi -> TCP -> discovery -> app localiza el equipo.
 *
 * [EQUIVALENCIA MCU]
 * Es habilitar un servicio de anuncio automatico en red.
 *
 * [SI NO EXISTIERA]
 * No habria descubrimiento automatico del firmware por UDP.
 */
esp_err_t app_network_discovery_start(const app_discovery_info_t *info);
/**
 * [POR QUE EXISTE]
 * Detiene el servicio UDP cuando USB o la perdida de red lo requieren.
 *
 * [QUIEN LA LLAMA]
 * app_main y el propio start() si necesita reiniciar discovery.
 *
 * [CUANDO SE EJECUTA]
 * Al montar USB, perder TCP/WiFi o cambiar datos anunciados.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Cierra socket UDP y espera la salida de la tarea.
 *
 * [CONCURRENCIA]
 * Coordina con la tarea discovery mediante bandera y cierre de socket.
 *
 * [FLUJO ACURATEX]
 * Red deja de estar disponible -> discovery se apaga.
 *
 * [EQUIVALENCIA MCU]
 * Es apagar un canal auxiliar de comunicaciones.
 *
 * [SI NO EXISTIERA]
 * La app podria seguir viendo un servicio TCP inexistente.
 */
void app_network_discovery_stop(void);
/**
 * [POR QUE EXISTE]
 * Permite consultar si discovery ya esta corriendo.
 *
 * [QUIEN LA LLAMA]
 * app_main antes de iniciar o detener servicios de red.
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
 * Lee estado actualizado por la tarea UDP.
 *
 * [FLUJO ACURATEX]
 * app_main -> comprobar discovery -> evitar duplicados.
 *
 * [EQUIVALENCIA MCU]
 * Es una bandera de servicio activo.
 *
 * [SI NO EXISTIERA]
 * app_main no podria saber si debe crear la tarea UDP.
 */
bool app_network_discovery_is_running(void);
