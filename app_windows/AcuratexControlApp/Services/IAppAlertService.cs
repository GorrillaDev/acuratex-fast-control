namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Este enum existe para clasificar el tipo visual y operativo de una alerta.
///
/// [QUIÉN LO USA]
/// Lo usan el servicio de alertas y el modal que la presenta.
///
/// [CUÁNDO SE USA]
/// Se usa cada vez que la app necesita mostrar una alerta distinta.
///
/// [ENTRADAS]
/// No recibe entradas.
///
/// [SALIDAS]
/// Devuelve una categoria cerrada: informacion, aviso o error.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Servicio -> `AppAlertKind` -> modal.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un codigo de severidad de diagnostico.
///
/// [SI NO EXISTIERA]
/// La UI tendria que decidir el estilo de cada alerta con cadenas sueltas.
/// </summary>
public enum AppAlertKind
{
    Info,
    Warning,
    Error
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para transportar de forma compacta toda la informacion de una alerta.
///
/// [QUIÉN LO USA]
/// Lo usan el servicio de alertas y el modal Razor.
///
/// [CUÁNDO SE USA]
/// Se usa cuando la app prepara un mensaje para mostrar al operador.
///
/// [ENTRADAS]
/// Recibe titulo, mensaje, tipo visual y texto del boton.
///
/// [SALIDAS]
/// Devuelve un objeto de datos inmutable por defecto.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Lógica -> `AppAlertRequest` -> modal.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una trama de mensaje de diagnostico con campos fijos.
///
/// [SI NO EXISTIERA]
/// La alerta tendria que pasar esos datos por parametros separados en varios lugares.
/// </summary>
public sealed record AppAlertRequest(
    string Title,
    string Message,
    AppAlertKind Kind,
    string ButtonText);

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para centralizar la apertura y cierre de alertas modales.
///
/// [QUIÉN LA USA]
/// La usan formularios, componentes Razor y servicios que necesitan avisar algo al operador.
///
/// [CUÁNDO SE USA]
/// Se usa cuando aparece una condicion que requiere un mensaje bloqueante o confirmable.
///
/// [ENTRADAS]
/// Expone solicitudes de alerta y un evento de actualizacion.
///
/// [SALIDAS]
/// Devuelve el estado actual de la alerta y operaciones asincronas de apertura o cierre.
///
/// [EFECTOS SECUNDARIOS]
/// Puede cambiar la alerta activa y repintar la UI.
///
/// [FLUJO ACURATEX]
/// Servicio -> modal -> confirmacion del operador.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una bandera de alarma visible para el operador.
///
/// [SI NO EXISTIERA]
/// Cada pantalla tendria que administrar su propio modal de aviso.
/// </summary>
public interface IAppAlertService
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para dejar visible la alerta activa, o `null` si no hay ninguna.
    ///
    /// [QUIÉN LA USA]
    /// La usa el modal Razor para decidir si debe renderizarse.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta durante el render.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la alerta activa o ausencia de alerta.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `CurrentAlert` -> modal.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de alarma que indica si hay una notificacion pendiente.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria si debe mostrar el modal.
    /// </summary>
    AppAlertRequest? CurrentAlert { get; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar que cambio la alerta y la UI debe repintarse.
    ///
    /// [QUIÉN LO USA]
    /// Lo suscriben los componentes Razor del modal.
    ///
    /// [CUÁNDO SE USA]
    /// Se dispara al abrir o cerrar una alerta.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Provoca actualización visual.
    ///
    /// [FLUJO ACURATEX]
    /// Estado de alerta -> `AlertChanged` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de refresco de pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no sabria cuando mostrar o ocultar el modal.
    /// </summary>
    event Action? AlertChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para abrir una alerta modal desde cualquier parte de la app
    /// sin que el llamador tenga que manipular controles visuales directamente.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman formularios, componentes Razor y servicios cuando necesitan avisar,
    /// bloquear una accion o pedir confirmacion.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta en el momento en que la logica detecta una condicion que requiere
    /// la atencion del operador.
    ///
    /// [ENTRADAS]
    /// Recibe titulo, mensaje, tipo visual, texto del boton y un `CancellationToken`
    /// para poder cancelar la espera.
    ///
    /// [SALIDAS]
    /// Devuelve un `Task` que termina cuando la alerta se cierra o se cancela.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza `CurrentAlert` y dispara `AlertChanged`.
    ///
    /// [FLUJO ACURATEX]
    /// UI o servicio -> `ShowAsync()` -> estado compartido de alerta -> componente modal.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a encender un LED o levantar una bandera de aviso que otro modulo observa.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendria que construir su propio modal y la app duplicaria la logica.
    /// </summary>
    Task ShowAsync(
        string title,
        string message,
        AppAlertKind kind = AppAlertKind.Warning,
        string buttonText = "Aceptar",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para cerrar la alerta activa sin depender de que el usuario
    /// encuentre el boton exacto del modal.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la propia vista del modal cuando el usuario acepta el mensaje.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta justo despues del clic en el boton de confirmacion.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Libera la alerta activa y avisa a los suscriptores.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> boton del modal -> `AcceptCurrent()` -> cierre de la alerta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a borrar un bit de estado cuando el operador reconoce una alarma.
    ///
    /// [SI NO EXISTIERA]
    /// El modal quedaria abierto hasta que otra pieza de codigo lo cerrara por accidente.
    /// </summary>
    void AcceptCurrent();
}
