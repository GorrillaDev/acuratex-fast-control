// [ACURATEX] Este contrato conecta el selector de sistema con las ventanas reales que se
// van a abrir después del login.
namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para que el selector pueda abrir o cerrar la shell unificada o modular.
///
/// [QUIÉN LA USA]
/// La usa `SystemSelectorView` desde Razor.
///
/// [CUÁNDO SE USA]
/// Se usa cuando el usuario elige un sistema o cancela el selector.
///
/// [ENTRADAS]
/// No recibe datos complejos; solo ordena cambiar la ventana activa.
///
/// [SALIDAS]
/// Devuelve tareas asincrónicas para integrarse con la UI.
///
/// [EFECTOS SECUNDARIOS]
/// Puede crear formularios y cerrar el selector.
///
/// [FLUJO ACURATEX]
/// Selector Razor -> ISystemSelectorHost -> SystemSelectorForm -> shell elegida.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un menú de arranque que decide qué imagen cargar.
///
/// [SI NO EXISTIERA]
/// El selector no tendría forma formal de abrir la pantalla elegida.
/// </summary>
public interface ISystemSelectorHost
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este método existe para abrir la shell unificada desde el selector.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la vista del selector al pulsar la opción unificada.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario elige el sistema unificado.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede crear y mostrar una nueva ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> `SelectUnifiedSystemAsync()` -> formulario unificado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a elegir un modo operativo en tiempo de arranque.
    ///
    /// [SI NO EXISTIERA]
    /// No habría una ruta clara para cargar la shell unificada.
    /// </summary>
    // [C#] `Task` indica que la accion puede ser asincrona aunque la interfaz solo describa el contrato.
    Task SelectUnifiedSystemAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este método existe para abrir la shell modular desde el selector.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la vista del selector al pulsar la opción modular.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario elige el sistema modular.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede crear y mostrar una nueva ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> `SelectCardSystemAsync()` -> formulario modular.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escoger una variante de firmware antes de entrar al loop principal.
    ///
    /// [SI NO EXISTIERA]
    /// No habría una ruta clara para cargar la shell modular.
    /// </summary>
    Task SelectCardSystemAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este método existe para cerrar el selector sin abrir una shell.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la vista cuando el usuario cancela.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al abandonar el selector.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra el diálogo actual.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> `CloseAsync()` -> cierre del formulario.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a salir de un menú sin aplicar cambios.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no tendría un botón de salida encapsulado.
    /// </summary>
    Task CloseAsync();
}
