// [ACURATEX] Servicio central de alertas modales.
// [FLUJO] La logica de negocio lo llama y el componente modal observa `CurrentAlert`.
// [EQUIV MCU] Es como una bandera de alarma compartida que la HMI lee.
namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para concentrar en un solo punto la vida de una alerta modal.
///
/// [QUIÉN LA LLAMA]
/// La llaman servicios y vistas que necesitan informar algo al operador.
///
/// [CUÁNDO SE EJECUTA]
/// Sus métodos se ejecutan cuando alguna parte de la app quiere mostrar o cerrar una alerta.
///
/// [ENTRADAS]
/// Recibe datos de texto, tipo visual y cancelación opcional.
///
/// [SALIDAS]
/// Devuelve tareas que terminan cuando la alerta se cierra.
///
/// [EFECTOS SECUNDARIOS]
/// Cambia `CurrentAlert`, activa eventos y administra la continuidad de la espera.
///
/// [FLUJO ACURATEX]
/// Servicio o UI -> `ShowAsync()` -> estado compartido -> componente modal.
///
/// [EQUIVALENCIA MCU]
/// Es parecido a levantar una bandera de alarma y esperar a que un operador la reconozca.
///
/// [SI NO EXISTIERA]
/// Cada pantalla tendría que replicar el manejo de modales y cancelaciones.
/// </summary>
public sealed class AppAlertService : IAppAlertService, IDisposable
{
    // [C#] `readonly` impide reemplazar la referencia despues de construir el objeto.
    // [ACURATEX] Este candado protege la alerta activa cuando varias partes quieren abrirla o cerrarla.
    private readonly object _gate = new();
    // [C#] `TaskCompletionSource` actua como un puente manual entre una accion y quien espera su fin.
    // [ACURATEX] Aquí representa el cierre asíncrono del modal.
    private TaskCompletionSource? _activeCompletion;
    // [C#] `CancellationTokenRegistration` guarda la suscripcion a la cancelacion.
    // [ACURATEX] Permite cerrar la alerta si la operación que la abrió se cancela.
    private CancellationTokenRegistration _cancellationRegistration;
    private bool _disposed;

    // [C#] Propiedad con `get` y `private set`.
    // [ACURATEX] La UI puede leer la alerta, pero solo el servicio decide cuál está activa.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para que la UI lea cual es la alerta modal activa sin poder
    /// cambiarla directamente.
    ///
    /// [QUIEN LA USA]
    /// La usan el componente visual del modal y cualquier vista que deba reflejar su estado.
    ///
    /// [CUANDO SE USA]
    /// Se consulta al renderizar o repintar la interfaz.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la alerta activa o `null` si no hay ninguna.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `CurrentAlert` -> modal Razor/WinForms.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una bandera de alarma que otro modulo escribio.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendria que preguntar por otro canal cual alerta esta activa.
    /// </summary>
    public AppAlertRequest? CurrentAlert { get; private set; }

    // [C#] `event` usa delegados y suscripción con `+=`.
    // [ACURATEX] Notifica a la interfaz cuando debe dibujar o quitar el modal.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este evento existe para avisar que la alerta cambio y la UI debe volver a leer
    /// `CurrentAlert`.
    ///
    /// [QUIEN LO USA]
    /// Lo usan el modal visual y las vistas que quieren repintarse.
    ///
    /// [CUANDO SE USA]
    /// Se dispara cuando se abre, cierra o reemplaza una alerta.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Despierta a los suscriptores para que repinten.
    ///
    /// [FLUJO ACURATEX]
    /// Cambio de alerta -> `AlertChanged` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de refresco para la HMI.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz tendria que estar sondeando el estado en lugar de reaccionar al cambio.
    /// </summary>
    public event Action? AlertChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abrir una alerta y devolver un `Task` que termina cuando
    /// el usuario la acepta o cuando el flujo se cancela.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman pantallas y servicios de la app.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta en el instante en que una condición requiere atención del operador.
    ///
    /// [ENTRADAS]
    /// Recibe título, mensaje, tipo visual, texto del botón y token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea que completa al cerrar la alerta.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia el estado compartido, registra la nueva espera y avisa a la UI.
    ///
    /// [FLUJO ACURATEX]
    /// Acción -> `ShowAsync()` -> alerta activa -> modal Razor/WinForms.
    ///
    /// [EQUIVALENCIA MCU]
    /// Similar a habilitar un indicador de error hasta que el operador lo reconoce.
    ///
    /// [SI NO EXISTIERA]
    /// No habría una forma unificada de esperar la confirmación del usuario.
    /// </summary>
    public Task ShowAsync(
        string title,
        string message,
        AppAlertKind kind = AppAlertKind.Warning,
        string buttonText = "Aceptar",
        CancellationToken cancellationToken = default)
    {
        // [C#] `new(...)` construye el objeto y ejecuta el constructor correspondiente.
        // [ACURATEX] Creamos una nueva espera para esta alerta concreta.
        // [FLUJO] Cada alerta obtiene su propio `TaskCompletionSource` para no mezclar cierres.
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource? previousCompletion = null;

        // [C#] `lock` protege el estado compartido cuando varias partes quieren mostrar alertas al mismo tiempo.
        // [ACURATEX] El servicio solo deja una alerta activa para que la HMI no muestre modales superpuestos.
        lock (_gate) {
            if (_disposed) {
                completion.SetResult();
                return completion.Task;
            }

            // [ACURATEX] Si ya habia una alerta viva, la dejamos terminar antes de reemplazarla.
            previousCompletion = _activeCompletion;
            _cancellationRegistration.Dispose();

            // [ACURATEX] Esta es la alerta que la vista va a mostrar ahora.
            CurrentAlert = new AppAlertRequest(title, message, kind, buttonText);
            _activeCompletion = completion;

            if (cancellationToken.CanBeCanceled) {
                // [C#] `Register` ejecuta un callback cuando el token se cancela.
                // [ACURATEX] Si la operacion se cancela, tambien cerramos el modal.
                _cancellationRegistration = cancellationToken.Register(() => Close(completion));
            }
        }

        previousCompletion?.TrySetResult();
        NotifyChanged();
        return completion.Task;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para que el botón del modal pueda cerrar la alerta activa.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el componente visual de la alerta.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pulsa el botón de aceptar.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Libera la espera interna y borra la alerta visible.
    ///
    /// [FLUJO ACURATEX]
    /// Botón modal -> `AcceptCurrent()` -> `Close()` -> `AlertChanged`.
    ///
    /// [EQUIVALENCIA MCU]
    /// Es parecido a confirmar una alarma y limpiar la bandera de aviso.
    ///
    /// [SI NO EXISTIERA]
    /// El modal no tendría una forma directa de cerrar la alerta actual.
    /// </summary>
    public void AcceptCurrent()
    {
        Close(null);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para liberar recursos si el servicio deja de usarse.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el contenedor de la app o el ciclo de vida del servicio.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al cerrar la aplicación o cuando el servicio se desecha manualmente.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cancela la alerta activa y marca el servicio como descartado.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de app -> `Dispose()` -> limpieza de la alerta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar un periférico y borrar su estado temporal.
    ///
    /// [SI NO EXISTIERA]
    /// Quedaría una espera colgada si la app termina con una alerta abierta.
    /// </summary>
    public void Dispose()
    {
        TaskCompletionSource? completion;

        lock (_gate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _cancellationRegistration.Dispose();
            completion = _activeCompletion;
            _activeCompletion = null;
            CurrentAlert = null;
        }

        completion?.TrySetResult();
        NotifyChanged();
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para cerrar la alerta solo si sigue siendo la que esta activa en este momento.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llaman `AcceptCurrent()` y el callback de cancelacion del `CancellationToken`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el usuario acepta la alerta o cuando se cancela la operacion que la abrio.
    ///
    /// [ENTRADAS]
    /// Recibe la instancia de `TaskCompletionSource` que se espera cerrar, o `null` para cerrar la activa.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Borra la alerta visible, libera la espera y notifica cambios.
    ///
    /// [FLUJO ACURATEX]
    /// Boton modal o cancelacion -> `Close()` -> estado limpio -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a limpiar una bandera de alarma solo si corresponde a la alarma actual.
    ///
    /// [SI NO EXISTIERA]
    /// Una cancelacion vieja podria cerrar una alerta nueva por error.
    /// </summary>
    private void Close(TaskCompletionSource? expectedCompletion)
    {
        // [ACURATEX] Solo se cierra la alerta que sigue siendo la activa.
        TaskCompletionSource? completion;

        // [C#] `ReferenceEquals` compara si dos referencias apuntan exactamente al mismo objeto.
        // [ACURATEX] Eso evita cerrar una alerta nueva usando una cancelacion vieja.
        lock (_gate) {
            if (expectedCompletion != null && !ReferenceEquals(_activeCompletion, expectedCompletion)) {
                return;
            }

            _cancellationRegistration.Dispose();
            completion = _activeCompletion;
            _activeCompletion = null;
            CurrentAlert = null;
        }

        completion?.TrySetResult();
        NotifyChanged();
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para centralizar el aviso a los suscriptores cuando cambia el estado de la alerta.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llaman las rutas que abren, cierran o desusan el servicio.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que la alerta cambia de estado.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara `AlertChanged` si hay suscriptores.
    ///
    /// [FLUJO ACURATEX]
    /// Cambio de alerta -> `NotifyChanged()` -> modal y vistas.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de refresco visual.
    ///
    /// [SI NO EXISTIERA]
    /// Cada ruta tendria que repetir la misma llamada al evento.
    /// </summary>
    private void NotifyChanged()
    {
        // [C#] `?.Invoke()` llama al evento solo si hay suscriptores.
        AlertChanged?.Invoke();
    }
}
