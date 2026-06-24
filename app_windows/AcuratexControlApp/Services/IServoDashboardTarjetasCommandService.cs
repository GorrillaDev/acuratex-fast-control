namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para separar la vista de servo modular de la forma exacta de
/// emitir comandos hacia el firmware.
///
/// [QUIÉN LA USA]
/// La usa `ServoDashboardTarjetas.razor`.
///
/// [CUÁNDO SE USA]
/// Se usa cuando el usuario cambia modo, init, salidas, frecuencias o posiciones.
///
/// [ENTRADAS]
/// Recibe textos de modo, flags, frecuencias, posiciones, vueltas y rutinas.
///
/// [SALIDAS]
/// Devuelve tareas asincronas.
///
/// [EFECTOS SECUNDARIOS]
/// Puede enviar lineas al firmware.
///
/// [FLUJO ACURATEX]
/// Razor -> interfaz -> servicio -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un driver que encapsula la trama de control.
///
/// [SI NO EXISTIERA]
/// La vista modular tendria que construir el protocolo por si misma.
/// </summary>
public interface IServoDashboardTarjetasCommandService
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
    /// Se parece a seleccionar el modo de un periférico.
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
    Task GoToPositionAsync(int positionNumber, decimal target, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para ajustar las vueltas asociadas a una posicion.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista cuando el operador cambia el numero de vueltas.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al editar el valor de vueltas.
    ///
    /// [ENTRADAS]
    /// Recibe la posicion, las vueltas y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar configuracion al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetTurnsAsync()` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar una constante de movimiento por posicion.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria fijar vueltas por separado.
    /// </summary>
    Task SetTurnsAsync(int positionNumber, int turns, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para aplicar una rutina compuesta.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista cuando el operador activa una rutina.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al confirmar una rutina con varios pasos.
    ///
    /// [ENTRADAS]
    /// Recibe si esta habilitada, la lista ordenada de posiciones y el nivel de velocidad.
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
    Task SetRoutineAsync(bool enabled, IReadOnlyList<int> orderedPositions, int speedLevel, CancellationToken cancellationToken = default);
}
