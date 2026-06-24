namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para separar la vista de servo unificado de la forma exacta de emitir comandos.
///
/// [QUIÉN LA USA]
/// La usa `ServoDashboardUnificado.razor`.
///
/// [CUÁNDO SE USA]
/// Se usa cada vez que un boton o control cambia un valor del tablero.
///
/// [ENTRADAS]
/// Recibe modos, flags, salidas, frecuencias, posiciones y rutinas.
///
/// [SALIDAS]
/// Devuelve tareas asincronas porque el envio al firmware puede tardar.
///
/// [EFECTOS SECUNDARIOS]
/// Puede mandar lineas de control al tester.
///
/// [FLUJO ACURATEX]
/// Razor -> interfaz -> servicio -> ConnectionController -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un driver con funciones de alto nivel para varios canales.
///
/// [SI NO EXISTIERA]
/// La vista tendria que construir el protocolo de servo por su cuenta.
/// </summary>
public interface IServoDashboardUnificadoCommandService
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para cambiar el modo de operacion del tablero servo.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista cuando el operador cambia de modo.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al elegir un modo nuevo.
    ///
    /// [ENTRADAS]
    /// Recibe el modo textual y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar una linea de control al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetModeAsync()` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a seleccionar el modo de un periferico.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que conocer el formato exacto de la linea de modo.
    /// </summary>
    Task SetModeAsync(string mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para activar o desactivar INIT.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista cuando el usuario pulsa INIT.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al cambiar el estado INIT.
    ///
    /// [ENTRADAS]
    /// Recibe el estado deseado y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede enviar una orden de inicializacion al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetInitAsync()` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a habilitar una fase de inicializacion en una maquina de estados.
    ///
    /// [SI NO EXISTIERA]
    /// INIT tendria que manejarse con una ruta separada en la UI.
    /// </summary>
    Task SetInitAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para habilitar o deshabilitar una salida concreta.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista cuando el usuario cambia un canal de salida.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al tocar un switch de salida.
    ///
    /// [ENTRADAS]
    /// Recibe la clave del canal, el estado y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar una orden de salida al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetOutputAsync()` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un bit de salida en un puerto.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria alterar una salida individual desde el servicio.
    /// </summary>
    Task SetOutputAsync(string key, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para ajustar una frecuencia asociada a un canal.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista cuando el operador cambia la frecuencia.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al editar un valor de frecuencia.
    ///
    /// [ENTRADAS]
    /// Recibe la clave del canal, la frecuencia y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar la frecuencia al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetFrequencyAsync()` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reprogramar el divisor de frecuencia de un periferico.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que codificar la frecuencia directamente.
    /// </summary>
    Task SetFrequencyAsync(string key, int frequencyHz, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para ordenar un movimiento a una posicion concreta.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista de posiciones del tablero servo.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario pide ir a una posicion.
    ///
    /// [ENTRADAS]
    /// Recibe el numero de posicion, el valor objetivo y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede generar una orden de movimiento al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `GoToPositionAsync()` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir el setpoint de un control de posicion.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que construir la orden de posicion por su cuenta.
    /// </summary>
    Task GoToPositionAsync(int positionNumber, decimal target, int turns, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para aplicar una rutina compuesta de pasos.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista cuando el operador activa una rutina.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al confirmar una rutina con varios pasos.
    ///
    /// [ENTRADAS]
    /// Recibe la rutina completa y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar una secuencia completa al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetRoutineAsync()` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a ejecutar una secuencia pregrabada de pasos.
    ///
    /// [SI NO EXISTIERA]
    /// La rutina deberia construirse con varias llamadas individuales.
    /// </summary>
    Task SetRoutineAsync(ServoDashboardUnificadoRoutineCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para describir una rutina compuesta enviada al tablero servo unificado.
///
/// [QUIÉN LO USA]
/// Lo usa el servicio unificado para agrupar pasos y velocidad.
///
/// [CUÁNDO SE USA]
/// Se usa al enviar una rutina completa.
///
/// [ENTRADAS]
/// Recibe habilitacion, nivel de velocidad y lista de pasos.
///
/// [SALIDAS]
/// Devuelve una estructura de solo datos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// UI -> `ServoDashboardUnificadoRoutineCommand` -> servicio.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una secuencia de acciones prearmada en firmware.
///
/// [SI NO EXISTIERA]
/// La rutina tendria que enviarse con parametros sueltos.
/// </summary>
public sealed record ServoDashboardUnificadoRoutineCommand(
    bool Enabled,
    int SpeedLevel,
    IReadOnlyList<ServoDashboardUnificadoRoutineStep> Steps);

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para describir un paso individual dentro de una rutina servo.
///
/// [QUIÉN LO USA]
/// Lo usa el record de rutina y el servicio unificado.
///
/// [CUÁNDO SE USA]
/// Se usa como parte de una lista de pasos ordenados.
///
/// [ENTRADAS]
/// Recibe numero de posicion, objetivo y vueltas.
///
/// [SALIDAS]
/// Devuelve un paso de solo datos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Rutina -> `ServoDashboardUnificadoRoutineStep` -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un punto de una secuencia de posicion.
///
/// [SI NO EXISTIERA]
/// La rutina perderia la descripcion de cada paso.
/// </summary>
public sealed record ServoDashboardUnificadoRoutineStep(
    int PositionNumber,
    decimal Target,
    int Turns);
