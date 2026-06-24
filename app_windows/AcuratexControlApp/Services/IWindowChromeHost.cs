// [C#] Una interfaz describe capacidades, no implementación.
// [ACURATEX] Este contrato permite que cualquier formulario exponga una chrome común.
namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para que la chrome personalizada de Razor pueda trabajar con
/// cualquier formulario WinForms sin conocer su clase concreta.
///
/// [QUIÉN LA USA]
/// La usan los componentes Razor que dibujan los botones de ventana.
///
/// [CUÁNDO SE USA]
/// Se usa mientras la ventana está viva y el usuario interactúa con ella.
///
/// [ENTRADAS]
/// Expone propiedades de estado y acciones de ventana.
///
/// [SALIDAS]
/// Devuelve tareas para que los botones puedan llamarse desde eventos asincrónicos.
///
/// [EFECTOS SECUNDARIOS]
/// Puede minimizar, maximizar, cerrar o iniciar el arrastre de la ventana.
///
/// [FLUJO ACURATEX]
/// Razor -> `IWindowChromeHost` -> formulario WinForms -> shell visual.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un pequeño conjunto de callbacks del panel frontal de una máquina.
///
/// [SI NO EXISTIERA]
/// Cada formulario tendría que exponer su propia API y la chrome perdería uniformidad.
/// </summary>
public interface IWindowChromeHost
{
    // [ACURATEX] Título que se muestra en la barra personalizada.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para que la chrome Razor muestre el titulo de la ventana sin
    /// conocer la clase concreta del formulario.
    ///
    /// [QUIEN LA USA]
    /// La usan los componentes Razor de la barra personalizada.
    ///
    /// [CUANDO SE USA]
    /// Se consulta al pintar la cabecera.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el texto del titulo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Formulario -> `Title` -> chrome visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un registro de nombre de modo en una HMI.
    ///
    /// [SI NO EXISTIERA]
    /// La chrome no sabria que titulo mostrar.
    /// </summary>
    string Title { get; }

    // [ACURATEX] Indica si la ventana deja minimizar.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para decirle a la UI si el boton de minimizar debe mostrarse
    /// habilitado.
    ///
    /// [QUIEN LA USA]
    /// La usa la chrome personalizada.
    ///
    /// [CUANDO SE USA]
    /// Se consulta al dibujar la barra de titulo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Formulario -> `CanMinimize` -> boton visible o no.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de permiso para una accion de la ventana.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria si puede minimizar la ventana.
    /// </summary>
    bool CanMinimize { get; }

    // [ACURATEX] Indica si la ventana deja maximizar.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para decirle a la UI si el boton de maximizar/restaurar
    /// debe mostrarse.
    ///
    /// [QUIEN LA USA]
    /// La usa la chrome personalizada.
    ///
    /// [CUANDO SE USA]
    /// Se consulta al renderizar los controles de la ventana.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Formulario -> `CanMaximize` -> boton maximizar.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de habilitacion de modo pantalla completa.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria si puede cambiar de tamaño.
    /// </summary>
    bool CanMaximize { get; }

    // [ACURATEX] Refleja el estado visual actual.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para reflejar si la ventana esta maximizada o restaurada.
    ///
    /// [QUIEN LA USA]
    /// La usan los componentes de la chrome para elegir iconos y textos.
    ///
    /// [CUANDO SE USA]
    /// Se consulta al repintar la barra de ventana.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el estado visual actual.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Ventana -> `IsMaximized` -> chrome.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de estado de pantalla completa.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria si mostrar restaurar o maximizar.
    /// </summary>
    bool IsMaximized { get; }

    // [ACURATEX] La UI escucha cambios para repintar botones.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este evento existe para avisar que cambio el estado visual de la ventana.
    ///
    /// [QUIEN LO USA]
    /// Lo usan los componentes Razor que deben repintar botones.
    ///
    /// [CUANDO SE USA]
    /// Se dispara cuando cambia el tamaño o el estado de la ventana.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Notifica a la UI que debe refrescarse.
    ///
    /// [FLUJO ACURATEX]
    /// Cambio de ventana -> `StateChanged` -> chrome Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de repintado.
    ///
    /// [SI NO EXISTIERA]
    /// La chrome no sabria cuando actualizar sus botones.
    /// </summary>
    event Action? StateChanged;

    // [ACURATEX] Minimiza la ventana.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para minimizar la ventana desde la chrome personalizada.
    ///
    /// [QUIEN LO USA]
    /// Lo llama el boton de minimizar.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta cuando el operador pulsa minimizar.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea completada cuando la accion termina.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia el estado visual de la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Chrome -> `MinimizeAsync()` -> WinForms.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner un periferico en bajo consumo sin apagarlo.
    ///
    /// [SI NO EXISTIERA]
    /// La chrome no podria ofrecer minimizar.
    /// </summary>
    Task MinimizeAsync();

    // [ACURATEX] Alterna maximizado/restaurado.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para alternar entre maximizar y restaurar.
    ///
    /// [QUIEN LO USA]
    /// Lo llama el boton de maximizar/restaurar.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta al pulsar el control de tamaño.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea completada cuando la accion termina.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia bounds y region de la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Chrome -> `ToggleMaximizeAsync()` -> estado visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar entre pantalla normal y modo pantalla completa.
    ///
    /// [SI NO EXISTIERA]
    /// No habria forma de alternar maximizado/restaurado desde la chrome.
    /// </summary>
    Task ToggleMaximizeAsync();

    // [ACURATEX] Cierra la ventana.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para cerrar la ventana desde la chrome personalizada.
    ///
    /// [QUIEN LO USA]
    /// Lo llama el boton de cerrar.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta cuando el operador quiere salir.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea completada cuando la ventana cierra.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra la ventana WinForms.
    ///
    /// [FLUJO ACURATEX]
    /// Chrome -> `CloseAsync()` -> cierre de ventana.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a salir de un menu y volver al sistema operativo.
    ///
    /// [SI NO EXISTIERA]
    /// La chrome no podria cerrar la ventana por si misma.
    /// </summary>
    Task CloseAsync();

    // [ACURATEX] Inicia el arrastre manual de la ventana.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para arrancar el arrastre manual de la ventana.
    ///
    /// [QUIEN LO USA]
    /// Lo llaman la barra de titulo y los controles que permiten mover la ventana.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta cuando el usuario arrastra la chrome.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea completada cuando el gesto fue convertido a arrastre nativo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Manda mensajes Win32 para simular el arrastre de la barra de titulo.
    ///
    /// [FLUJO ACURATEX]
    /// Mouse -> `BeginDragWindowAsync()` -> mensaje nativo -> movimiento.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a entregar el control de un gesto a la rutina del sistema de ventanas.
    ///
    /// [SI NO EXISTIERA]
    /// La barra personalizada no permitiria mover la ventana.
    /// </summary>
    Task BeginDragWindowAsync();
}
