namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para centralizar la logica de bloqueo por emergencia.
///
/// [QUIÉN LA USA]
/// La usan la UI, los atajos de teclado y servicios de seguridad.
///
/// [CUÁNDO SE USA]
/// Se usa cuando el operador activa, libera o consulta la parada.
///
/// [ENTRADAS]
/// Expone origen de la orden, cancelacion y estado interno.
///
/// [SALIDAS]
/// Devuelve tareas asincronas y estado de bloqueo.
///
/// [EFECTOS SECUNDARIOS]
/// Puede escribir auditorias, cambiar flags y notificar a la UI.
///
/// [FLUJO ACURATEX]
/// UI -> `IEmergencyStopService` -> estado de seguridad -> firmware o bloqueo.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una maquina de estados de seguridad con bandera global de paro.
///
/// [SI NO EXISTIERA]
/// Cada pantalla implementaria su propio criterio de seguridad.
/// </summary>
public interface IEmergencyStopService
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para que la UI sepa si el equipo quedo bloqueado por una parada.
    ///
    /// [QUIÉN LA USA]
    /// La usan paneles visuales y botones de accion.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta durante el render o antes de permitir acciones.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si la parada esta activa.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado de seguridad -> `IsEmergencyStopActive` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un flag de paro en un registro de estado.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no sabria si seguir bloqueando operaciones.
    /// </summary>
    bool IsEmergencyStopActive { get; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para conservar la ultima auditoria de seguridad visible.
    ///
    /// [QUIÉN LA USA]
    /// La usan la UI y herramientas de diagnostico.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cuando interesa mostrar el ultimo evento.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la ultima entrada de auditoria o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `LastAuditEntry` -> diagnostico.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un buffer que recuerda la ultima alarma.
    ///
    /// [SI NO EXISTIERA]
    /// El usuario no veria el ultimo contexto de la parada.
    /// </summary>
    EmergencyStopAuditEntry? LastAuditEntry { get; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar que cambio el estado de bloqueo.
    ///
    /// [QUIÉN LO USA]
    /// Lo suscriben la UI y los paneles de seguridad.
    ///
    /// [CUÁNDO SE USA]
    /// Se dispara al activar o liberar el paro.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Provoca repintado de la interfaz.
    ///
    /// [FLUJO ACURATEX]
    /// Estado de emergencia -> `StateChanged` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de cambio de estado.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no sabria que el bloqueo cambio.
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para permitir reacciones asincronas cuando la parada se activa.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan servicios de seguridad y rutinas que deben ejecutar limpieza.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando la parada se activa de manera efectiva.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea asincrona a cada suscriptor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede disparar acciones de mitigacion o limpieza.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `EmergencyStopTriggered` -> callbacks de seguridad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un callback de interrupcion critica.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una señal comun para reaccionar a la emergencia.
    /// </summary>
    event Func<Task>? EmergencyStopTriggered;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para alternar el estado de emergencia sin decidirlo en la UI.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la tecla ESC, un boton o una accion equivalente.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el operador pide cambiar el estado de bloqueo.
    ///
    /// [ENTRADAS]
    /// Recibe el origen y un `CancellationToken`.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia el estado interno, escribe auditoria y notifica a suscriptores.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ToggleEmergencyStopAsync()` -> servicio de emergencia.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a conmutar una bandera global de paro.
    ///
    /// [SI NO EXISTIERA]
    /// Habria que separar la activacion y liberacion en rutas distintas.
    /// </summary>
    Task ToggleEmergencyStopAsync(string origin = "ESC", CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para forzar la activacion de la parada de forma explicita.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman rutas criticas y acciones internas de seguridad.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la aplicacion necesita detenerse de inmediato.
    ///
    /// [ENTRADAS]
    /// Recibe el origen y un `CancellationToken`.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Activa el estado de emergencia y registra auditoria.
    ///
    /// [FLUJO ACURATEX]
    /// Evento critico -> `ExecuteEmergencyStopAsync()` -> estado -> bloqueo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Equivale a escribir un bit de stop en un registro de seguridad.
    ///
    /// [SI NO EXISTIERA]
    /// La parada solo podria alternarse, no activarse de forma directa.
    /// </summary>
    Task ExecuteEmergencyStopAsync(string origin = "ESCAPE", CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para limpiar el bloqueo sin reactivar la parada.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman la recuperacion manual y los flujos de salida del estado seguro.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el operador confirma que puede volver a operar.
    ///
    /// [ENTRADAS]
    /// Recibe la fuente de la orden y un `CancellationToken`.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desactiva el bloqueo y avisa a la UI.
    ///
    /// [FLUJO ACURATEX]
    /// Operador -> liberar emergencia -> servicio -> actualización de pantalla.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a borrar una bandera de fallo latente.
    ///
    /// [SI NO EXISTIERA]
    /// La unica forma de salir del estado de emergencia seria reiniciar la sesion.
    /// </summary>
    Task ClearEmergencyStopLockAsync(string source = "ESC", CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para reconstruir el estado tras iniciar la aplicacion o
    /// despues de una restauracion interna.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman el arranque y los reinicios logicos del sistema.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al inicializar la aplicacion o al restablecer la condicion.
    ///
    /// [ENTRADAS]
    /// Recibe el origen de la restauracion.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Limpia el bloqueo activo y agrega una auditoria.
    ///
    /// [FLUJO ACURATEX]
    /// Inicializacion -> `ResetEmergencyStop()` -> estado limpio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Equivale a resetear una maquina de estados de seguridad.
    ///
    /// [SI NO EXISTIERA]
    /// El arranque podria heredar un bloqueo viejo aunque el equipo ya este libre.
    /// </summary>
    void ResetEmergencyStop(string origin = "INIT");
}
