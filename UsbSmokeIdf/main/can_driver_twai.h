#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "esp_err.h"

/**
 * [POR QUE EXISTE]
 * Inicializa el controlador TWAI interno del ESP32-S3 y el pin STBY del
 * transceiver CAN.
 *
 * [QUIEN LA LLAMA]
 * La llama app_main() durante el arranque.
 *
 * [CUANDO SE EJECUTA]
 * Una vez al inicio, antes de aceptar comandos `send`, `can1`, `can2` o acciones
 * HEAD que transmiten CAN.
 *
 * [ENTRADAS]
 * No recibe parametros; usa pines y bitrate definidos en can_driver_twai.cpp.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si TWAI queda instalado e iniciado, o un esp_err_t de GPIO/TWAI.
 *
 * [ESTADO QUE MODIFICA]
 * Cambia banderas internas de driver instalado/iniciado y selecciona CAN1.
 *
 * [CONCURRENCIA]
 * Se ejecuta en app_main antes del uso concurrente del driver.
 *
 * [FLUJO ACURATEX]
 * Reset -> app_main -> app_can_init -> comandos CAN disponibles.
 *
 * [EQUIVALENCIA MCU]
 * Es equivalente a configurar pines, bitrate y habilitar un periferico CAN.
 *
 * [SI NO EXISTIERA]
 * El firmware no podria transmitir tramas CAN/TWAI.
 */
esp_err_t app_can_init(void);

/**
 * [POR QUE EXISTE]
 * Informa si el driver TWAI esta realmente corriendo.
 *
 * [QUIEN LA LLAMA]
 * La usan app_can_select_bus(), app_can_send_standard() y app_can_get_status_name().
 *
 * [CUANDO SE EJECUTA]
 * Antes de seleccionar bus o transmitir, y al construir respuestas de estado.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve true si el driver fue instalado, iniciado y TWAI reporta RUNNING.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee banderas internas y consulta estado TWAI; no toma mutex propio.
 *
 * [FLUJO ACURATEX]
 * status/send -> verificar TWAI -> continuar o devolver error.
 *
 * [EQUIVALENCIA MCU]
 * Es leer el estado del periferico antes de usarlo.
 *
 * [SI NO EXISTIERA]
 * Se intentaria transmitir aunque TWAI no estuviera listo.
 */
bool app_can_is_started(void);

/**
 * [POR QUE EXISTE]
 * Selecciona el bus logico CAN1 o CAN2 usado por futuras transmisiones.
 *
 * [QUIEN LA LLAMA]
 * command_processor y el interprete TXT cuando reciben `can1` o `can2`.
 *
 * [CUANDO SE EJECUTA]
 * Antes de comandos `send` o lineas TXT que deben salir por un bus logico.
 *
 * [ENTRADAS]
 * Recibe 1 o 2.
 *
 * [SALIDAS]
 * Devuelve ESP_OK, ESP_ERR_INVALID_ARG o ESP_ERR_INVALID_STATE.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza el bus logico seleccionado.
 *
 * [CONCURRENCIA]
 * No toma mutex propio; el firmware mantiene un unico controlador TWAI.
 *
 * [FLUJO ACURATEX]
 * App/TXT -> can2 -> s_selected_bus=2 -> send usa CAN2 como nombre logico.
 *
 * [EQUIVALENCIA MCU]
 * Es seleccionar un canal de salida abstracto antes de escribir.
 *
 * [SI NO EXISTIERA]
 * `can1`/`can2` no tendrian efecto para la app ni el TXT.
 */
esp_err_t app_can_select_bus(int bus);

/**
 * [POR QUE EXISTE]
 * Construye una solicitud CAN estandar y la encola para la tarea unica TWAI.
 *
 * [QUIEN LA LLAMA]
 * command_processor, command_head_program_runner y head_state_manager_tick().
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que la app, un TXT o RUN necesitan enviar CAN.
 *
 * [ENTRADAS]
 * Bus logico, ID estandar de 11 bits, puntero a datos y DLC/len 0..8.
 *
 * [SALIDAS]
 * ESP_OK si la tarea can_tx confirma que TWAI acepto la trama, o esp_err_t si
 * falla validacion, estado, cola o timeout TWAI.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica protocolo de aplicacion; entrega una copia a app_can_tx_queue.
 *
 * [CONCURRENCIA]
 * Puede bloquear esperando la notificacion de resultado de can_tx.
 *
 * [FLUJO ACURATEX]
 * send/TXT/RUN -> app_can_send_standard -> can_tx -> TWAI -> bus CAN.
 *
 * [EQUIVALENCIA MCU]
 * Es pedir a la unica tarea CAN que escriba ID, DLC y DATA en TWAI.
 *
 * [SI NO EXISTIERA]
 * Ningun comando podria llegar fisicamente al bus CAN.
 */
esp_err_t app_can_send_standard(int bus, uint32_t id, const uint8_t *data, size_t len);

/**
 * [POR QUE EXISTE]
 * Devuelve el bus logico actualmente seleccionado.
 *
 * [QUIEN LA LLAMA]
 * app_fill_command_env().
 *
 * [CUANDO SE EJECUTA]
 * Al preparar el entorno de cada comando.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve 1 o 2.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee una variable estatica simple.
 *
 * [FLUJO ACURATEX]
 * Transporte -> env.active_bus -> command_processor.
 *
 * [EQUIVALENCIA MCU]
 * Es consultar un selector de salida.
 *
 * [SI NO EXISTIERA]
 * El procesador de comandos no sabria que bus usar por defecto.
 */
int app_can_get_selected_bus(void);

/**
 * [POR QUE EXISTE]
 * Devuelve `CAN1` o `CAN2` para respuestas y logs.
 *
 * [QUIEN LA LLAMA]
 * app_fill_command_env() y respuestas `TX_OK`.
 *
 * [CUANDO SE EJECUTA]
 * Al construir el entorno de comandos o mensajes de estado.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Puntero a string constante.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee s_selected_bus sin bloquear.
 *
 * [FLUJO ACURATEX]
 * TX_OK bus=CAN1/CAN2.
 *
 * [EQUIVALENCIA MCU]
 * Es convertir un enum/selector a texto.
 *
 * [SI NO EXISTIERA]
 * La app no veria el nombre del bus en respuestas humanas.
 */
const char *app_can_get_selected_bus_name(void);

/**
 * [POR QUE EXISTE]
 * Resume el estado CAN como texto para `status`.
 *
 * [QUIEN LA LLAMA]
 * app_fill_command_env().
 *
 * [CUANDO SE EJECUTA]
 * Al construir una respuesta de estado.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve `TWAI` si el driver corre o `ERROR` si no.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Llama app_can_is_started().
 *
 * [FLUJO ACURATEX]
 * status -> CAN=TWAI/ERROR.
 *
 * [EQUIVALENCIA MCU]
 * Es reportar si el periferico esta habilitado.
 *
 * [SI NO EXISTIERA]
 * La app no podria diagnosticar CAN en la linea de estado.
 */
const char *app_can_get_status_name(void);
