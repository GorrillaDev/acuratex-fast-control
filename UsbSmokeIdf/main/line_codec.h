#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

// [ACURATEX] Utilidades compartidas para normalizar lineas de comandos y
// parsear el formato textual de tramas CAN.

/**
 * [POR QUE EXISTE]
 * Recorta espacios, CR y LF al inicio/final de una linea modificable.
 *
 * [QUIEN LA LLAMA]
 * Transportes, parser de comandos y lectores de configuracion.
 *
 * [CUANDO SE EJECUTA]
 * Antes de comparar comandos o parsear texto.
 *
 * [ENTRADAS]
 * Puntero a string C modificable.
 *
 * [SALIDAS]
 * No devuelve valor; modifica el string en sitio.
 *
 * [ESTADO QUE MODIFICA]
 * Solo el buffer recibido.
 *
 * [CONCURRENCIA]
 * No usa globales.
 *
 * [FLUJO ACURATEX]
 * bytes recibidos -> linea limpia -> command_processor.
 *
 * [EQUIVALENCIA MCU]
 * Es limpiar una linea leida por Serial antes de interpretarla.
 *
 * [SI NO EXISTIERA]
 * Comandos con CR/LF o espacios podrian no coincidir.
 */
void app_trim_line(char *line);

/**
 * [POR QUE EXISTE]
 * Convierte una linea tipo `<id_hex> <byte_hex> ...` en campos CAN.
 *
 * [QUIEN LA LLAMA]
 * command_processor y command_head_program_runner antes de llamar al driver CAN.
 *
 * [CUANDO SE EJECUTA]
 * Para comandos `send`, tramas directas y lineas TXT `send`.
 *
 * [ENTRADAS]
 * Linea textual, punteros de salida, DLC maximo y max_id permitido.
 *
 * [SALIDAS]
 * true si extrae ID, datos y longitud validos.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe `*id`, `data[]` y `*len`.
 *
 * [CONCURRENCIA]
 * No comparte estado.
 *
 * [FLUJO ACURATEX]
 * `send 320 1D 00 FF` -> ID=0x320, DLC=3, DATA=1D 00 FF.
 *
 * [EQUIVALENCIA MCU]
 * Es llenar los campos ID/DLC/DATA antes de cargar el mailbox CAN.
 *
 * [SI NO EXISTIERA]
 * El firmware no podria convertir comandos ASCII a tramas CAN.
 */
bool app_parse_frame_line(const char *line,
                          uint32_t *id,
                          uint8_t *data,
                          size_t *len,
                          size_t max_data_len,
                          uint32_t max_id);