namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para separar la vista del cabezal unificado de la forma concreta en
/// que se traducen sus botones y campos a comandos reales.
///
/// [QUIÉN LA USA]
/// La usa `CabezalDashboardUnificado.razor`.
///
/// [CUÁNDO SE USA]
/// Se usa cada vez que la UI necesita mandar una intención de cabezal.
///
/// [ENTRADAS]
/// Define métodos que reciben texto, índices, estados y cancelación.
///
/// [SALIDAS]
/// Devuelve `Task` porque cada acción puede esperar conexión o firmware.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene; solo define contrato.
///
/// [FLUJO ACURATEX]
/// UI -> interfaz -> implementación concreta -> conexión o firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla de vectores que define qué rutinas de salida existen.
///
/// [SI NO EXISTIERA]
/// La vista dependería de una clase concreta y perdería flexibilidad de prueba o cambio.
/// </summary>
public interface ICabezalDashboardUnificadoCommandService
{
    /// <summary>
    /// Envía una línea CAN directa sin reinterpretarla como acción de alto nivel.
    /// </summary>
    Task SendCanLineAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un comando lógico del cabezal, como `init`, `status` o `j_run_all`.
    /// </summary>
    Task SendDoCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía una posición de DEN usando el índice lógico del motor y la selección visual.
    /// </summary>
    Task SendDenPositionAsync(int motorIndex, int position, int selectedPositionNumber = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía una posición de SIC usando el índice lógico del motor y la selección visual.
    /// </summary>
    Task SendSicPositionAsync(int sicIndex, int position, int selectedPositionNumber = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un registro J completo como una sola intención de grupo.
    /// </summary>
    Task SendJRegisterAsync(int jIndex, byte value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa o desactiva todos los canales de un grupo J.
    /// </summary>
    Task SendJAllAsync(int jIndex, bool on, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa o desactiva un canal individual de J.
    /// </summary>
    Task SendJChannelAsync(int jIndex, int channelIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa o desactiva un pin de Yarn o Stitch según la llave lógica del bloque.
    /// </summary>
    Task SendBlockPinAsync(string blockKey, int pinIndex, bool on, CancellationToken cancellationToken = default);
}
