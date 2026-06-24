// [ACURATEX] Este contrato deja que la shell unificada pida envío de comandos sin conocer
// el formulario WinForms que la hospeda.
namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para que la shell unificada pueda lanzar comandos del catálogo.
///
/// [QUIÉN LA USA]
/// La usa `UnifiedSystemShell` desde Razor.
///
/// [CUÁNDO SE USA]
/// Se usa cuando el operador pulsa un botón de comando.
///
/// [ENTRADAS]
/// Recibe una definición de comando.
///
/// [SALIDAS]
/// Devuelve tareas asincrónicas.
///
/// [EFECTOS SECUNDARIOS]
/// Puede mandar una línea al firmware o mostrar errores.
///
/// [FLUJO ACURATEX]
/// Razor -> IUnifiedSystemShellHost -> UnifiedSystemForm -> ConnectionController -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla de callbacks que dispara acciones de control.
///
/// [SI NO EXISTIERA]
/// La shell no tendría un puente formal para ejecutar comandos.
/// </summary>
public interface IUnifiedSystemShellHost
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para que la UI acceda al catálogo de comandos.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista unificada al construir botones.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta durante el render.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una lista de comandos.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UnifiedSystemForm -> Commands -> Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una tabla fija de órdenes.
    ///
    /// [SI NO EXISTIERA]
    /// La shell no sabría qué comandos mostrar.
    /// </summary>
    // [C#] `IReadOnlyList<T>` expone una lista que se puede leer pero no modificar desde la vista.
    IReadOnlyList<CommandDefinition> Commands { get; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este método existe para mandar un comando del catálogo hacia la conexión activa.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la vista al pulsar un botón.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cada vez que se quiere enviar un comando.
    ///
    /// [ENTRADAS]
    /// Recibe la definición del comando.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede producir transmisión al firmware o alertas de error.
    ///
    /// [FLUJO ACURATEX]
    /// Shell Razor -> `SendCommandAsync()` -> Formulario -> conexión -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a disparar una rutina de control desde una tabla de botones.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendría que hablar con la conexión directamente.
    /// </summary>
    // [C#] `Task` vuelve asincrono el contrato del envio de comandos.
    Task SendCommandAsync(CommandDefinition command);
}
