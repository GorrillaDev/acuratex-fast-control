#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include "esp_err.h"

// [ACURATEX] Este header define el contrato entre los transportes
// (USB/TCP/UART) y el procesador central de comandos.
// El procesador no escribe directamente en USB ni TCP: recibe callbacks.

// [C/C++] typedef de puntero a funcion. Cualquier transporte que quiera recibir
// respuestas debe proveer una funcion con esta firma.
// [ESP-IDF] Devuelve esp_err_t para reportar ESP_OK o un error estandar.
// [FLUJO] command_processor -> app_reply_fn_t -> USB/TCP/UART.
typedef esp_err_t (*app_reply_fn_t)(const char *line, void *ctx);
// [C/C++] Puntero a funcion usado para seleccionar el bus CAN activo sin que el
// parser conozca la implementacion de TWAI.
// [FLUJO] comando `can1`/`can2` -> callback -> can_driver_twai.
typedef esp_err_t (*app_can_select_bus_fn_t)(int bus);
// [C/C++] Puntero a funcion usado para transmitir una trama CAN estandar.
// [FLUJO] comando `send` -> parser ASCII -> callback -> TWAI.
typedef esp_err_t (*app_can_send_standard_fn_t)(int bus, uint32_t id, const uint8_t *data, size_t len);
// [C/C++] Puntero a funcion para el camino generico de texto.
// [FLUJO] texto no clasificado -> callback -> handler comun si aplica.
typedef esp_err_t (*app_text_input_fn_t)(const char *text, app_reply_fn_t reply, void *ctx);
// [C/C++] Punteros a funcion para contextos de respuesta diferida.
// [RTOS] Evitan guardar punteros a stack o sockets crudos en tareas largas.
typedef void *(*app_reply_ctx_clone_fn_t)(void *ctx);
typedef void (*app_reply_ctx_release_fn_t)(void *ctx);

// [ACURATEX] Valores logicos de bus vistos por el protocolo textual.
// No son pines ni controlan directamente hardware; el driver CAN los interpreta.
enum {
    APP_CMD_CAN_BUS_NONE = 0,
    APP_CMD_CAN_BUS_1 = 1,
    APP_CMD_CAN_BUS_2 = 2,
};

// [ACURATEX] Estructura de entorno que app_main llena antes de llamar al
// procesador. Reune estado actual y callbacks para que command_processor.cpp
// pueda ser independiente del transporte concreto.
typedef struct {
    // [ACURATEX] true cuando la linea entro por la ruta USB activa.
    bool usb_mounted;
    // [ACURATEX] Estado WiFi usado por `status`.
    bool wifi_connected;
    // [C/C++] Punteros a strings administrados por wifi_manager/can_driver.
    // El procesador los lee, no los libera.
    const char *wifi_ip;
    int tcp_port;
    const char *wifi_ssid;
    int active_bus;
    const char *active_bus_name;
    const char *can_status;
    // [C/C++] Callbacks que desacoplan el parser de la implementacion CAN.
    app_can_select_bus_fn_t can_select_bus;
    app_can_send_standard_fn_t can_send_standard;
    app_text_input_fn_t text_input;
    // [RTOS] Si una accion asincrona necesita responder despues, clona el ctx
    // con estas funciones. En la arquitectura nueva se clona app_reply_route_t.
    app_reply_ctx_clone_fn_t reply_ctx_clone;
    app_reply_ctx_release_fn_t reply_ctx_release;
    // [ACURATEX] Limites del formato textual de tramas CAN aceptado.
    size_t can_max_frame_len;
    uint32_t can_std_id_mask;
} app_command_env_t;

bool app_command_line_is_physical(const char *incoming_line,
                                  const app_command_env_t *env);

// [ACURATEX] Entrada unica del parser de comandos de aplicacion.
/**
 * [POR QUE EXISTE]
 * Es el dispatcher central de lineas recibidas desde USB, TCP o UART.
 *
 * [QUIEN LA LLAMA]
 * app_main para USB/UART y tcp_server.cpp mediante app_handle_tcp_line().
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que un transporte entrega una linea completa terminada por salto o
 * paquete equivalente.
 *
 * [ENTRADAS]
 * Recibe la linea, callback de respuesta, contexto del transporte y
 * app_command_env_t con estado/callbacks disponibles.
 *
 * [SALIDAS]
 * Devuelve esp_err_t y envia una respuesta textual por el callback.
 *
 * [ESTADO QUE MODIFICA]
 * No guarda estado propio importante; delega cambios a FILE, HEAD o CAN segun
 * el comando.
 *
 * [CONCURRENCIA]
 * Puede ser invocado desde tareas distintas segun transporte. La sincronizacion
 * real esta en los modulos delegados y en los mutex de respuesta.
 *
 * [FLUJO ACURATEX]
 * Transporte -> command_processor -> FILE/HEAD/CAN/status -> respuesta.
 *
 * [EQUIVALENCIA MCU]
 * Es el parser principal de comandos de un firmware con varios buses de entrada.
 *
 * [SI NO EXISTIERA]
 * Cada transporte tendria que duplicar la logica de protocolos Acuratex.
 */
esp_err_t app_command_process_line(const char *incoming_line,
                                   app_reply_fn_t reply,
                                   void *ctx,
                                   const app_command_env_t *env);
