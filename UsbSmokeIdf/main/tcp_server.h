#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"

#include "command_processor.h"

// [ACURATEX] Callback que conecta el servidor TCP con el procesador real de
// comandos. tcp_server.cpp no interpreta HEAD_*, FILE_* ni CAN: solo entrega
// lineas completas.
// [C/C++] Es un puntero a funcion: el modulo TCP guarda la direccion de una
// funcion de app_main y la invoca cuando recibe una linea.
typedef esp_err_t (*app_tcp_line_handler_t)(const char *line,
                                            uint32_t session_id,
                                            void *user_ctx);

/**
 * [POR QUE EXISTE]
 * Inicia el servidor TCP en el puerto indicado y registra el handler de lineas.
 *
 * [QUIEN LA LLAMA]
 * app_main desde app_poll_network_without_usb().
 *
 * [CUANDO SE EJECUTA]
 * Cuando WiFi esta conectado, USB no esta activo y toca habilitar red.
 *
 * [ENTRADAS]
 * Recibe puerto TCP, callback y contexto de usuario.
 *
 * [SALIDAS]
 * Devuelve esp_err_t para indicar ESP_OK o fallo de parametros/memoria.
 *
 * [ESTADO QUE MODIFICA]
 * Crea tarea TCP, guarda puerto, handler y mutex de envio.
 *
 * [CONCURRENCIA]
 * La tarea TCP corre en paralelo con app_main y usa mutex para send().
 *
 * [FLUJO ACURATEX]
 * WiFi IP -> TCP start -> app envia lineas -> command_processor.
 *
 * [EQUIVALENCIA MCU]
 * Es crear un canal de comunicaciones con una tarea dedicada.
 *
 * [SI NO EXISTIERA]
 * La app no podria controlar el firmware por red TCP.
 */
esp_err_t app_tcp_server_start(int port, app_tcp_line_handler_t handler, void *user_ctx);
/**
 * [POR QUE EXISTE]
 * Detiene listener, cliente activo y tarea TCP.
 *
 * [QUIEN LA LLAMA]
 * app_main cuando USB toma prioridad o WiFi deja de estar conectado.
 *
 * [CUANDO SE EJECUTA]
 * Al apagar la ruta de red.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Cierra sockets y marca la tarea como detenida.
 *
 * [CONCURRENCIA]
 * Cerrar sockets desbloquea accept()/recv() de la tarea TCP.
 *
 * [FLUJO ACURATEX]
 * USB activo o WiFi caido -> TCP stop -> no entran comandos por red.
 *
 * [EQUIVALENCIA MCU]
 * Es apagar ordenadamente un bus de comunicacion.
 *
 * [SI NO EXISTIERA]
 * TCP podria quedar aceptando comandos cuando no corresponde.
 */
void app_tcp_server_stop(void);
esp_err_t app_tcp_server_send_to_session(uint32_t session_id, const char *line);
uint32_t app_tcp_server_get_active_session_id(void);
/**
 * [POR QUE EXISTE]
 * Informa si la tarea TCP esta viva y marcada como running.
 *
 * [QUIEN LA LLAMA]
 * app_main para decidir arranque/parada de red y discovery.
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
 * Lee variables actualizadas por la tarea TCP y stop().
 *
 * [FLUJO ACURATEX]
 * app_main -> consultar TCP -> iniciar/parar discovery.
 *
 * [EQUIVALENCIA MCU]
 * Es una bandera de servicio activo.
 *
 * [SI NO EXISTIERA]
 * app_main no podria saber si TCP ya esta disponible.
 */
bool app_tcp_server_is_running(void);
/**
 * [POR QUE EXISTE]
 * Clona el contexto de respuesta TCP para respuestas asincronas.
 *
 * [QUIEN LA LLAMA]
 * Modulos que necesitan conservar reply_ctx mas alla del handler inmediato.
 *
 * [CUANDO SE EJECUTA]
 * Cuando una accion puede responder mas tarde por el mismo canal TCP.
 *
 * [ENTRADAS]
 * Recibe void* con el contexto real de TCP.
 *
 * [SALIDAS]
 * Devuelve puntero clonado o NULL.
 *
 * [ESTADO QUE MODIFICA]
 * Reserva memoria heap para la copia.
 *
 * [CONCURRENCIA]
 * La validez del descriptor depende de que el cliente siga conectado.
 *
 * [FLUJO ACURATEX]
 * Handler asincrono -> clonar contexto -> responder despues por TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es guardar el identificador de un canal para contestar luego.
 *
 * [SI NO EXISTIERA]
 * Los modulos asincronos no tendrian forma generica de conservar el canal TCP.
 */
void *app_tcp_reply_ctx_clone(void *ctx);
/**
 * [POR QUE EXISTE]
 * Libera una copia de contexto TCP creada por app_tcp_reply_ctx_clone().
 *
 * [QUIEN LA LLAMA]
 * Modulos que terminaron una respuesta asincrona por TCP.
 *
 * [CUANDO SE EJECUTA]
 * Despues de usar el contexto clonado.
 *
 * [ENTRADAS]
 * Recibe el puntero a liberar.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Libera memoria heap.
 *
 * [CONCURRENCIA]
 * No sincroniza sockets; solo libera memoria del contexto.
 *
 * [FLUJO ACURATEX]
 * Respuesta diferida completada -> liberar contexto TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es devolver un recurso reservado.
 *
 * [SI NO EXISTIERA]
 * Cada clon de contexto quedaria filtrado en memoria.
 */
void app_tcp_reply_ctx_release(void *ctx);
