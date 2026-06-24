#pragma once

#include "esp_err.h"
#include "command_processor.h"

// [ACURATEX] Interfaz publica del runner de programas de Cabezal.
// command_processor.cpp llama estas funciones cuando detecta comandos HEAD_*.

/**
 * [POR QUE EXISTE]
 * Inicializa el runner HEAD_* antes de aceptar comandos de Cabezal.
 *
 * [QUIEN LA LLAMA]
 * app_main() durante el arranque del firmware.
 *
 * [CUANDO SE EJECUTA]
 * Una vez, despues de preparar almacenamiento y antes del bucle principal.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Inicializa estado interno del runner, mutex y gestor de estado de Cabezal.
 *
 * [CONCURRENCIA]
 * Prepara recursos que luego usan comandos HEAD_ACTION y tareas periodicas.
 *
 * [FLUJO ACURATEX]
 * Reset -> app_main -> runner listo -> HEAD_PROGRAM_SELECT/HEAD_ACTION.
 *
 * [EQUIVALENCIA MCU]
 * Es inicializar la maquina de estados del modulo de aplicacion.
 *
 * [SI NO EXISTIERA]
 * Los comandos HEAD_* no tendrian estado ni sincronizacion inicial.
 */
void app_head_program_runner_init();
/**
 * [POR QUE EXISTE]
 * Entrega el nombre textual del estado actual del runner.
 *
 * [QUIEN LA LLAMA]
 * command_processor, FILE/status y logs que necesitan reportar estado HEAD_*.
 *
 * [CUANDO SE EJECUTA]
 * Al responder `status` o diagnosticar una accion de Cabezal.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve un const char* como IDLE, RUNNING, STOPPING, DONE o ERROR.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee estado global del runner.
 *
 * [FLUJO ACURATEX]
 * Estado interno -> respuesta hacia la app.
 *
 * [EQUIVALENCIA MCU]
 * Es convertir un enum de maquina de estados a texto.
 *
 * [SI NO EXISTIERA]
 * La app tendria menos informacion legible sobre HEAD_ACTION.
 */
const char *app_head_program_runner_state_name(void);
/**
 * [POR QUE EXISTE]
 * Expone la ultima etapa interna ejecutada por el runner.
 *
 * [QUIEN LA LLAMA]
 * tcp_server y otros modulos de respuesta cuando registran timeouts o errores.
 *
 * [CUANDO SE EJECUTA]
 * Al construir logs de diagnostico o reportes de estado.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve const char* con el ultimo stage conocido.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee texto global actualizado por el runner durante HEAD_ACTION.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION -> stage -> log/status diagnostico.
 *
 * [EQUIVALENCIA MCU]
 * Es un marcador de depuracion de una maquina de estados.
 *
 * [SI NO EXISTIERA]
 * Seria mas dificil saber donde fallo una accion TXT/CAN.
 */
const char *app_head_program_runner_last_stage(void);

/**
 * [POR QUE EXISTE]
 * Permite al procesador central reconocer si una linea pertenece a HEAD_*.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp antes de derivar el comando al runner.
 *
 * [CUANDO SE EJECUTA]
 * Por cada linea recibida desde USB, TCP o UART.
 *
 * [ENTRADAS]
 * Recibe la linea textual normalizada.
 *
 * [SALIDAS]
 * Devuelve true si el prefijo corresponde al protocolo de Cabezal.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea ni usa recursos compartidos.
 *
 * [FLUJO ACURATEX]
 * Transporte -> command_processor -> is_command -> process_line.
 *
 * [EQUIVALENCIA MCU]
 * Es comparar una cabecera de protocolo antes de llamar al handler.
 *
 * [SI NO EXISTIERA]
 * command_processor no sabria separar HEAD_* de FILE_* o CAN.
 */
bool app_head_program_is_command(const char *line);

/**
 * [POR QUE EXISTE]
 * Ejecuta los comandos HEAD_PROGRAM_SELECT, HEAD_ACTION, HEAD_STOP,
 * HEAD_STATUS y HEAD_PROGRAM_INFO.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp cuando app_head_program_is_command() devuelve true.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que la app envia un comando HEAD_* por cualquier transporte.
 *
 * [ENTRADAS]
 * Recibe linea, callback de respuesta, contexto de respuesta y entorno de
 * comandos con CAN, WiFi, USB y estado.
 *
 * [SALIDAS]
 * Devuelve esp_err_t y emite respuestas como OK|HEAD_ACTION,
 * HEAD_ACTION_DONE o errores HEAD_* segun el caso.
 *
 * [ESTADO QUE MODIFICA]
 * Puede seleccionar programa, lanzar/cancelar acciones, actualizar estado J y
 * usar LittleFS/TWAI por medio de otros modulos.
 *
 * [CONCURRENCIA]
 * Puede crear trabajo asincrono y usa mutex/estado compartido dentro del runner.
 *
 * [FLUJO ACURATEX]
 * App -> HEAD_* -> runner -> TXT/LittleFS/CAN/estado -> respuesta.
 *
 * [EQUIVALENCIA MCU]
 * Es el dispatcher de una maquina de estados de aplicacion.
 *
 * [SI NO EXISTIERA]
 * El firmware no podria ejecutar programas TXT ni acciones de Cabezal.
 */
esp_err_t app_head_program_process_line(const char *line,
                                        app_reply_fn_t reply,
                                        void *ctx,
                                        const app_command_env_t *env);
