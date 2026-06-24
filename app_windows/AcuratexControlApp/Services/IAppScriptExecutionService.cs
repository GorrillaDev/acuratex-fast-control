namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para devolver a la capa superior el resultado de una ejecución de
/// acción o script sin lanzar excepciones cuando la operación solo falla por una condición
/// esperable.
///
/// [QUIÉN LO USA]
/// Lo usan `IAppScriptExecutionService` y la UI que necesita saber si una orden se ejecutó.
///
/// [CUÁNDO SE USA]
/// Se crea cada vez que una acción del cabezal termina con éxito o error controlado.
///
/// [ENTRADAS]
/// Recibe estado de éxito, mensaje, nombre del archivo y contador de líneas enviadas.
///
/// [SALIDAS]
/// Devuelve una instantánea del resultado.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Acción UI -> servicio -> `AppScriptExecutionResult` -> vista o log.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un código de retorno que acompaña a una rutina de periférico.
///
/// [SI NO EXISTIERA]
/// La UI tendría que interpretar excepciones para saber si una orden fue aceptada.
/// </summary>
public sealed record AppScriptExecutionResult(
    /// <summary>Indica si la ejecución terminó correctamente.</summary>
    bool Success,
    /// <summary>Mensaje legible para la UI o para el log.</summary>
    string Message,
    /// <summary>Nombre del script asociado, si la ruta usó un archivo.</summary>
    string? ScriptFileName,
    /// <summary>Cantidad de líneas o comandos enviados durante la ejecución.</summary>
    int SentCommands);

/// <summary>
/// [POR QUÉ EXISTE]
/// Este enum separa las dos políticas de ejecución del cabezal: firmware directo o script.
///
/// [QUIÉN LO USA]
/// Lo usa `AppScriptExecutionService` para decidir cómo resolver una acción.
///
/// [CUÁNDO SE USA]
/// Se consulta en cada acción del cabezal.
///
/// [ENTRADAS]
/// No recibe entradas.
///
/// [SALIDAS]
/// Devuelve el modo elegido por la configuración interna.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// UI -> servicio de scripts -> enum de modo -> firmware o script.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una bandera de compilación o a un selector de estrategia de ejecución.
///
/// [SI NO EXISTIERA]
/// La política de ejecución quedaría oculta en valores mágicos.
/// </summary>
public enum HeadScriptExecutionMode
{
    /// <summary>Enviar `HEAD_ACTION` directo al firmware.</summary>
    FirmwareHeadAction = 1,
    /// <summary>Ejecutar el comando desde un archivo o secuencia de líneas.</summary>
    AppLineByLine = 2
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para que la UI no sepa si una acción se resolverá con `HEAD_ACTION`
/// directo, con un script descargado o con ambas cosas.
///
/// [QUIÉN LA USA]
/// La usa el servicio de comandos del cabezal unificado y cualquier otro flujo que dispare
/// acciones de alto nivel.
///
/// [CUÁNDO SE USA]
/// Se usa cada vez que un botón, atajo o automatismo necesita ejecutar una orden lógica.
///
/// [ENTRADAS]
/// No recibe entradas por sí misma; define operaciones que luego reciben sistema, instancia,
/// acción y cancelación.
///
/// [SALIDAS]
/// Expone los métodos que devuelven `Task<AppScriptExecutionResult>`.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene; solo define contrato.
///
/// [FLUJO ACURATEX]
/// UI -> servicio de scripts -> conexión/firmware o script.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla de vectores: define qué rutina existe, no cómo se implementa.
///
/// [SI NO EXISTIERA]
/// La vista tendría que acoplarse a la implementación concreta.
/// </summary>
public interface IAppScriptExecutionService
{
    /// <summary>
    /// Modo actual de ejecución que el servicio usa al resolver acciones.
    /// </summary>
    HeadScriptExecutionMode ExecutionMode { get; }

    /// <summary>
    /// Ejecuta una acción de alto nivel sobre un sistema, instancia y nombre de acción.
    /// </summary>
    Task<AppScriptExecutionResult> ExecuteActionAsync(
        HeadSystemKind systemKind,
        string instanceName,
        string actionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta una acción `HEAD_ACTION` directa contra el firmware.
    /// </summary>
    Task<AppScriptExecutionResult> ExecuteHeadActionAsync(
        string actionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta una binding que apunta a un archivo de script o una secuencia de líneas.
    /// </summary>
    Task<AppScriptExecutionResult> ExecuteBindingAsync(
        HeadButtonBinding binding,
        CancellationToken cancellationToken = default);
}
