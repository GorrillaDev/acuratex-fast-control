#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "esp_err.h"

// [ACURATEX] El firmware maneja como maximo J1..J8. Este limite mantiene
// arreglos fijos y evita reservar memoria dinamica para el estado del cabezal.
#define APP_HEAD_STATE_MAX_J 8
#define APP_HEAD_STATE_MAX_YARN 2
#define APP_HEAD_STATE_MAX_SIC 2
#define APP_HEAD_STATE_MAX_STITCH 4

// [C/C++] `typedef` crea un nombre corto para un puntero a funcion.
// [ACURATEX] El gestor de estado no conoce directamente el driver CAN/TWAI:
// recibe un callback con la misma forma que necesita para enviar una trama.
typedef esp_err_t (*app_head_state_manager_can_send_standard_fn_t)(int bus,
                                                                   uint32_t id,
                                                                   const uint8_t *data,
                                                                   size_t len);

/**
 * [POR QUE EXISTE]
 * Inicializa la fuente de verdad del estado J y el mutex que la protege.
 *
 * [QUIEN LA LLAMA]
 * La llama app_main() durante el arranque del firmware.
 *
 * [CUANDO SE EJECUTA]
 * Una vez al iniciar, antes de aceptar HEAD_ACTION o publicar estado.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve ESP_OK o un esp_err_t si no pudo crear/tomar el mutex.
 *
 * [ESTADO QUE MODIFICA]
 * Deja J1..J8 en registro fisico 0xFF y sin RUN activo.
 *
 * [CONCURRENCIA]
 * Crea y toma un mutex FreeRTOS porque despues varias tareas pueden consultar
 * o cambiar estos bytes.
 *
 * [FLUJO ACURATEX]
 * Reset -> app_main -> estado J inicial -> HEAD_ACTION puede usar XX.
 *
 * [EQUIVALENCIA MCU]
 * Es equivalente a inicializar registros de salida antes del loop principal.
 *
 * [SI NO EXISTIERA]
 * Las acciones J no tendrian un estado inicial confiable para calcular XX.
 */
esp_err_t app_head_state_manager_init(void);

/**
 * [POR QUE EXISTE]
 * Entrega el byte fisico actual de Jn para calcular un nuevo byte candidato.
 *
 * [QUIEN LA LLAMA]
 * La usa el parser de HEAD_ACTION Jx.CHy antes de sustituir XX.
 *
 * [CUANDO SE EJECUTA]
 * Al iniciar una accion dinamica J antes de leer el bloque TXT.
 *
 * [ENTRADAS]
 * `instance` es J1..J8 en base 1; `value` es el puntero de salida.
 *
 * [SALIDAS]
 * Devuelve true si copio el byte fisico actual.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado, solo toma una fotografia protegida por mutex.
 *
 * [CONCURRENCIA]
 * Toma el mutex interno con portMAX_DELAY para leer sin carrera.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION|J1.CH2 -> leer registro fisico -> calcular XX.
 *
 * [EQUIVALENCIA MCU]
 * Es como leer un latch software que representa un puerto activo-bajo.
 *
 * [SI NO EXISTIERA]
 * Jx.CHy no podria saber que bit alternar.
 */
bool app_head_state_manager_get_j_physical_register(uint8_t instance,
                                                    uint8_t *value);

/**
 * [POR QUE EXISTE]
 * Confirma que un byte fisico J ya fue enviado correctamente por CAN.
 *
 * [QUIEN LA LLAMA]
 * La llama el interprete TXT despues de un `send` con XX transmitido sin error.
 *
 * [CUANDO SE EJECUTA]
 * Solo despues de env->can_send_standard() exitoso.
 *
 * [ENTRADAS]
 * `instance` es J1..J8; `value` es el byte fisico confirmado.
 *
 * [SALIDAS]
 * Devuelve true si actualizo el estado.
 *
 * [ESTADO QUE MODIFICA]
 * Cambia el registro fisico, su mascara logica derivada y la revision J.
 *
 * [CONCURRENCIA]
 * Usa el mutex interno para que HEAD_STATUS, RUN y acciones TXT no mezclen
 * lecturas/escrituras.
 *
 * [FLUJO ACURATEX]
 * send con XX -> CAN OK -> commit del registro -> estado 427 coherente.
 *
 * [EQUIVALENCIA MCU]
 * Es como actualizar el espejo RAM de un puerto solo si la escritura al bus fue
 * aceptada.
 *
 * [SI NO EXISTIERA]
 * La siguiente accion J calcularia XX con un estado viejo.
 */
bool app_head_state_manager_commit_j_physical_register(uint8_t instance,
                                                       uint8_t value);

/**
 * [POR QUE EXISTE]
 * Obtiene el estado visible de Jn: byte fisico y bandera RUN.
 *
 * [QUIEN LA LLAMA]
 * La usan HEAD_STATUS, RUN/STOP y la logica que necesita saber si RUN sigue
 * activo antes de ciertas acciones J.
 *
 * [CUANDO SE EJECUTA]
 * Cuando la app pide estado o antes de preparar una accion J.
 *
 * [ENTRADAS]
 * `instance` es J1..J8; los punteros de salida pueden ser NULL si no se
 * necesita ese campo.
 *
 * [SALIDAS]
 * Devuelve true si pudo copiar los campos pedidos.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Protege la lectura con mutex.
 *
 * [FLUJO ACURATEX]
 * HEAD_STATUS -> snapshot J -> respuesta con Jn=XX y JnR.
 *
 * [EQUIVALENCIA MCU]
 * Es una lectura atomica de variables compartidas de estado.
 *
 * [SI NO EXISTIERA]
 * La app no podria ver el byte J ni saber si RUN esta activo.
 */
bool app_head_state_manager_get_j_status(uint8_t instance,
                                         uint8_t *physical_register,
                                         bool *running);

/**
 * [POR QUE EXISTE]
 * Inicia la secuencia automatica RUN de un modulo J.
 *
 * [QUIEN LA LLAMA]
 * La llama el handler directo de HEAD_ACTION|Jx.RUN.
 *
 * [CUANDO SE EJECUTA]
 * Cuando la app pide RUN para un J habilitado.
 *
 * [ENTRADAS]
 * `instance` indica J1..J8, `can_bus` selecciona CAN1/CAN2 y `now_ms` agenda
 * el primer paso.
 *
 * [SALIDAS]
 * Devuelve true si el RUN queda activo o actualizado.
 *
 * [ESTADO QUE MODIFICA]
 * Activa running, reinicia bit/secuencia, registra bus y marca revision.
 *
 * [CONCURRENCIA]
 * Toma el mutex interno; los pasos posteriores los ejecuta tick().
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION|J1.RUN -> RUN activo -> tick envia tramas 0x320.
 *
 * [EQUIVALENCIA MCU]
 * Es habilitar una maquina de estados periodica.
 *
 * [SI NO EXISTIERA]
 * Jx.RUN no podria arrancar la secuencia automatica.
 */
bool app_head_state_manager_start_j_run(uint8_t instance,
                                        int can_bus,
                                        uint32_t now_ms);

/**
 * [POR QUE EXISTE]
 * Detiene la secuencia automatica RUN de un modulo J.
 *
 * [QUIEN LA LLAMA]
 * La llama HEAD_ACTION|Jx.STOP, HEAD_STOP global y las rutas que deciden
 * detener RUN antes de continuar.
 *
 * [CUANDO SE EJECUTA]
 * Cuando se solicita detener RUN o cuando una transmision RUN falla.
 *
 * [ENTRADAS]
 * `instance` es J1..J8.
 *
 * [SALIDAS]
 * Devuelve true si el indice era valido y el gestor pudo procesarlo.
 *
 * [ESTADO QUE MODIFICA]
 * Pone running=false y limpia el proximo vencimiento.
 *
 * [CONCURRENCIA]
 * Usa mutex para no cortar una actualizacion parcial.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION|J1.STOP -> detener maquina RUN.
 *
 * [EQUIVALENCIA MCU]
 * Es deshabilitar un temporizador/estado periodico.
 *
 * [SI NO EXISTIERA]
 * RUN quedaria activo hasta reiniciar o fallar por otra causa.
 */
bool app_head_state_manager_stop_j_run(uint8_t instance);

/**
 * [POR QUE EXISTE]
 * Apaga todas las maquinas RUN de J1..J8.
 *
 * [QUIEN LA LLAMA]
 * La usa la logica de parada/cancelacion general del cabezal.
 *
 * [CUANDO SE EJECUTA]
 * Cuando se necesita dejar todos los RUN en estado no activo.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Puede cambiar running=false para varios J.
 *
 * [CONCURRENCIA]
 * Recorre todos los J con el mutex tomado.
 *
 * [FLUJO ACURATEX]
 * Stop global -> todos los RUN J detenidos.
 *
 * [EQUIVALENCIA MCU]
 * Es una parada general de maquinas de estado.
 *
 * [SI NO EXISTIERA]
 * Habria que detener cada J individualmente.
 */
void app_head_state_manager_stop_all_j_runs(void);

bool app_head_state_manager_start_den_run(uint8_t instance,
                                          int can_bus,
                                          uint32_t now_ms);

bool app_head_state_manager_start_den_run1(uint8_t instance,
                                           int can_bus,
                                           uint32_t now_ms);

bool app_head_state_manager_stop_den_run(uint8_t instance);

void app_head_state_manager_stop_all_den_runs(void);

bool app_head_state_manager_start_sic_run(uint8_t instance,
                                          int can_bus,
                                          uint32_t now_ms);

bool app_head_state_manager_stop_sic_run(uint8_t instance);

void app_head_state_manager_stop_all_sic_runs(void);

/**
 * [POR QUE EXISTE]
 * Inicia una secuencia RUN de Yarn.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp al recibir `y1_run`, `y2_run` o `y_run_all`.
 *
 * [CUANDO SE EJECUTA]
 * Cuando la app pide que Yarn empiece a barrer sus canales.
 *
 * [ENTRADAS]
 * `instance` es Yarn1 o Yarn2, `can_bus` es CAN1/CAN2 y `now_ms` agenda.
 *
 * [SALIDAS]
 * Devuelve true si el RUN queda activo.
 *
 * [ESTADO QUE MODIFICA]
 * Activa la cascada, reinicia fase y primer indice.
 *
 * [CONCURRENCIA]
 * Usa el mutex interno del gestor de estado.
 *
 * [FLUJO ACURATEX]
 * y1_run/y2_run -> Yarn RUN -> tick envia 0x320/0x1E.
 */
bool app_head_state_manager_start_yarn_run(uint8_t instance,
                                           int can_bus,
                                           uint32_t now_ms);

/**
 * [POR QUE EXISTE]
 * Detiene una secuencia RUN de Yarn.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp al recibir `y1_stop`, `y2_stop` o `y_stop_all`.
 */
bool app_head_state_manager_stop_yarn_run(uint8_t instance);

/**
 * [POR QUE EXISTE]
 * Detiene todas las secuencias RUN de Yarn.
 */
void app_head_state_manager_stop_all_yarn_runs(void);

/**
 * [POR QUE EXISTE]
 * Inicia una secuencia RUN de Stitch.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp al recibir `s_run_1..4` o `s_run_all`.
 */
bool app_head_state_manager_start_stitch_run(uint8_t instance,
                                             int can_bus,
                                             uint32_t now_ms);

/**
 * [POR QUE EXISTE]
 * Detiene una secuencia RUN de Stitch.
 */
bool app_head_state_manager_stop_stitch_run(uint8_t instance);

/**
 * [POR QUE EXISTE]
 * Detiene todas las secuencias RUN de Stitch.
 */
void app_head_state_manager_stop_all_stitch_runs(void);

/**
 * [POR QUE EXISTE]
 * Detiene todo RUN fisico del cabezal en una sola llamada.
 */
void app_head_state_manager_stop_all_motion(void);

/**
 * [POR QUE EXISTE]
 * Avanza las secuencias RUN pendientes y envia sus tramas CAN.
 *
 * [QUIEN LA LLAMA]
 * La llama una tarea periodica del firmware desde el modulo principal.
 *
 * [CUANDO SE EJECUTA]
 * Periodicamente, usando `now_ms` para saber que J ya vencio.
 *
 * [ENTRADAS]
 * Recibe callback de envio CAN y tiempo actual en milisegundos.
 *
 * [SALIDAS]
 * Devuelve ESP_OK o el error del envio CAN/timeout interno.
 *
 * [ESTADO QUE MODIFICA]
 * Confirma bytes fisicos de RUN, bit actual, direccion ON/OFF y proximo tiempo.
 *
 * [CONCURRENCIA]
 * Toma snapshot bajo mutex, libera mutex para enviar CAN y vuelve a tomarlo
 * para confirmar si la revision no cambio.
 *
 * [FLUJO ACURATEX]
 * RUN activo -> tick -> CAN ID 0x320 -> commit de byte fisico.
 *
 * [EQUIVALENCIA MCU]
 * Es una tarea de temporizador que camina bits de un registro activo-bajo.
 *
 * [SI NO EXISTIERA]
 * HEAD_ACTION|Jx.RUN solo cambiaria una bandera, pero no enviaria secuencia CAN.
 */
esp_err_t app_head_state_manager_tick(app_head_state_manager_can_send_standard_fn_t can_send_standard,
                                      uint32_t now_ms);

/**
 * [POR QUE EXISTE]
 * Actualiza estado J para acciones que se completaron por el flujo TXT normal.
 *
 * [QUIEN LA LLAMA]
 * La llama el runner al terminar una accion con exito, cuando aplica.
 *
 * [CUANDO SE EJECUTA]
 * Despues de HEAD_ACTION_DONE para acciones J reconocibles.
 *
 * [ENTRADAS]
 * Recibe texto de accion como `J1.CH2`, `J1.ON_ALL` o `J1.OFF_ALL`.
 *
 * [SALIDAS]
 * Devuelve true si cambio algun estado J.
 *
 * [ESTADO QUE MODIFICA]
 * Alterna bits CH o fuerza 0x00/0xFF para ON_ALL/OFF_ALL.
 *
 * [CONCURRENCIA]
 * Protege el cambio con mutex.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION_DONE -> actualizar espejo J -> HEAD_STATUS/427 consistentes.
 *
 * [EQUIVALENCIA MCU]
 * Es mantener actualizado el shadow register despues de una accion aceptada.
 *
 * [SI NO EXISTIERA]
 * Algunas acciones J terminadas no se reflejarian en el estado interno.
 */
bool app_head_state_manager_apply_successful_action(const char *action);
