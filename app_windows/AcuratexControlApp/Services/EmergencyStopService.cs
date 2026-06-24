// [ACURATEX] Servicio que coordina la parada de emergencia, la auditoria y la reanudacion segura.
// [FLUJO] Tecla ESC o accion critica -> servicio -> estado interno -> comandos al firmware.
using System.Diagnostics;
using AcuratexControlApp.Services.Auth;

namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para guardar la auditoria de una accion de emergencia con todo su contexto.
///
/// [QUIÉN LO USA]
/// Lo usan la UI de diagnostico, el historial y las rutinas de seguridad.
///
/// [CUÁNDO SE USA]
/// Se usa cada vez que el servicio activa, libera o reconstruye el estado de emergencia.
///
/// [ENTRADAS]
/// Recibe fecha, usuario, rol, sistema, origen y mensaje.
///
/// [SALIDAS]
/// Devuelve una entrada de solo datos para el historial.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Seguridad -> `EmergencyStopAuditEntry` -> historial de auditoria.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una linea de log de seguridad guardada en memoria.
///
/// [SI NO EXISTIERA]
/// El historial no tendria una forma uniforme de registrar la causa.
/// </summary>
public sealed record EmergencyStopAuditEntry(
    DateTime TimestampLocal,
    string Username,
    string Role,
    string SystemName,
    string Origin,
    string Message);

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para centralizar el bloqueo de seguridad cuando el operador activa
/// una parada de emergencia o cuando la aplicacion necesita recuperarse de ella.
///
/// [QUIEN LA LLAMA]
/// La llaman atajos de teclado, botones de la UI, servicios internos y rutinas de arranque.
///
/// [CUANDO SE EJECUTA]
/// Se ejecuta cada vez que la app debe activar, liberar o reconstruir el estado de emergencia.
///
/// [ENTRADAS]
/// Recibe el origen de la orden y, en algunas rutas, un `CancellationToken`.
///
/// [SALIDAS]
/// Devuelve tareas asincronas cuando hay que notificar a otros componentes o mandar secuencias.
///
/// [EFECTOS SECUNDARIOS]
/// Cambia el estado de bloqueo, genera auditoria, dispara eventos y manda comandos al equipo.
///
/// [FLUJO ACURATEX]
/// UI -> `EmergencyStopService` -> conexion -> firmware -> actualizacion de estado.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una interrupcion de seguridad con una bandera global de paro.
///
/// [SI NO EXISTIERA]
/// Cada pantalla tendria que implementar su propio paro de emergencia y la seguridad seria inconsistente.
/// </summary>
public sealed class EmergencyStopService : IEmergencyStopService, IDisposable
{
    // [C#] `const` define literales fijos en tiempo de compilacion.
    // [ACURATEX] El firmware recibe este comando como inicio de la secuencia de paro.
    private const string EmergencyStopCommand = "emergency_stop";
    private const string EmergencyActivatedByEscMessage = "PARADA DE EMERGENCIA ACTIVADA POR ESC";
    private const string EmergencyClearedByEscMessage = "BLOQUEO DE EMERGENCIA DESACTIVADO POR ESC";
    private const string EmergencyResetByInitMessage = "RECUPERACION DE EMERGENCIA POR INIT";

    private static readonly string[] EmergencyStopSequence =
    {
        EmergencyStopCommand,
        "stop",
        "j_stop_all",
        "y_stop_all",
        "s_stop_all",
        "S2_ROUTINE|OFF",
        "S1_RUN|OFF",
        "S1_SON|OFF",
        "STEP_RUN|OFF",
        "STEP_REV|OFF",
        "S2_RUN|OFF",
        "S2_SON|OFF",
        "INIT|OFF"
    };

    // [C#] `object` se usa como candado de lectura/escritura.
    // [ACURATEX] Protege el estado activo de emergencia y la auditoria.
    private readonly object _stateGate = new();
    // [C#] `SemaphoreSlim` limita cuantas ejecuciones concurrentes entran en la seccion critica.
    // [ACURATEX] Evita que dos paradas criticas se disparen a la vez.
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly IConnectionController _connection;
    private readonly AuthStateService _authState;
    private readonly string _systemName;
    private readonly List<EmergencyStopAuditEntry> _auditTrail = new();
    private bool _isEmergencyStopActive;
    private bool _disposed;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// El constructor existe para recibir la conexion, la sesion activa y el nombre del sistema.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el contenedor de dependencias o el formulario anfitrion.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez al crear el servicio.
    ///
    /// [ENTRADAS]
    /// Recibe la conexion, la autenticacion y un nombre legible del sistema.
    ///
    /// [SALIDAS]
    /// Devuelve el servicio preparado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Normaliza el nombre del sistema y conserva dependencias.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `EmergencyStopService` -> seguridad lista.
    ///
    /// [EQUIVALENCIA MCU]
    /// Equivale a configurar registros y buffers antes del loop principal.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una unica autoridad para la parada de emergencia.
    /// </summary>
    public EmergencyStopService(IConnectionController connection, AuthStateService authState, string systemName)
    {
        _connection = connection;
        _authState = authState;
        _systemName = string.IsNullOrWhiteSpace(systemName) ? "Sistema" : systemName.Trim();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para que la UI sepa si el sistema está bloqueado por emergencia.
    ///
    /// [QUIÉN LA USA]
    /// La usan los paneles, los atajos de teclado y los servicios que necesitan decidir si
    /// una acción sigue permitida.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al pintar el estado de seguridad o antes de ejecutar comandos críticos.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si la emergencia sigue activa.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado interno -> `IsEmergencyStopActive` -> UI y lógica de seguridad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una bandera global de paro en un microcontrolador.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que inspeccionar el estado interno por otros medios.
    /// </summary>
    // [C#] Propiedad de solo lectura.
    // [ACURATEX] La UI consulta si el bloqueo sigue activo.
    public bool IsEmergencyStopActive
    {
        get
        {
            // [C#] `lock` protege una variable compartida frente a accesos simultaneos.
            lock (_stateGate) {
                return _isEmergencyStopActive;
            }
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para mostrar el último evento de auditoría de emergencia.
    ///
    /// [QUIÉN LA USA]
    /// La usan la UI, diagnósticos y registro visual.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta después de activar, limpiar o reiniciar la emergencia.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la última entrada de auditoría o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Auditoría -> `LastAuditEntry` -> pantalla de estado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al último diagnóstico registrado en una bitácora de fallo.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría mostrar la causa más reciente de la emergencia.
    /// </summary>
    // [C#] `?` permite no tener auditoria aun.
    // [ACURATEX] La pantalla puede mostrar el ultimo evento de seguridad.
    public EmergencyStopAuditEntry? LastAuditEntry
    {
        get
        {
            // [ACURATEX] La ultima entrada sirve para mostrar al operador la causa mas reciente.
            lock (_stateGate) {
                return _auditTrail.Count == 0 ? null : _auditTrail[^1];
            }
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar que cambió el estado de emergencia.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan pantallas, atajos y otros servicios que necesitan repintar.
    ///
    /// [CUÁNDO SE USA]
    /// Se dispara al activar, liberar o reiniciar la emergencia.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Despierta a suscriptores para que actualicen la interfaz.
    ///
    /// [FLUJO ACURATEX]
    /// Cambio de seguridad -> `StateChanged` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una señal de interrupción que fuerza refresco.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría cuándo cambió el bloqueo de emergencia.
    /// </summary>
    // [C#] `event` desacopla el emisor del receptor.
    // [ACURATEX] La interfaz se entera cuando cambia el bloqueo.
    public event Action? StateChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para notificar a módulos externos que la parada se disparó.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan componentes que reaccionan de forma asíncrona a la emergencia.
    ///
    /// [CUÁNDO SE USA]
    /// Se invoca después de activar el paro.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea mediante el `Func<Task>` suscrito.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Lanza callbacks de emergencia.
    ///
    /// [FLUJO ACURATEX]
    /// Emergencia -> `EmergencyStopTriggered` -> respuestas asíncronas.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una serie de handlers que reaccionan a un evento crítico.
    ///
    /// [SI NO EXISTIERA]
    /// No habría un aviso asíncrono centralizado de paro activado.
    /// </summary>
    // [C#] `Func<Task>` modela callbacks asincronos.
    // [ACURATEX] Permite avisar a otros modulos cuando la parada se dispara.
    public event Func<Task>? EmergencyStopTriggered;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para alternar el paro de emergencia sin que el llamador
    /// tenga que decidir si activa o limpia el bloqueo.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman la tecla ESC, botones de seguridad o logica interna.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el operador pulsa la accion de emergencia.
    ///
    /// [ENTRADAS]
    /// Recibe el origen del evento y un token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea que termina al activar o liberar el bloqueo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede activar o limpiar la emergencia y disparar otros avisos.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> alternar emergencia -> servicio -> firmware/estado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Es parecido a cambiar una bandera de emergencia desde una ISR o callback.
    ///
    /// [SI NO EXISTIERA]
    /// Tendriamos dos rutas separadas para activar y liberar, con mas riesgo de duplicacion.
    /// </summary>
    public async Task ToggleEmergencyStopAsync(string origin = "ESC", CancellationToken cancellationToken = default)
    {
        if (IsEmergencyStopActive) {
            await ClearEmergencyStopLockAsync(origin, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteEmergencyStopAsync(origin, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para forzar la activacion de la parada y registrar la causa.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman rutas de seguridad y el alternador cuando debe pasar a estado activo.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al detectar una condicion critica o al pulsar la orden de paro.
    ///
    /// [ENTRADAS]
    /// Recibe el origen y un `CancellationToken`.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea asincrona.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Marca el bloqueo activo, agrega auditoria, dispara eventos y manda comandos.
    ///
    /// [FLUJO ACURATEX]
    /// Evento critico -> activacion -> auditoria -> secuencia al firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Equivale a elevar una linea de paro y registrar la causa en RAM.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una manera explicita de entrar al estado de emergencia.
    /// </summary>
    public async Task ExecuteEmergencyStopAsync(string origin = "ESCAPE", CancellationToken cancellationToken = default)
    {
        if (_disposed) {
            return;
        }

        bool activated;
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            lock (_stateGate) {
                if (_disposed || _isEmergencyStopActive) {
                    return;
                }

                _isEmergencyStopActive = true;
                AddAuditEntryLocked(CreateAuditEntry(origin, BuildActivationMessage(origin)));
                activated = true;
            }
        } finally {
            _executionGate.Release();
        }

        if (!activated) {
            return;
        }

        NotifyStateChanged();
        await NotifyEmergencyHandlersAsync().ConfigureAwait(false);
        await SendEmergencyStopToDeviceAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para liberar el bloqueo sin volver a ejecutar la secuencia de paro.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman la tecla ESC en modo desbloqueo y la logica de recuperacion.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el operador confirma que puede salir del modo de emergencia.
    ///
    /// [ENTRADAS]
    /// Recibe la fuente del desbloqueo y un token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desactiva el bloqueo y notifica a la UI.
    ///
    /// [FLUJO ACURATEX]
    /// Desbloqueo -> limpieza de estado -> actualizacion visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a borrar una bandera de fallo cuando el operador la reconoce.
    ///
    /// [SI NO EXISTIERA]
    /// No podria salir del estado de paro sin reiniciar toda la aplicacion.
    /// </summary>
    public async Task ClearEmergencyStopLockAsync(string source = "ESC", CancellationToken cancellationToken = default)
    {
        if (_disposed) {
            return;
        }

        bool changed = false;
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            lock (_stateGate) {
                if (_disposed || !_isEmergencyStopActive) {
                    return;
                }

                _isEmergencyStopActive = false;
                AddAuditEntryLocked(CreateAuditEntry(source, BuildClearMessage(source)));
                changed = true;
            }
        } finally {
            _executionGate.Release();
        }

        if (changed) {
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para reconstruir el estado de paro durante el arranque.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el codigo de inicio o una recuperacion interna.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al iniciar la app o al restaurar el estado persistido.
    ///
    /// [ENTRADAS]
    /// Recibe el origen de la recuperacion.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Limpia el bloqueo y agrega un registro de auditoria.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> reset de emergencia -> estado limpio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reiniciar una maquina de estados de seguridad.
    ///
    /// [SI NO EXISTIERA]
    /// La aplicacion podria arrancar creyendo que sigue en emergencia.
    /// </summary>
    public void ResetEmergencyStop(string origin = "INIT")
    {
        bool changed = false;
        _executionGate.Wait();
        try {
            lock (_stateGate) {
                if (_disposed || !_isEmergencyStopActive) {
                    return;
                }

                _isEmergencyStopActive = false;
                AddAuditEntryLocked(CreateAuditEntry(origin, BuildResetMessage(origin)));
                changed = true;
            }
        } finally {
            _executionGate.Release();
        }

        if (changed) {
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este método existe para dejar el servicio en un estado descartado y liberar recursos.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama el contenedor o el formulario anfitrión al cerrar la app.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el servicio ya no se necesita.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Marca el servicio como descartado y libera el semáforo.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre -> `Dispose()` -> limpieza de seguridad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar un módulo de seguridad y liberar sus recursos.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio podría quedar vivo después del cierre de la app.
    /// </summary>
    public void Dispose()
    {
        // [ACURATEX] Al destruirse el servicio ya no debe aceptar cambios de estado.
        lock (_stateGate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
        }

        _executionGate.Dispose();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para construir el mensaje que se guarda al activar la parada.
    ///
    /// [QUIÉN LA USA]
    /// La usan las rutas de activación de emergencia.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al registrar la auditoría de activación.
    ///
    /// [ENTRADAS]
    /// Recibe el origen.
    ///
    /// [SALIDAS]
    /// Devuelve un texto de auditoría.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Origen -> `BuildActivationMessage()` -> auditoría.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una causa de fallo en una bitácora de sistema.
    ///
    /// [SI NO EXISTIERA]
    /// La auditoría no tendría una descripción consistente al activar la parada.
    /// </summary>
    // [ACURATEX] Construye el mensaje de auditoria cuando se activa la parada.
    // [FLUJO] El mensaje cambia segun si el origen fue ESC o una ruta interna.
    private static string BuildActivationMessage(string origin)
    {
        return IsEscSource(origin)
            ? EmergencyActivatedByEscMessage
            : "PARADA DE EMERGENCIA ACTIVADA";
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para construir el mensaje de auditoría al liberar el bloqueo.
    ///
    /// [QUIÉN LA USA]
    /// La usan las rutas de desbloqueo.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando se limpia la emergencia.
    ///
    /// [ENTRADAS]
    /// Recibe el origen del desbloqueo.
    ///
    /// [SALIDAS]
    /// Devuelve un texto de auditoría.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Origen -> `BuildClearMessage()` -> auditoría.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a registrar que una bandera de seguridad fue liberada.
    ///
    /// [SI NO EXISTIERA]
    /// No habría texto uniforme para la liberación de emergencia.
    /// </summary>
    // [ACURATEX] Construye el mensaje de auditoria al liberar el bloqueo.
    private static string BuildClearMessage(string source)
    {
        return IsEscSource(source)
            ? EmergencyClearedByEscMessage
            : "BLOQUEO DE EMERGENCIA DESACTIVADO";
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para construir el mensaje usado durante el reset de arranque.
    ///
    /// [QUIÉN LA USA]
    /// La usan las rutas de recuperación.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al reconstruir el estado durante el inicio.
    ///
    /// [ENTRADAS]
    /// Recibe el origen.
    ///
    /// [SALIDAS]
    /// Devuelve un mensaje de auditoría.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Origen -> `BuildResetMessage()` -> bitácora de inicio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a marcar en RAM que el sistema salió de un reset seguro.
    ///
    /// [SI NO EXISTIERA]
    /// El arranque no podría registrar claramente la recuperación de emergencia.
    /// </summary>
    // [ACURATEX] Construye el mensaje para el reset de arranque.
    private static string BuildResetMessage(string origin)
    {
        if (string.Equals(origin?.Trim(), "INIT", StringComparison.OrdinalIgnoreCase)) {
            return EmergencyResetByInitMessage;
        }

        return "RECUPERACION DE EMERGENCIA";
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para reconocer si el origen textual representa la tecla ESC.
    ///
    /// [QUIÉN LA USA]
    /// La usan las funciones de mensaje y el alternador de emergencia.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al decidir qué texto de auditoría usar.
    ///
    /// [ENTRADAS]
    /// Recibe el origen textual.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el origen es ESC o ESCAPE.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Origen -> `IsEscSource()` -> texto de auditoría.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a decodificar una tecla de emergencia en un panel físico.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no distinguiría entre ESC y otros orígenes.
    /// </summary>
    // [ACURATEX] Detecta si el origen textual viene de ESC o ESCAPE.
    private static bool IsEscSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) {
            return false;
        }

        string cleanSource = source.Trim();
        return string.Equals(cleanSource, "ESC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cleanSource, "ESCAPE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para ejecutar todos los handlers asíncronos de emergencia sin
    /// cortar la notificación si uno falla.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ExecuteEmergencyStopAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta después de activar la emergencia.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Invoca cada callback registrado y registra errores de forma no fatal.
    ///
    /// [FLUJO ACURATEX]
    /// Emergencia -> `NotifyEmergencyHandlersAsync()` -> callbacks.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a barrer una lista de callbacks de seguridad en un sistema embebido.
    ///
    /// [SI NO EXISTIERA]
    /// Un handler roto impediría avisar al resto.
    /// </summary>
    // [ACURATEX] Recorre los handlers asincronos sin romper la ejecucion si uno falla.
    private async Task NotifyEmergencyHandlersAsync()
    {
        // [C#] `GetInvocationList()` extrae cada suscriptor del evento para llamarlo uno por uno.
        // [ACURATEX] Esto evita que un handler roto impida notificar al resto.
        Func<Task>? handlers = EmergencyStopTriggered;
        if (handlers == null) {
            return;
        }

        foreach (Func<Task> handler in handlers.GetInvocationList().Cast<Func<Task>>()) {
            try {
                await handler().ConfigureAwait(false);
            } catch (Exception ex) {
                Trace.WriteLine($"[EmergencyStop] handler error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para mandar al firmware la secuencia de parada de emergencia.
    ///
    /// [QUIÉN LA USA]
    /// La usa la ruta de activación de emergencia.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta después de marcar la emergencia local.
    ///
    /// [ENTRADAS]
    /// Recibe un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía múltiples líneas al firmware, si hay conexión.
    ///
    /// [FLUJO ACURATEX]
    /// Emergencia -> `SendEmergencyStopToDeviceAsync()` -> comandos al firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a forzar varias salidas seguras en orden predefinido.
    ///
    /// [SI NO EXISTIERA]
    /// La app solo bloquearía la UI, sin avisar al firmware.
    /// </summary>
    // [ACURATEX] Manda al firmware una secuencia de lineas de paro.
    private async Task SendEmergencyStopToDeviceAsync(CancellationToken cancellationToken)
    {
        // [FLUJO] Si no hay conexion, el servicio conserva el estado local y evita fallos de envio.
        // [ACURATEX] La seguridad local sigue activa aunque el firmware no responda.
        if (!_connection.IsConnected) {
            return;
        }

        foreach (string line in EmergencyStopSequence) {
            try {
                await _connection.SendLineAsync(line, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                Trace.WriteLine($"[EmergencyStop] send '{line}' failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para crear una entrada de auditoría con contexto de sesión.
    ///
    /// [QUIÉN LA USA]
    /// La usan las rutas de seguridad antes de guardar eventos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al crear una entrada de historial.
    ///
    /// [ENTRADAS]
    /// Recibe origen y mensaje.
    ///
    /// [SALIDAS]
    /// Devuelve una entrada de auditoría lista para guardar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Contexto actual -> `CreateAuditEntry()` -> bitácora.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a registrar en memoria quién disparó un evento de seguridad.
    ///
    /// [SI NO EXISTIERA]
    /// La auditoría no tendría un formato uniforme.
    /// </summary>
    // [ACURATEX] Fabrica una linea de auditoria con contexto de sesion.
    private EmergencyStopAuditEntry CreateAuditEntry(string origin, string message)
    {
        string username = _authState.CurrentUser?.Username ?? "sin-sesion";
        string role = _authState.CurrentRole?.Name ?? "sin-rol";
        string cleanOrigin = string.IsNullOrWhiteSpace(origin) ? "DESCONOCIDO" : origin.Trim().ToUpperInvariant();

        return new EmergencyStopAuditEntry(
            DateTime.Now,
            username,
            role,
            _systemName,
            cleanOrigin,
            message);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para agregar una entrada al historial protegido por candado.
    ///
    /// [QUIÉN LA USA]
    /// La usan las rutas de auditoría del servicio.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al registrar cada cambio de seguridad.
    ///
    /// [ENTRADAS]
    /// Recibe una entrada de auditoría.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Agrega al historial, recorta entradas viejas y escribe en `Trace`.
    ///
    /// [FLUJO ACURATEX]
    /// Entrada -> `AddAuditEntryLocked()` -> historial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un log circular en RAM.
    ///
    /// [SI NO EXISTIERA]
    /// El historial no se mantendría acotado ni centralizado.
    /// </summary>
    // [ACURATEX] Añade el evento al historial bajo el candado de estado.
    private void AddAuditEntryLocked(EmergencyStopAuditEntry entry)
    {
        _auditTrail.Add(entry);
        if (_auditTrail.Count > 200) {
            _auditTrail.RemoveRange(0, _auditTrail.Count - 200);
        }

        Trace.WriteLine(
            $"[EmergencyStop][{entry.TimestampLocal:yyyy-MM-dd HH:mm:ss}] user={entry.Username} role={entry.Role} system={entry.SystemName} origin={entry.Origin} msg={entry.Message}");
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para avisar a la UI que el estado de emergencia cambió.
    ///
    /// [QUIÉN LA USA]
    /// La usan las rutas de activación, liberación y reset.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta después de modificar el estado interno.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara `StateChanged`.
    ///
    /// [FLUJO ACURATEX]
    /// Estado de seguridad -> `NotifyStateChanged()` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a levantar una interrupción de refresco de pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no sabría cuándo actualizar el estado de paro.
    /// </summary>
    // [ACURATEX] Notifica a la UI que el estado de seguridad cambio.
    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}
