#include "head_state_manager.h"

#include <string.h>
#include <strings.h>
#include <stdlib.h>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "esp_log.h"

#include "app_rtos_types.h"

// [ESP-IDF] TAG se imprime en ESP_LOGW/ESP_LOGI para identificar que el mensaje
// viene del gestor de estado del cabezal.
static const char *TAG = "head_state";

// [ACURATEX] Estado interno de los modulos J. Cada posicion del arreglo es un
// modulo independiente: indice 0 = J1, indice 7 = J8.
typedef struct {
    bool running;
    uint8_t phase;
    uint8_t p;
    uint32_t next_due_ms;
    uint16_t delay_ms;
    int can_bus;
    uint32_t revision;
} app_head_cascade_state_t;

typedef struct {
    // [ACURATEX] Registro fisico activo-bajo que se envia hacia hardware.
    // Fisico 0xFF significa todos los bits en 1; logicamente equivale a 0x00.
    uint8_t j_physical_register[APP_HEAD_STATE_MAX_J];
    // [ACURATEX] Mascara logica derivada: se guarda como complemento del fisico.
    // Ejemplo: fisico 0xFE -> logico 0x01 porque el bit 0 esta activo.
    uint8_t j_logical_mask[APP_HEAD_STATE_MAX_J];
    // [ACURATEX] RUN indica si Jn esta en secuencia automatica de bits.
    bool j_running[APP_HEAD_STATE_MAX_J];
    // [ACURATEX] En RUN, true significa que el barrido actual esta encendiendo
    // bits fisicos con 0; false significa que los esta apagando con 1.
    bool j_turning_on[APP_HEAD_STATE_MAX_J];
    // [ACURATEX] Bit fisico siguiente que RUN va a cambiar, de 0 a 7.
    uint8_t j_bit[APP_HEAD_STATE_MAX_J];
    // [FREERTOS] Tiempo en milisegundos del proximo paso RUN para cada J.
    uint32_t j_next_due_ms[APP_HEAD_STATE_MAX_J];
    // [ACURATEX] Bus CAN elegido para el RUN de cada modulo J.
    int j_can_bus[APP_HEAD_STATE_MAX_J];
    // [CONCURRENCIA] Revision para detectar si otro comando cambio J mientras
    // tick() estaba enviando CAN con el mutex liberado.
    uint32_t j_revision[APP_HEAD_STATE_MAX_J];
    app_head_cascade_state_t yarn[APP_HEAD_STATE_MAX_YARN];
    app_head_cascade_state_t stitch[APP_HEAD_STATE_MAX_STITCH];
} app_head_state_manager_state_t;

// [FREERTOS] Mutex que protege `s_state`.
static SemaphoreHandle_t s_head_state_mutex = NULL;
// [C/C++] `static` limita esta fuente de verdad al archivo.
static app_head_state_manager_state_t s_state;

/**
 * [POR QUE EXISTE]
 * Centraliza la toma del mutex del estado J.
 *
 * [QUIEN LA LLAMA]
 * Todas las funciones que leen o modifican s_state.
 *
 * [CUANDO SE EJECUTA]
 * Antes de tocar el registro fisico, RUN, bit, bus o revision.
 *
 * [ENTRADAS]
 * Recibe timeout FreeRTOS en ticks.
 *
 * [SALIDAS]
 * Devuelve true si el mutex existe y fue tomado.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica datos J; solo ocupa el mutex.
 *
 * [CONCURRENCIA]
 * Puede bloquear hasta `timeout`, incluido portMAX_DELAY.
 *
 * [FLUJO ACURATEX]
 * Cualquier HEAD_ACTION/HEAD_STATUS -> tomar mutex -> leer/cambiar J.
 *
 * [EQUIVALENCIA MCU]
 * Es como entrar a una seccion critica antes de tocar un registro compartido.
 *
 * [SI NO EXISTIERA]
 * Cada funcion repetiria la comprobacion del mutex y seria mas facil olvidar la
 * proteccion.
 */
static bool app_head_state_take_mutex(TickType_t timeout)
{
    return s_head_state_mutex != NULL && xSemaphoreTake(s_head_state_mutex, timeout) == pdTRUE;
}

/**
 * [POR QUE EXISTE]
 * Libera el mutex tomado por app_head_state_take_mutex().
 *
 * [QUIEN LA LLAMA]
 * Todas las funciones que terminan una lectura o escritura protegida.
 *
 * [CUANDO SE EJECUTA]
 * Al salir de una seccion protegida del estado J.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * No cambia s_state; libera el mutex si existe.
 *
 * [CONCURRENCIA]
 * Desbloquea a otras tareas que esperan leer o escribir J.
 *
 * [FLUJO ACURATEX]
 * Lectura/cambio J completado -> liberar mutex -> otra tarea puede continuar.
 *
 * [EQUIVALENCIA MCU]
 * Es como salir de una seccion critica.
 *
 * [SI NO EXISTIERA]
 * Las tareas quedarian bloqueadas esperando el mutex.
 */
static void app_head_state_give_mutex(void)
{
    if (s_head_state_mutex != NULL) {
        xSemaphoreGive(s_head_state_mutex);
    }
}

/**
 * [POR QUE EXISTE]
 * Convierte cualquier bus no reconocido al bus logico CAN1.
 *
 * [QUIEN LA LLAMA]
 * start_j_run() y tick().
 *
 * [CUANDO SE EJECUTA]
 * Al guardar o usar el bus elegido para RUN.
 *
 * [ENTRADAS]
 * Recibe un entero de bus.
 *
 * [SALIDAS]
 * Devuelve 2 solo si la entrada es 2; en otro caso devuelve 1.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No usa mutex porque solo calcula un valor local.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION|Jx.RUN -> bus normalizado -> envio CAN RUN.
 *
 * [EQUIVALENCIA MCU]
 * Es una validacion defensiva de selector de periferico.
 *
 * [SI NO EXISTIERA]
 * Un valor fuera de rango podria propagarse al callback CAN.
 */
static int app_head_state_normalize_bus(int bus)
{
    return bus == 2 ? 2 : 1;
}

/**
 * [POR QUE EXISTE]
 * Incrementa la version de una cascada Yarn/Stitch.
 *
 * [QUIEN LA LLAMA]
 * Las rutas start/stop y tick() cuando confirma o cancela un paso.
 */
static void app_head_state_bump_cascade_revision_locked(app_head_cascade_state_t *cascade)
{
    if (cascade == NULL) {
        return;
    }

    cascade->revision++;
    if (cascade->revision == 0) {
        cascade->revision = 1;
    }
}

/**
 * [POR QUE EXISTE]
 * Restaura una cascada Yarn/Stitch a su estado neutral.
 *
 * [QUIEN LA LLAMA]
 * app_head_state_manager_init().
 */
static void app_head_state_reset_cascade_locked(app_head_cascade_state_t *cascade)
{
    if (cascade == NULL) {
        return;
    }

    cascade->running = false;
    cascade->phase = 0;
    cascade->p = 1;
    cascade->next_due_ms = 0;
    cascade->delay_ms = 80;
    cascade->can_bus = 1;
}

/**
 * [POR QUE EXISTE]
 * Arranca o reinicia una cascada Yarn/Stitch.
 */
static void app_head_state_start_cascade_locked(app_head_cascade_state_t *cascade,
                                                int bus,
                                                uint32_t now_ms,
                                                uint16_t delay_ms)
{
    if (cascade == NULL) {
        return;
    }

    cascade->running = true;
    cascade->phase = 0;
    cascade->p = 1;
    cascade->next_due_ms = now_ms;
    cascade->delay_ms = delay_ms;
    cascade->can_bus = app_head_state_normalize_bus(bus);
    app_head_state_bump_cascade_revision_locked(cascade);
}

/**
 * [POR QUE EXISTE]
 * Detiene una cascada Yarn/Stitch sin cambiar su ultimo estado confirmado.
 */
static void app_head_state_stop_cascade_locked(app_head_cascade_state_t *cascade)
{
    if (cascade == NULL) {
        return;
    }

    cascade->running = false;
    cascade->next_due_ms = 0;
    app_head_state_bump_cascade_revision_locked(cascade);
}

/**
 * [POR QUE EXISTE]
 * Marca que el estado de un J cambio.
 *
 * [QUIEN LA LLAMA]
 * Las funciones `*_locked` y commits que alteran registro/RUN.
 *
 * [CUANDO SE EJECUTA]
 * Mientras el mutex ya esta tomado.
 *
 * [ENTRADAS]
 * Recibe indice base 0 del modulo J.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Incrementa j_revision[index] y evita que quede en 0.
 *
 * [CONCURRENCIA]
 * Debe llamarse con mutex tomado; ayuda a detectar carreras en tick().
 *
 * [FLUJO ACURATEX]
 * Cambio J -> revision nueva -> snapshot viejo no puede sobrescribirlo.
 *
 * [EQUIVALENCIA MCU]
 * Es un contador de version de un registro compartido.
 *
 * [SI NO EXISTIERA]
 * tick() podria confirmar una trama usando un snapshot obsoleto.
 */
static void app_head_state_bump_revision_locked(int index)
{
    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        return;
    }

    s_state.j_revision[index]++;
    if (s_state.j_revision[index] == 0) {
        s_state.j_revision[index] = 1;
    }
}

/**
 * [POR QUE EXISTE]
 * Escribe el registro fisico J y recalcula la mascara logica.
 *
 * [QUIEN LA LLAMA]
 * Inicializacion, commits dinamicos XX, RUN y acciones ON_ALL/OFF_ALL/CH.
 *
 * [CUANDO SE EJECUTA]
 * Solo dentro de secciones con mutex ya tomado.
 *
 * [ENTRADAS]
 * `index` es base 0; `value` es el byte fisico activo-bajo.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza j_physical_register[index] y j_logical_mask[index].
 *
 * [CONCURRENCIA]
 * No toma mutex por si misma; el sufijo `_locked` documenta ese requisito.
 *
 * [FLUJO ACURATEX]
 * CAN OK o accion local -> byte fisico nuevo -> mascara logica derivada.
 *
 * [EQUIVALENCIA MCU]
 * Es escribir el shadow register y su vista logica.
 *
 * [SI NO EXISTIERA]
 * Habria duplicacion y riesgo de que fisico/logico quedaran inconsistentes.
 */
static void app_head_state_set_j_physical_locked(int index, uint8_t value)
{
    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        return;
    }

    s_state.j_physical_register[index] = value;
    // [ACURATEX] El hardware es activo-bajo: bit fisico 0 significa activo.
    // Por eso la vista logica se calcula invirtiendo el byte.
    s_state.j_logical_mask[index] = (uint8_t)~value;
}

/**
 * [POR QUE EXISTE]
 * Limpia los campos de la maquina RUN de un J sin alterar el byte fisico.
 *
 * [QUIEN LA LLAMA]
 * init() y stop/stop_all indirectamente.
 *
 * [CUANDO SE EJECUTA]
 * Al arrancar o cuando se necesita dejar RUN sin programacion pendiente.
 *
 * [ENTRADAS]
 * Indice J base 0.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * running, turning_on, bit, next_due_ms y bus de un J.
 *
 * [CONCURRENCIA]
 * Requiere mutex tomado por el llamador.
 *
 * [FLUJO ACURATEX]
 * Reset/stop -> RUN inactivo -> el tick ya no agenda tramas.
 *
 * [EQUIVALENCIA MCU]
 * Es reiniciar variables de una maquina de estados.
 *
 * [SI NO EXISTIERA]
 * Un RUN anterior podria dejar bit o tiempo residual.
 */
static void app_head_state_reset_j_run_locked(int index)
{
    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        return;
    }

    s_state.j_running[index] = false;
    s_state.j_turning_on[index] = true;
    s_state.j_bit[index] = 0;
    s_state.j_next_due_ms[index] = 0;
    s_state.j_can_bus[index] = 1;
}

/**
 * [POR QUE EXISTE]
 * Prepara el primer paso de la secuencia RUN para un J.
 *
 * [QUIEN LA LLAMA]
 * app_head_state_manager_start_j_run().
 *
 * [CUANDO SE EJECUTA]
 * Al recibir HEAD_ACTION|Jx.RUN cuando ese J no estaba corriendo.
 *
 * [ENTRADAS]
 * Indice base 0, bus CAN y hora actual.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Activa running, reinicia bit 0, pone fisico 0xFF/logico 0x00 y agenda ahora.
 *
 * [CONCURRENCIA]
 * Requiere mutex tomado por el llamador.
 *
 * [FLUJO ACURATEX]
 * Jx.RUN -> estado RUN inicial -> tick enviara bit 0 activo-bajo.
 *
 * [EQUIVALENCIA MCU]
 * Es armar el estado inicial de una rutina temporizada.
 *
 * [SI NO EXISTIERA]
 * RUN no tendria punto de arranque ni bus definido.
 */
static void app_head_state_start_j_run_locked(int index, int bus, uint32_t now_ms)
{
    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        return;
    }

    s_state.j_running[index] = true;
    s_state.j_turning_on[index] = true;
    s_state.j_bit[index] = 0;
    // [ACURATEX] RUN arranca desde todo apagado fisico: FF equivale a logico 00.
    s_state.j_physical_register[index] = 0xFF;
    s_state.j_logical_mask[index] = 0x00;
    s_state.j_next_due_ms[index] = now_ms;
    s_state.j_can_bus[index] = app_head_state_normalize_bus(bus);
    app_head_state_bump_revision_locked(index);
}

/**
 * [POR QUE EXISTE]
 * Detiene la maquina RUN de un J manteniendo el ultimo byte fisico confirmado.
 *
 * [QUIEN LA LLAMA]
 * stop_j_run(), stop_all_j_runs() y la ruta de error de tick().
 *
 * [CUANDO SE EJECUTA]
 * Cuando la app pide STOP, cuando una ruta decide interrumpir RUN o si CAN falla
 * durante RUN.
 *
 * [ENTRADAS]
 * Indice J base 0.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * running=false, next_due_ms=0 y revision.
 *
 * [CONCURRENCIA]
 * Requiere mutex tomado.
 *
 * [FLUJO ACURATEX]
 * Jx.STOP/fallo CAN -> RUN detenido -> HEAD_STATUS muestra JxR=0.
 *
 * [EQUIVALENCIA MCU]
 * Es parar una maquina periodica sin cambiar salidas ya confirmadas.
 *
 * [SI NO EXISTIERA]
 * El tick seguiria intentando enviar pasos RUN.
 */
static void app_head_state_stop_j_run_locked(int index)
{
    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        return;
    }

    s_state.j_running[index] = false;
    s_state.j_next_due_ms[index] = 0;
    app_head_state_bump_revision_locked(index);
}

/**
 * [POR QUE EXISTE]
 * Separa una accion `INSTANCIA.VERBO` en sus dos partes.
 *
 * [QUIEN LA LLAMA]
 * apply_successful_action() para reconocer Jx.CHn/ON_ALL/OFF_ALL.
 *
 * [CUANDO SE EJECUTA]
 * Al aplicar el estado final de una accion exitosa.
 *
 * [ENTRADAS]
 * Texto de accion y buffers de salida.
 *
 * [SALIDAS]
 * Devuelve true si habia punto y ambas partes caben.
 *
 * [ESTADO QUE MODIFICA]
 * Solo escribe en buffers locales del llamador.
 *
 * [CONCURRENCIA]
 * No toca estado global.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION_DONE -> partir `J1.CH2` -> actualizar estado J.
 *
 * [EQUIVALENCIA MCU]
 * Es parseo de un comando textual simple.
 *
 * [SI NO EXISTIERA]
 * La funcion de aplicacion no sabria que modulo ni que verbo actualizar.
 */
static bool app_head_state_split_action(const char *action,
                                        char *instance,
                                        size_t instance_size,
                                        char *verb,
                                        size_t verb_size)
{
    const char *dot = NULL;
    size_t left_len;

    if (action == NULL || instance == NULL || verb == NULL || instance_size == 0 || verb_size == 0) {
        return false;
    }

    dot = strchr(action, '.');
    if (dot == NULL || dot == action || dot[1] == '\0') {
        return false;
    }

    left_len = (size_t)(dot - action);
    if (left_len >= instance_size || strlen(dot + 1) >= verb_size) {
        return false;
    }

    memcpy(instance, action, left_len);
    instance[left_len] = '\0';
    strlcpy(verb, dot + 1, verb_size);
    return true;
}

/**
 * [POR QUE EXISTE]
 * Convierte `J1`, `J8`, etc. a indice base 0 validado.
 *
 * [QUIEN LA LLAMA]
 * apply_successful_action().
 *
 * [CUANDO SE EJECUTA]
 * Antes de tocar un arreglo J.
 *
 * [ENTRADAS]
 * Texto, prefijo esperado, maximo permitido y puntero de salida.
 *
 * [SALIDAS]
 * Devuelve true y escribe indice base 0 si el texto es valido.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado global.
 *
 * [CONCURRENCIA]
 * No usa mutex porque solo parsea texto.
 *
 * [FLUJO ACURATEX]
 * `J1` -> indice 0 -> arreglo j_physical_register[0].
 *
 * [EQUIVALENCIA MCU]
 * Es validar un numero de canal antes de indexar un arreglo.
 *
 * [SI NO EXISTIERA]
 * Podria indexarse fuera de J1..J8.
 */
static bool app_head_state_parse_index(const char *text,
                                       const char *prefix,
                                       int max_count,
                                       int *zero_based_index)
{
    const size_t prefix_len = strlen(prefix);
    const char *number_text = NULL;
    char *endptr = NULL;
    long value;

    if (text == NULL || zero_based_index == NULL || strncasecmp(text, prefix, prefix_len) != 0) {
        return false;
    }

    number_text = text + prefix_len;
    if (number_text[0] == '\0') {
        return false;
    }

    value = strtol(number_text, &endptr, 10);
    if (endptr == number_text || *endptr != '\0' || value <= 0 || value > max_count) {
        return false;
    }

    *zero_based_index = (int)value - 1;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Convierte verbos numerados como `CH2` al numero 2.
 *
 * [QUIEN LA LLAMA]
 * apply_successful_action().
 *
 * [CUANDO SE EJECUTA]
 * Cuando la accion podria ser Jx.CHy.
 *
 * [ENTRADAS]
 * Verbo, prefijo esperado, maximo y puntero de salida.
 *
 * [SALIDAS]
 * Devuelve true si el numero existe y esta en rango.
 *
 * [ESTADO QUE MODIFICA]
 * Solo escribe el numero de salida.
 *
 * [CONCURRENCIA]
 * No toca estado compartido.
 *
 * [FLUJO ACURATEX]
 * `CH2` -> numero 2 -> mascara 1 << 1.
 *
 * [EQUIVALENCIA MCU]
 * Es decodificar un bit pedido por texto.
 *
 * [SI NO EXISTIERA]
 * CHn no podria convertirse en una mascara de bit.
 */
static bool app_head_state_parse_numbered_verb(const char *verb,
                                               const char *prefix,
                                               int max_number,
                                               int *number)
{
    const size_t prefix_len = strlen(prefix);
    const char *number_text = NULL;
    char *endptr = NULL;
    long value;

    if (verb == NULL || number == NULL || strncasecmp(verb, prefix, prefix_len) != 0) {
        return false;
    }

    number_text = verb + prefix_len;
    if (number_text[0] == '\0') {
        return false;
    }

    value = strtol(number_text, &endptr, 10);
    if (endptr == number_text || *endptr != '\0' || value <= 0 || value > max_number) {
        return false;
    }

    *number = (int)value;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Inicializa el gestor de estado J y su mutex.
 *
 * [QUIEN LA LLAMA]
 * app_main() durante el arranque del firmware.
 *
 * [CUANDO SE EJECUTA]
 * Antes de crear/usar comandos de Cabezal.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * ESP_OK si queda inicializado; ESP_ERR_NO_MEM o ESP_ERR_TIMEOUT si falla.
 *
 * [ESTADO QUE MODIFICA]
 * Limpia s_state, deja J1..J8 con fisico 0xFF y RUN detenido.
 *
 * [CONCURRENCIA]
 * Crea el mutex FreeRTOS y toma portMAX_DELAY para inicializar sin carreras.
 *
 * [FLUJO ACURATEX]
 * Reset -> app_main -> J fisico FF/logico 00 -> comandos seguros.
 *
 * [EQUIVALENCIA MCU]
 * Es inicializar salidas shadow antes de aceptar comandos externos.
 *
 * [SI NO EXISTIERA]
 * El estado de J seria indeterminado y XX no tendria base confiable.
 */
esp_err_t app_head_state_manager_init(void)
{
    if (s_head_state_mutex == NULL) {
        // [FREERTOS] Mutex binario con propiedad de exclusion mutua para s_state.
        s_head_state_mutex = xSemaphoreCreateMutex();
        if (s_head_state_mutex == NULL) {
            return ESP_ERR_NO_MEM;
        }
    }

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return ESP_ERR_TIMEOUT;
    }

    // [C/C++] memset limpia toda la estructura antes de cargar defaults propios.
    memset(&s_state, 0, sizeof(s_state));
    for (int i = 0; i < APP_HEAD_STATE_MAX_J; ++i) {
        // [ACURATEX] Fuente de verdad inicial: fisico FF = logico 00.
        app_head_state_set_j_physical_locked(i, 0xFF);
        app_head_state_reset_j_run_locked(i);
    }

    for (int i = 0; i < APP_HEAD_STATE_MAX_YARN; ++i) {
        app_head_state_reset_cascade_locked(&s_state.yarn[i]);
    }

    for (int i = 0; i < APP_HEAD_STATE_MAX_STITCH; ++i) {
        app_head_state_reset_cascade_locked(&s_state.stitch[i]);
    }

    app_head_state_give_mutex();
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Lee el byte fisico actual de un modulo J.
 *
 * [QUIEN LA LLAMA]
 * app_head_parse_dynamic_j_action() antes de calcular el valor que reemplaza XX.
 *
 * [CUANDO SE EJECUTA]
 * Al recibir HEAD_ACTION|Jx.CHy con modulo habilitado.
 *
 * [ENTRADAS]
 * `instance` es J en base 1; `value` apunta al byte de salida.
 *
 * [SALIDAS]
 * true si copio el byte; false si el puntero/indice/mutex fallo.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Toma el mutex hasta copiar el byte.
 *
 * [FLUJO ACURATEX]
 * J1.CH2 -> leer fisico actual -> XOR bit -> candidate_physical.
 *
 * [EQUIVALENCIA MCU]
 * Es leer el shadow register de un puerto activo-bajo.
 *
 * [SI NO EXISTIERA]
 * El parser dinamico no podria alternar un canal manteniendo los demas bits.
 */
bool app_head_state_manager_get_j_physical_register(uint8_t instance, uint8_t *value)
{
    // [ACURATEX] El protocolo usa J1..J8, pero los arreglos C usan 0..7.
    int index = (int)instance - 1;

    if (value == NULL) {
        return false;
    }

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        app_head_state_give_mutex();
        return false;
    }

    *value = s_state.j_physical_register[index];
    app_head_state_give_mutex();
    return true;
}

/**
 * [POR QUE EXISTE]
 * Confirma el nuevo byte fisico despues de una transmision CAN exitosa.
 *
 * [QUIEN LA LLAMA]
 * app_head_execute_allowed_line() cuando `send` uso XX y CAN devolvio ESP_OK.
 *
 * [CUANDO SE EJECUTA]
 * Durante la tarea HEAD_ACTION, inmediatamente despues de enviar la trama.
 *
 * [ENTRADAS]
 * Instancia J base 1 y byte fisico confirmado.
 *
 * [SALIDAS]
 * true si actualizo el estado.
 *
 * [ESTADO QUE MODIFICA]
 * j_physical_register, j_logical_mask y j_revision.
 *
 * [CONCURRENCIA]
 * Protege la escritura con mutex.
 *
 * [FLUJO ACURATEX]
 * send 320 ... XX -> CAN OK -> commit -> HEAD_STATUS muestra nuevo byte.
 *
 * [EQUIVALENCIA MCU]
 * Es actualizar el latch software despues de que el bus acepto la salida.
 *
 * [SI NO EXISTIERA]
 * El siguiente Jx.CHy repetiria el mismo estado anterior.
 */
bool app_head_state_manager_commit_j_physical_register(uint8_t instance, uint8_t value)
{
    int index = (int)instance - 1;

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        app_head_state_give_mutex();
        return false;
    }

    app_head_state_set_j_physical_locked(index, value);
    app_head_state_bump_revision_locked(index);
    app_head_state_give_mutex();
    return true;
}

/**
 * [POR QUE EXISTE]
 * Entrega un snapshot del byte fisico y de la bandera RUN.
 *
 * [QUIEN LA LLAMA]
 * HEAD_STATUS, app_head_append_j_status_fields() y controles que dependen de
 * saber si RUN esta activo.
 *
 * [CUANDO SE EJECUTA]
 * Cuando se necesita publicar o decidir con base en el estado J actual.
 *
 * [ENTRADAS]
 * Instancia base 1 y punteros opcionales de salida.
 *
 * [SALIDAS]
 * true si el indice fue valido y al menos un dato pudo pedirse.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Toma mutex para que fisico y running pertenezcan al mismo instante.
 *
 * [FLUJO ACURATEX]
 * HEAD_STATUS -> J1=FE|J1R=0.
 *
 * [EQUIVALENCIA MCU]
 * Es tomar una fotografia atomica de variables compartidas.
 *
 * [SI NO EXISTIERA]
 * La app no podria ver estado J coherente.
 */
bool app_head_state_manager_get_j_status(uint8_t instance,
                                         uint8_t *physical_register,
                                         bool *running)
{
    int index = (int)instance - 1;

    if (physical_register == NULL && running == NULL) {
        return false;
    }

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        app_head_state_give_mutex();
        return false;
    }

    if (physical_register != NULL) {
        *physical_register = s_state.j_physical_register[index];
    }
    if (running != NULL) {
        *running = s_state.j_running[index];
    }

    app_head_state_give_mutex();
    return true;
}

/**
 * [POR QUE EXISTE]
 * Activa o actualiza la maquina RUN de un J.
 *
 * [QUIEN LA LLAMA]
 * app_head_handle_direct_j_action() para HEAD_ACTION|Jx.RUN.
 *
 * [CUANDO SE EJECUTA]
 * En el handler de comando, no dentro del TXT.
 *
 * [ENTRADAS]
 * Instancia base 1, bus CAN y tiempo inicial.
 *
 * [SALIDAS]
 * true si el modulo existe y el mutex pudo tomarse.
 *
 * [ESTADO QUE MODIFICA]
 * Si no corria, reinicia RUN; si ya corria, solo actualiza bus.
 *
 * [CONCURRENCIA]
 * Usa mutex porque tick() puede estar recorriendo RUN al mismo tiempo.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION|J1.RUN -> j_running[0]=true -> tick envia.
 *
 * [EQUIVALENCIA MCU]
 * Es habilitar una tarea periodica sobre un canal.
 *
 * [SI NO EXISTIERA]
 * RUN no podria ser iniciado desde la app.
 */
bool app_head_state_manager_start_j_run(uint8_t instance, int can_bus, uint32_t now_ms)
{
    int index = (int)instance - 1;

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        app_head_state_give_mutex();
        return false;
    }

    if (!s_state.j_running[index]) {
        app_head_state_start_j_run_locked(index, can_bus, now_ms);
    } else {
        // [ACURATEX] Si RUN ya existe, no reinicia la secuencia; solo corrige el
        // bus que usara el proximo tick.
        s_state.j_can_bus[index] = app_head_state_normalize_bus(can_bus);
    }

    app_head_state_give_mutex();
    return true;
}

/**
 * [POR QUE EXISTE]
 * Desactiva RUN de un J individual.
 *
 * [QUIEN LA LLAMA]
 * app_head_handle_direct_j_action(), app_head_run_action() y tick() en error.
 *
 * [CUANDO SE EJECUTA]
 * Al recibir Jx.STOP, cuando una ruta decide parar RUN, o tras fallo CAN de RUN.
 *
 * [ENTRADAS]
 * Instancia J base 1.
 *
 * [SALIDAS]
 * true si el indice fue valido y el mutex pudo tomarse.
 *
 * [ESTADO QUE MODIFICA]
 * Puede cambiar running=false y revision.
 *
 * [CONCURRENCIA]
 * Toma mutex para no competir con tick().
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION|J1.STOP -> RUN inactivo.
 *
 * [EQUIVALENCIA MCU]
 * Es apagar una maquina de estados.
 *
 * [SI NO EXISTIERA]
 * No habria forma directa de detener un RUN individual.
 */
bool app_head_state_manager_stop_j_run(uint8_t instance)
{
    int index = (int)instance - 1;

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_J) {
        app_head_state_give_mutex();
        return false;
    }

    if (s_state.j_running[index]) {
        app_head_state_stop_j_run_locked(index);
    }

    app_head_state_give_mutex();
    return true;
}

/**
 * [POR QUE EXISTE]
 * Detiene cualquier RUN J activo.
 *
 * [QUIEN LA LLAMA]
 * Rutinas de parada/cancelacion global del cabezal.
 *
 * [CUANDO SE EJECUTA]
 * Cuando el firmware debe cancelar actividad automatica J.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Cambia running=false en todos los J que estuvieran activos.
 *
 * [CONCURRENCIA]
 * Recorre el arreglo completo con mutex.
 *
 * [FLUJO ACURATEX]
 * Stop global -> J1..J8 sin RUN.
 *
 * [EQUIVALENCIA MCU]
 * Es una parada de emergencia de maquinas periodicas internas.
 *
 * [SI NO EXISTIERA]
 * Habria riesgo de dejar algun J corriendo al cancelar.
 */
void app_head_state_manager_stop_all_j_runs(void)
{
    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return;
    }

    for (int i = 0; i < APP_HEAD_STATE_MAX_J; ++i) {
        if (s_state.j_running[i]) {
            app_head_state_stop_j_run_locked(i);
        }
    }

    app_head_state_give_mutex();
}

// [ACURATEX] Tablas exactas del Arduino antiguo para Yarn y Stitch.
static const uint8_t YARN1_ADDR[8] = { 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F };
static const uint8_t YARN2_ADDR[8] = { 0x24, 0x25, 0x26, 0x27, 0x20, 0x21, 0x22, 0x23 };
static const uint8_t STITCH1_ADDR[4] = { 0x00, 0x01, 0x02, 0x05 };
static const uint8_t STITCH2_ADDR[4] = { 0x06, 0x07, 0x08, 0x0B };
static const uint8_t STITCH3_ADDR[4] = { 0x0C, 0x0D, 0x0E, 0x11 };
static const uint8_t STITCH4_ADDR[4] = { 0x12, 0x13, 0x14, 0x17 };

/**
 * [POR QUE EXISTE]
 * Ejecuta un paso de cascada Yarn/Stitch si su vencimiento ya llego.
 *
 * [QUIEN LA LLAMA]
 * app_head_state_manager_tick().
 */
static esp_err_t app_head_state_tick_cascade(app_head_state_manager_can_send_standard_fn_t can_send_standard,
                                             app_head_cascade_state_t *states,
                                             size_t state_count,
                                             int index,
                                             const uint8_t *addr_table,
                                             size_t addr_count,
                                             const char *kind,
                                             uint32_t now_ms)
{
    app_head_cascade_state_t step = {};
    uint8_t addr = 0;
    uint8_t value = 0;
    int bus = 1;
    esp_err_t tx_err = ESP_OK;

    if (can_send_standard == NULL || states == NULL || addr_table == NULL || kind == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return ESP_ERR_TIMEOUT;
    }

    if (index < 0 || index >= (int)state_count || !states[index].running) {
        app_head_state_give_mutex();
        return ESP_OK;
    }

    if ((int32_t)(now_ms - states[index].next_due_ms) < 0) {
        app_head_state_give_mutex();
        return ESP_OK;
    }

    step = states[index];
    app_head_state_give_mutex();

    if (step.p == 0 || step.p > addr_count) {
        if (!app_head_state_take_mutex(portMAX_DELAY)) {
            return ESP_ERR_TIMEOUT;
        }

        if (index >= 0 && index < (int)state_count && states[index].running && states[index].revision == step.revision) {
            app_head_state_stop_cascade_locked(&states[index]);
        }

        app_head_state_give_mutex();
        return ESP_ERR_INVALID_STATE;
    }

    addr = addr_table[step.p - 1];
    value = (step.phase == 0) ? 0x01 : 0x00;
    bus = app_head_state_normalize_bus(step.can_bus);

    {
        uint8_t data[3] = { 0x1E, addr, value };
        tx_err = can_send_standard(bus, 0x320, data, sizeof(data));
    }

    if (tx_err != ESP_OK) {
        ESP_LOGW(TAG,
                 "%s_RUN_TX_FAIL|IDX=%d|BUS=%d|CODE=%s",
                 kind,
                 index + 1,
                 bus,
                 esp_err_to_name(tx_err));

        if (!app_head_state_take_mutex(portMAX_DELAY)) {
            return tx_err;
        }

        if (index >= 0 && index < (int)state_count && states[index].running && states[index].revision == step.revision) {
            app_head_state_stop_cascade_locked(&states[index]);
        }

        app_head_state_give_mutex();
        return tx_err;
    }

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return ESP_ERR_TIMEOUT;
    }

    if (index >= 0 && index < (int)state_count && states[index].running && states[index].revision == step.revision) {
        uint8_t next_p = (uint8_t)(step.p + 1U);
        uint8_t next_phase = step.phase;

        if (next_p > (uint8_t)addr_count) {
            next_p = 1;
            next_phase = (uint8_t)(step.phase == 0 ? 1 : 0);
        }

        states[index].phase = next_phase;
        states[index].p = next_p;
        states[index].next_due_ms = now_ms + step.delay_ms;
        app_head_state_bump_cascade_revision_locked(&states[index]);
    }

    app_head_state_give_mutex();
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Inicia una cascada Yarn concreta.
 */
bool app_head_state_manager_start_yarn_run(uint8_t instance, int can_bus, uint32_t now_ms)
{
    int index = (int)instance - 1;

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_YARN) {
        app_head_state_give_mutex();
        return false;
    }

    app_head_state_start_cascade_locked(&s_state.yarn[index], can_bus, now_ms, 80);
    app_head_state_give_mutex();
    return true;
}

/**
 * [POR QUE EXISTE]
 * Detiene una cascada Yarn concreta.
 */
bool app_head_state_manager_stop_yarn_run(uint8_t instance)
{
    int index = (int)instance - 1;

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_YARN) {
        app_head_state_give_mutex();
        return false;
    }

    app_head_state_stop_cascade_locked(&s_state.yarn[index]);
    app_head_state_give_mutex();
    return true;
}

void app_head_state_manager_stop_all_yarn_runs(void)
{
    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return;
    }

    for (int i = 0; i < APP_HEAD_STATE_MAX_YARN; ++i) {
        app_head_state_stop_cascade_locked(&s_state.yarn[i]);
    }

    app_head_state_give_mutex();
}

bool app_head_state_manager_start_stitch_run(uint8_t instance, int can_bus, uint32_t now_ms)
{
    int index = (int)instance - 1;

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_STITCH) {
        app_head_state_give_mutex();
        return false;
    }

    app_head_state_start_cascade_locked(&s_state.stitch[index], can_bus, now_ms, 80);
    app_head_state_give_mutex();
    return true;
}

bool app_head_state_manager_stop_stitch_run(uint8_t instance)
{
    int index = (int)instance - 1;

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (index < 0 || index >= APP_HEAD_STATE_MAX_STITCH) {
        app_head_state_give_mutex();
        return false;
    }

    app_head_state_stop_cascade_locked(&s_state.stitch[index]);
    app_head_state_give_mutex();
    return true;
}

void app_head_state_manager_stop_all_stitch_runs(void)
{
    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return;
    }

    for (int i = 0; i < APP_HEAD_STATE_MAX_STITCH; ++i) {
        app_head_state_stop_cascade_locked(&s_state.stitch[i]);
    }

    app_head_state_give_mutex();
}

void app_head_state_manager_stop_all_motion(void)
{
    app_head_state_manager_stop_all_j_runs();
    app_head_state_manager_stop_all_yarn_runs();
    app_head_state_manager_stop_all_stitch_runs();
}

// [ACURATEX] Snapshot pequeno de un paso RUN. Se copia bajo mutex y luego se
// usa fuera del mutex para no bloquear a otras tareas durante el envio CAN.
typedef struct {
    bool turning_on;
    uint8_t bit;
    uint8_t physical_register;
    uint32_t next_due_ms;
    int can_bus;
    uint32_t revision;
    int index;
} app_head_j_run_step_t;

/**
 * [POR QUE EXISTE]
 * Ejecuta los pasos pendientes de las maquinas RUN J.
 *
 * [QUIEN LA LLAMA]
 * Una tarea periodica del firmware que atiende estado de cabezal.
 *
 * [CUANDO SE EJECUTA]
 * Repetidamente; `now_ms` decide si ya toca otro bit.
 *
 * [ENTRADAS]
 * Callback de envio CAN estandar y tiempo actual.
 *
 * [SALIDAS]
 * ESP_OK si no hubo fallos; propaga error CAN o timeout si ocurre.
 *
 * [ESTADO QUE MODIFICA]
 * En exito actualiza byte fisico, bit siguiente, direccion ON/OFF, proximo
 * vencimiento y revision. En fallo puede detener RUN.
 *
 * [CONCURRENCIA]
 * Copia pendientes con mutex, libera para enviar CAN y confirma solo si la
 * revision sigue igual; asi no sobrescribe un STOP o accion posterior.
 *
 * [FLUJO ACURATEX]
 * Jx.RUN -> tick -> frame 0x320 {0x1D, indice, byte} -> estado actualizado.
 *
 * [EQUIVALENCIA MCU]
 * Es una maquina de estados temporizada que alterna bits de un puerto.
 *
 * [SI NO EXISTIERA]
 * RUN no produciria las tramas CAN periodicas.
 */
esp_err_t app_head_state_manager_tick(app_head_state_manager_can_send_standard_fn_t can_send_standard,
                                      uint32_t now_ms)
{
    app_head_j_run_step_t pending[APP_HEAD_STATE_MAX_J];
    size_t pending_count = 0;
    esp_err_t result = ESP_OK;

    if (can_send_standard == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return ESP_ERR_TIMEOUT;
    }

    for (int i = 0; i < APP_HEAD_STATE_MAX_J; ++i) {
        if (!s_state.j_running[i]) {
            continue;
        }

        // [FREERTOS] Comparacion con resta signed para tolerar wrap-around de
        // contador en milisegundos.
        if ((int32_t)(now_ms - s_state.j_next_due_ms[i]) < 0) {
            continue;
        }

        if (pending_count >= APP_HEAD_STATE_MAX_J) {
            break;
        }

        // [CONCURRENCIA] Se copia todo lo necesario antes de liberar el mutex.
        pending[pending_count].turning_on = s_state.j_turning_on[i];
        pending[pending_count].bit = s_state.j_bit[i];
        pending[pending_count].physical_register = s_state.j_physical_register[i];
        pending[pending_count].next_due_ms = s_state.j_next_due_ms[i];
        pending[pending_count].can_bus = s_state.j_can_bus[i];
        pending[pending_count].revision = s_state.j_revision[i];
        pending[pending_count].index = i;
        pending_count++;
    }

    app_head_state_give_mutex();

    for (size_t i = 0; i < pending_count; ++i) {
        const app_head_j_run_step_t *step = &pending[i];
        if (FAST_PERF_LOG && step->index == 0) {
            int32_t delta_ms = (int32_t)(now_ms - step->next_due_ms);
            ESP_LOGI(TAG, "[FAST] J1 tick delta_ms=%ld", (long)delta_ms);
        }
        // [ACURATEX] `mask` selecciona el bit fisico del canal actual.
        uint8_t mask = (uint8_t)(1U << step->bit);
        // [ACURATEX] Activo-bajo: encender = poner bit fisico en 0; apagar =
        // poner bit fisico en 1.
        uint8_t candidate = step->turning_on
            ? (uint8_t)(step->physical_register & (uint8_t)~mask)
            : (uint8_t)(step->physical_register | mask);
        // [ACURATEX] Trama RUN fija: ID 0x320, comando 0x1D, indice J base 0 y
        // nuevo byte fisico candidato.
        uint8_t frame[3] = { 0x1D, (uint8_t)step->index, candidate };
        int bus = app_head_state_normalize_bus(step->can_bus);
        esp_err_t tx_err = can_send_standard(bus, 0x320, frame, sizeof(frame));

        if (tx_err != ESP_OK) {
            ESP_LOGW(TAG,
                     "J_RUN_TX_FAIL|J=%d|BUS=%d|CODE=%s",
                     step->index + 1,
                     bus,
                     esp_err_to_name(tx_err));

            if (!app_head_state_take_mutex(portMAX_DELAY)) {
                result = tx_err;
                continue;
            }

            if (step->index >= 0
                && step->index < APP_HEAD_STATE_MAX_J
                && s_state.j_running[step->index]
                && s_state.j_revision[step->index] == step->revision) {
                // [CONCURRENCIA] Solo se detiene si nadie cambio este J desde
                // que se tomo el snapshot.
                app_head_state_stop_j_run_locked(step->index);
            }

            app_head_state_give_mutex();
            result = tx_err;
            continue;
        }

        if (!app_head_state_take_mutex(portMAX_DELAY)) {
            result = ESP_ERR_TIMEOUT;
            continue;
        }

        if (step->index >= 0
            && step->index < APP_HEAD_STATE_MAX_J
            && s_state.j_running[step->index]
            && s_state.j_revision[step->index] == step->revision) {
            uint8_t next_bit = (uint8_t)(step->bit + 1U);
            bool next_turning_on = step->turning_on;

            if (next_bit >= 8U) {
                next_bit = 0;
                // [ACURATEX] Tras recorrer los 8 bits, RUN cambia de fase:
                // primero enciende todos de a uno, luego los apaga de a uno.
                next_turning_on = !step->turning_on;
            }

            // [ACURATEX] El commit se hace despues de CAN OK y revision valida.
            app_head_state_set_j_physical_locked(step->index, candidate);
            s_state.j_running[step->index] = true;
            s_state.j_bit[step->index] = next_bit;
            s_state.j_turning_on[step->index] = next_turning_on;
            // [ACURATEX] Mantiene el periodo original de 80 ms por paso.
            s_state.j_next_due_ms[step->index] = step->next_due_ms + 80U;
            app_head_state_bump_revision_locked(step->index);
        }

        app_head_state_give_mutex();
    }

    {
        const uint8_t *const yarn_tables[APP_HEAD_STATE_MAX_YARN] = {
            YARN1_ADDR,
            YARN2_ADDR,
        };
        for (int i = 0; i < APP_HEAD_STATE_MAX_YARN; ++i) {
            esp_err_t cascade_err = app_head_state_tick_cascade(can_send_standard,
                                                                 s_state.yarn,
                                                                 APP_HEAD_STATE_MAX_YARN,
                                                                 i,
                                                                 yarn_tables[i],
                                                                 8,
                                                                 "YARN",
                                                                 now_ms);
            if (cascade_err != ESP_OK) {
                result = cascade_err;
            }
        }
    }

    {
        const uint8_t *const stitch_tables[APP_HEAD_STATE_MAX_STITCH] = {
            STITCH1_ADDR,
            STITCH2_ADDR,
            STITCH3_ADDR,
            STITCH4_ADDR,
        };
        for (int i = 0; i < APP_HEAD_STATE_MAX_STITCH; ++i) {
            esp_err_t cascade_err = app_head_state_tick_cascade(can_send_standard,
                                                                 s_state.stitch,
                                                                 APP_HEAD_STATE_MAX_STITCH,
                                                                 i,
                                                                 stitch_tables[i],
                                                                 4,
                                                                 "STITCH",
                                                                 now_ms);
            if (cascade_err != ESP_OK) {
                result = cascade_err;
            }
        }
    }

    return result;
}

/**
 * [POR QUE EXISTE]
 * Aplica al estado J una accion completada por el flujo normal de TXT.
 *
 * [QUIEN LA LLAMA]
 * El runner de HEAD_ACTION cuando una accion termina correctamente.
 *
 * [CUANDO SE EJECUTA]
 * Despues de ejecutar BEGIN|accion ... END con resultado exitoso.
 *
 * [ENTRADAS]
 * Texto de accion, por ejemplo `J1.CH2`, `J1.ON_ALL`, `J1.OFF_ALL`.
 *
 * [SALIDAS]
 * true si reconocio la accion y cambio el estado.
 *
 * [ESTADO QUE MODIFICA]
 * Para CH alterna un bit; ON_ALL fuerza 0x00; OFF_ALL fuerza 0xFF.
 *
 * [CONCURRENCIA]
 * Toma el mutex mientras decide y cambia el byte.
 *
 * [FLUJO ACURATEX]
 * HEAD_ACTION_DONE -> aplicar estado -> HEAD_STATUS/427 reflejan el resultado.
 *
 * [EQUIVALENCIA MCU]
 * Es actualizar un registro espejo despues de que una rutina termino bien.
 *
 * [SI NO EXISTIERA]
 * Acciones J sin placeholder XX podrian terminar sin cambiar el estado interno.
 */
bool app_head_state_manager_apply_successful_action(const char *action)
{
    char instance[32];
    char verb[32];
    int index = -1;
    int number = 0;
    bool changed = false;

    if (action == NULL) {
        return false;
    }

    if (!app_head_state_split_action(action, instance, sizeof(instance), verb, sizeof(verb))) {
        return false;
    }

    if (!app_head_state_take_mutex(portMAX_DELAY)) {
        return false;
    }

    if (app_head_state_parse_index(instance, "J", APP_HEAD_STATE_MAX_J, &index)) {
        if (app_head_state_parse_numbered_verb(verb, "CH", APP_HEAD_STATE_MAX_J, &number)) {
            // [ACURATEX] CHn alterna el bit fisico n-1. Ejemplo J1.CH1:
            // FF ^ 01 = FE, que logicamente significa canal 1 activo.
            uint8_t mask = (uint8_t)(1U << (number - 1));
            app_head_state_set_j_physical_locked(index, (uint8_t)(s_state.j_physical_register[index] ^ mask));
            app_head_state_bump_revision_locked(index);
            changed = true;
        } else if (strcasecmp(verb, "ON_ALL") == 0) {
            // [ACURATEX] En activo-bajo, todos encendidos = todos los bits a 0.
            app_head_state_set_j_physical_locked(index, 0x00);
            app_head_state_bump_revision_locked(index);
            changed = true;
        } else if (strcasecmp(verb, "OFF_ALL") == 0) {
            // [ACURATEX] Todos apagados = todos los bits fisicos a 1.
            app_head_state_set_j_physical_locked(index, 0xFF);
            app_head_state_bump_revision_locked(index);
            changed = true;
        }
    }

    app_head_state_give_mutex();
    return changed;
}
