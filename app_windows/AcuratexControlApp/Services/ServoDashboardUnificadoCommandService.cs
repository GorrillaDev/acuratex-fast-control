// [ACURATEX] Emite las lineas de control del tablero servo del sistema unificado.
using System.Globalization;

namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para traducir operaciones de la UI de servo unificado en lineas concretas.
///
/// [QUIEN LA USA]
/// La usa `ServoDashboardUnificado.razor`.
///
/// [CUANDO SE USA]
/// Se ejecuta cuando la UI cambia modo, frecuencia, posicion o rutina.
///
/// [ENTRADAS]
/// Recibe textos de modo, flags, frecuencias, posiciones y rutinas compuestas.
///
/// [SALIDAS]
/// Devuelve tareas de envio al firmware.
///
/// [EFECTOS SECUNDARIOS]
/// Puede mandar una linea por cada cambio.
///
/// [FLUJO ACURATEX]
/// UI -> servicio -> texto de comando -> ConnectionController -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una capa de driver que formatea registros y tramas antes de salir.
///
/// [SI NO EXISTIERA]
/// La vista tendria que conocer cada comando textual del servo.
/// </summary>
public sealed class ServoDashboardUnificadoCommandService : IServoDashboardUnificadoCommandService
{
    // [ACURATEX] Conexion unica que transporta las lineas al firmware.
    private readonly IConnectionController _connection;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// El constructor existe para recibir la conexion que realmente manda las lineas.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el contenedor de dependencias.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al crear el servicio.
    ///
    /// [ENTRADAS]
    /// Recibe el controlador de conexion.
    ///
    /// [SALIDAS]
    /// Devuelve el servicio listo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda la referencia al transporte.
    ///
    /// [FLUJO ACURATEX]
    /// DI -> servicio servo -> conexion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a guardar el driver de UART/CAN que emitira las tramas.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no podria mandar ningun comando.
    /// </summary>
    public ServoDashboardUnificadoCommandService(IConnectionController connection)
    {
        _connection = connection;
    }

    // [ACURATEX] Cambia el modo del tablero servo.
    public Task SetModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        return SendLineAsync($"MODE|{mode}", cancellationToken);
    }

    // [ACURATEX] Activa o desactiva INIT.
    public Task SetInitAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return SendLineAsync($"INIT|{StateOnOff(enabled)}", cancellationToken);
    }

    // [ACURATEX] Traduce llaves de salida a lineas concretas del firmware.
    public Task SetOutputAsync(string key, bool enabled, CancellationToken cancellationToken = default)
    {
        string line = key switch
        {
            "s1-son" => $"S1_SON|{StateOnOff(enabled)}",
            "s1-run" => $"S1_RUN|{StateOnOff(enabled)}",
            "s1-dir" => $"S1_DIR|{StateBit(enabled)}",
            "step-run" => $"STEP_RUN|{StateOnOff(enabled)}",
            "step-dir" => $"STEP_DIR|{StateBit(enabled)}",
            "step-rev" => $"STEP_REV|{StateOnOff(enabled)}",
            _ => $"OUTPUT|KEY={key}|VALUE={StateBit(enabled)}"
        };

        return SendLineAsync(line, cancellationToken);
    }

    // [ACURATEX] Traducir frecuencias a texto es parte del protocolo del tablero.
    public Task SetFrequencyAsync(string key, int frequencyHz, CancellationToken cancellationToken = default)
    {
        string line = key switch
        {
            "servo1" => $"S1_FREQ|HZ={frequencyHz}",
            "stepper" => $"STEP_FREQ|HZ={frequencyHz}",
            _ => $"FREQ|KEY={key}|HZ={frequencyHz}"
        };

        return SendLineAsync(line, cancellationToken);
    }

    // [ACURATEX] Envía la orden de posicionamiento.
    public Task GoToPositionAsync(int positionNumber, decimal target, int turns, CancellationToken cancellationToken = default)
    {
        string line = $"S2_GOTO|POS={positionNumber}|TARGET={FormatDecimal(target)}|TURNS={turns}";
        return SendLineAsync(line, cancellationToken);
    }

    // [ACURATEX] Serializa una rutina compuesta en una sola linea de control.
    public Task SetRoutineAsync(ServoDashboardUnificadoRoutineCommand command, CancellationToken cancellationToken = default)
    {
        if (!command.Enabled) {
            return SendLineAsync("S2_ROUTINE|OFF", cancellationToken);
        }

        string order = string.Join(",", command.Steps.Select(static step => step.PositionNumber));
        string steps = string.Join("|", command.Steps.Select(static step =>
            $"P{step.PositionNumber}={FormatDecimal(step.Target)}:{step.Turns}"));
        string line = $"S2_ROUTINE|ON|SPEED={command.SpeedLevel}|ORDER={order}";

        if (!string.IsNullOrWhiteSpace(steps)) {
            line += $"|{steps}";
        }

        return SendLineAsync(line, cancellationToken);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para centralizar el chequeo de conexion antes de enviar una linea.
    ///
    /// [QUIÉN LA USA]
    /// La usan todos los metodos publicos del servicio unificado.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta justo antes de mandar cualquier comando.
    ///
    /// [ENTRADAS]
    /// Recibe la linea ya formateada y un token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` o una tarea completada si no hay conexion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede escribir en la conexion activa.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `SendLineAsync()` -> `IConnectionController` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la puerta comun de salida de un puerto serie o CAN.
    ///
    /// [SI NO EXISTIERA]
    /// Cada metodo deberia repetir el mismo chequeo de enlace.
    /// </summary>
    private Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        if (!_connection.IsConnected) {
            return Task.CompletedTask;
        }

        return _connection.SendLineAsync(line, cancellationToken);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para formatear decimales con punto fijo y evitar dependencia de cultura local.
    ///
    /// [QUIÉN LA USA]
    /// La usan los comandos de posicion y rutina.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al convertir valores numericos a texto.
    ///
    /// [ENTRADAS]
    /// Recibe un valor decimal.
    ///
    /// [SALIDAS]
    /// Devuelve texto con separador de punto.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Valor -> `FormatDecimal()` -> comando.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a convertir un entero escalado a texto antes de transmitirlo.
    ///
    /// [SI NO EXISTIERA]
    /// La cultura del sistema podria romper el formato de comandos.
    /// </summary>
    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para convertir un booleano a la palabra ON o OFF.
    ///
    /// [QUIÉN LA USA]
    /// La usan los comandos de init y salida.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al serializar estados binarios.
    ///
    /// [ENTRADAS]
    /// Recibe `true` o `false`.
    ///
    /// [SALIDAS]
    /// Devuelve ON u OFF.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Bool -> `StateOnOff()` -> comando textual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a traducir un bit a una palabra de protocolo.
    ///
    /// [SI NO EXISTIERA]
    /// El protocolo tendria que repetir la conversion en varios sitios.
    /// </summary>
    private static string StateOnOff(bool enabled)
    {
        return enabled ? "ON" : "OFF";
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para convertir un booleano a 1 o 0 cuando el protocolo usa bits.
    ///
    /// [QUIÉN LA USA]
    /// La usan los comandos de salida y direccion.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al formatear flags binarios.
    ///
    /// [ENTRADAS]
    /// Recibe `true` o `false`.
    ///
    /// [SALIDAS]
    /// Devuelve 1 o 0.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Bool -> `StateBit()` -> protocolo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un bit de registro.
    ///
    /// [SI NO EXISTIERA]
    /// La conversion de bit quedaria repetida en varios comandos.
    /// </summary>
    private static int StateBit(bool enabled)
    {
        return enabled ? 1 : 0;
    }
}
