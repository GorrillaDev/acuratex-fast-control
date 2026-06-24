#include <ctype.h>
#include <stdlib.h>
#include <string.h>
#include "line_codec.h"

// [ACURATEX] Este archivo contiene rutinas de bajo nivel de texto.
// Los transportes entregan bytes, pero command_processor trabaja con lineas C
// limpias y, para CAN, con tokens hexadecimales validados.

/**
 * [POR QUE EXISTE]
 * Esta funcion normaliza una linea recibida por USB, TCP, UART o archivo,
 * quitando espacios y terminadores al inicio y al final.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_main(), app_poll_uart_rescue(), app_tcp_process_line(),
 * app_command_process_line() y wifi_manager al leer `wifi.txt`.
 *
 * [CUANDO SE EJECUTA]
 * Antes de clasificar comandos o interpretar claves/valores de configuracion.
 *
 * [ENTRADAS]
 * Recibe un puntero a un string C modificable.
 *
 * [SALIDAS]
 * No devuelve valor; modifica el buffer en sitio.
 *
 * [ESTADO QUE MODIFICA]
 * Solo modifica el contenido apuntado por `line`.
 *
 * [CONCURRENCIA]
 * No usa memoria global ni mutex. Es segura si cada llamada usa su propio
 * buffer.
 *
 * [FLUJO ACURATEX]
 * Transporte -> bytes -> string -> app_trim_line -> command_processor.
 *
 * [EQUIVALENCIA MCU]
 * Es el equivalente a limpiar una linea leida por Serial antes de comparar el
 * comando.
 *
 * [SI NO EXISTIERA]
 * `ping\r\n`, espacios o lineas con tabs podrian fallar al compararse con los
 * comandos esperados.
 */
void app_trim_line(char *line)
{
    size_t len;

    if (line == NULL) {
        return;
    }

    len = strlen(line);
    while (len > 0 && isspace((unsigned char)line[len - 1])) {
        // [C/C++] Se reemplaza el ultimo caracter de espacio por terminador
        // nulo para acortar el string sin mover memoria.
        line[--len] = '\0';
    }

    if (len == 0) {
        return;
    }

    char *start = line;
    while (*start && isspace((unsigned char)*start)) {
        // [C/C++] start avanza dentro del mismo buffer hasta el primer caracter
        // no blanco.
        start++;
    }

    if (start != line) {
        // [C/C++] memmove permite mover dentro del mismo buffer con regiones
        // solapadas. strlen(start)+1 incluye el terminador '\0'.
        memmove(line, start, strlen(start) + 1);
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion valida y convierte un token hexadecimal a un byte CAN.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_parse_frame_line() para cada token de datos.
 *
 * [CUANDO SE EJECUTA]
 * Durante el parsing de lineas `send` o tramas CAN directas.
 *
 * [ENTRADAS]
 * Recibe el token textual y un puntero donde guardar el byte resultante.
 *
 * [SALIDAS]
 * Devuelve true si el token representa un valor entre 0x00 y 0xFF.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe `*out` solo cuando el token es valido.
 *
 * [CONCURRENCIA]
 * No comparte estado.
 *
 * [FLUJO ACURATEX]
 * Texto CAN -> token de dato -> uint8_t -> buffer de trama.
 *
 * [EQUIVALENCIA MCU]
 * Es como convertir dos caracteres ASCII hex a un registro de 8 bits.
 *
 * [SI NO EXISTIERA]
 * El parser aceptaria bytes invalidos o tendria que duplicar esta validacion.
 */
static bool app_parse_hex_byte(const char *token, uint8_t *out)
{
    char *endptr = NULL;
    long value;

    if (token == NULL || out == NULL) {
        return false;
    }

    // [C/C++] strtol con base 16 acepta tokens hexadecimales. endptr permite
    // comprobar que todo el token fue consumido.
    value = strtol(token, &endptr, 16);
    if (endptr == token || *endptr != '\0' || value < 0 || value > 0xFF) {
        return false;
    }

    // [C/C++] El cast a uint8_t es seguro porque ya se verifico 0..0xFF.
    *out = (uint8_t)value;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion valida y convierte el primer token de una trama CAN a ID.
 *
 * [QUIEN LA LLAMA]
 * Es llamada por app_parse_frame_line().
 *
 * [CUANDO SE EJECUTA]
 * Al iniciar el parseo de cada trama textual.
 *
 * [ENTRADAS]
 * Recibe token hexadecimal, puntero de salida y max_id permitido.
 *
 * [SALIDAS]
 * Devuelve true si el ID es valido y no supera max_id.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe `*out` con el ID validado.
 *
 * [CONCURRENCIA]
 * No bloquea ni comparte estado.
 *
 * [FLUJO ACURATEX]
 * Texto CAN -> ID hex -> uint32_t -> app_can_send_standard.
 *
 * [EQUIVALENCIA MCU]
 * Es validar que un identificador cabe en el campo de ID del controlador CAN.
 *
 * [SI NO EXISTIERA]
 * Podrian llegar identificadores fuera de rango al driver CAN.
 */
static bool app_parse_hex_id(const char *token, uint32_t *out, uint32_t max_id)
{
    char *endptr = NULL;
    unsigned long value;

    if (token == NULL || out == NULL) {
        return false;
    }

    // [C/C++] strtoul usa unsigned long porque el ID no debe ser negativo.
    // [ACURATEX] En este firmware max_id llega como 0x7FF desde el entorno, por
    // lo que el parser limita el ID al formato CAN estandar de 11 bits.
    value = strtoul(token, &endptr, 16);
    if (endptr == token || *endptr != '\0' || value > max_id) {
        return false;
    }

    // [C/C++] Cast a uint32_t despues de validar el limite max_id.
    *out = (uint32_t)value;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion interpreta el formato textual de trama CAN aceptado por la app:
 * primer token = ID hexadecimal, tokens siguientes = bytes hexadecimales.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_looks_like_frame() y app_process_frame_command().
 *
 * [CUANDO SE EJECUTA]
 * Cuando una linea tiene prefijo `send ` o parece una trama CAN directa.
 *
 * [ENTRADAS]
 * Recibe linea textual, punteros de salida para ID/datos/longitud, maximo de
 * datos y mascara/limite de ID.
 *
 * [SALIDAS]
 * Devuelve true si toda la linea es valida; llena id, data y len.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe en los buffers pasados por puntero.
 *
 * [CONCURRENCIA]
 * No usa globales; cada llamada trabaja sobre buffer local.
 *
 * [FLUJO ACURATEX]
 * App -> `send 123 01 02` -> app_parse_frame_line -> ID=0x123, DLC=2,
 * data={0x01,0x02}.
 *
 * [EQUIVALENCIA MCU]
 * Es la rutina que convierte un comando ASCII a los registros ID/DLC/DATA de
 * un controlador CAN.
 *
 * [SI NO EXISTIERA]
 * command_processor no podria validar ni construir tramas CAN desde texto.
 */
bool app_parse_frame_line(const char *line,
                          uint32_t *id,
                          uint8_t *data,
                          size_t *len,
                          size_t max_data_len,
                          uint32_t max_id)
{
    // [ACURATEX] Buffer local para tokenizar sin alterar la linea original que
    // puede venir de USB, TCP, UART o del TXT.
    char buffer[160];
    char *saveptr = NULL;
    char *token;
    size_t count = 0;

    if (line == NULL || id == NULL || data == NULL || len == NULL) {
        return false;
    }

    // [C/C++] strtok_r modifica el buffer. Por eso se copia line a un buffer
    // local antes de tokenizar.
    strlcpy(buffer, line, sizeof(buffer));
    // [C/C++] saveptr guarda el estado interno de strtok_r entre tokens.
    // [ACURATEX] El primer token siempre es el ID CAN; los demas tokens seran
    // bytes de datos y determinaran el DLC final.
    token = strtok_r(buffer, " ", &saveptr);
    if (token == NULL || !app_parse_hex_id(token, id, max_id)) {
        return false;
    }

    while ((token = strtok_r(NULL, " ", &saveptr)) != NULL) {
        // [ACURATEX] count se convierte en DLC. No puede superar
        // max_data_len, que en este firmware se llena como 8 para CAN clasico.
        if (count >= max_data_len || !app_parse_hex_byte(token, &data[count])) {
            return false;
        }
        count++;
    }

    // [ACURATEX] `count` es el DLC: numero de bytes de data[] que se copiaron.
    *len = count;
    return true;
}