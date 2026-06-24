#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include "esp_err.h"

/**
 * [POR QUE EXISTE]
 * Prepara LittleFS y el estado interno de transferencias FILE_*.
 *
 * [QUIEN LA LLAMA]
 * app_main().
 *
 * [CUANDO SE EJECUTA]
 * Durante el arranque del firmware.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Monta /fs, limpia upload/download y carga seleccion persistida.
 *
 * [CONCURRENCIA]
 * Corre antes del uso normal por comandos.
 *
 * [FLUJO ACURATEX]
 * app_main -> app_file_transfer_init -> FILE_* disponible.
 *
 * [EQUIVALENCIA MCU]
 * Es inicializar el almacenamiento en flash.
 *
 * [SI NO EXISTIERA]
 * FILE_* dependeria de inicializacion perezosa sin limpieza previa.
 */
void app_file_transfer_init(void);

/**
 * [POR QUE EXISTE]
 * Permite al command_processor reconocer comandos FILE_*.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp.
 *
 * [CUANDO SE EJECUTA]
 * Por cada linea recibida antes de clasificarla como CAN o texto generico.
 *
 * [ENTRADAS]
 * Linea C normalizada.
 *
 * [SALIDAS]
 * true si corresponde al protocolo FILE_*.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea.
 *
 * [FLUJO ACURATEX]
 * linea -> is_command -> process_line.
 *
 * [EQUIVALENCIA MCU]
 * Es validar prefijo de comando.
 *
 * [SI NO EXISTIERA]
 * El procesador central no sabria derivar FILE_* al modulo correcto.
 */
bool app_file_transfer_is_command(const char *line);

/**
 * [POR QUE EXISTE]
 * Ejecuta FILE_BEGIN, FILE_DATA, FILE_END, FILE_LIST, FILE_GET y relacionados.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp cuando detecta FILE_*.
 *
 * [CUANDO SE EJECUTA]
 * Bajo pedido de la app por USB/TCP/UART.
 *
 * [ENTRADAS]
 * Linea de comando, buffer de respuesta y tamano del buffer.
 *
 * [SALIDAS]
 * ESP_OK con una respuesta textual en `response`, o ESP_ERR_INVALID_ARG si la
 * llamada es invalida.
 *
 * [ESTADO QUE MODIFICA]
 * Puede modificar LittleFS, upload/download y archivo seleccionado.
 *
 * [CONCURRENCIA]
 * No crea tareas; procesa una linea y retorna.
 *
 * [FLUJO ACURATEX]
 * FILE_* -> LittleFS/Base64/chunks -> respuesta a app.
 *
 * [EQUIVALENCIA MCU]
 * Es el interprete de comandos de almacenamiento.
 *
 * [SI NO EXISTIERA]
 * No habria transferencia ni gestion de programas TXT.
 */
esp_err_t app_file_transfer_process_line(const char *line, char *response, size_t response_size);