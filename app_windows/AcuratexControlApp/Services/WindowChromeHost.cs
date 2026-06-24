using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Esta clase existe para dar a un formulario WinForms una barra de tÃ­tulo propia
/// con minimizar, maximizar, cerrar y arrastre manual.
///
/// [QUIÃ‰N LA LLAMA]
/// La llama el formulario anfitriÃ³n que quiere delegar la gestiÃ³n del chrome.
///
/// [CUÃNDO SE EJECUTA]
/// Se usa mientras el formulario estÃ¡ visible y el usuario interactÃºa con la ventana.
///
/// [ENTRADAS]
/// Recibe el formulario, el tÃ­tulo y las capacidades de ventana.
///
/// [SALIDAS]
/// Devuelve tareas completadas cuando las acciones de ventana terminan.
///
/// [EFECTOS SECUNDARIOS]
/// Cambia tamaÃ±o, estado visual y regiÃ³n redondeada del formulario.
///
/// [FLUJO ACURATEX]
/// Formulario -> `WindowChromeHost` -> controles de ventana.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una placa de interfaz que traduce botones fÃ­sicos en comandos del sistema.
///
/// [SI NO EXISTIERA]
/// Cada formulario tendrÃ­a que implementar por separado su chrome personalizada.
/// </summary>
public sealed class WindowChromeHost : IWindowChromeHost, IDisposable
{
    private const int WmNcButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;
    private readonly Form _form;
    private readonly int _cornerRadius;
    private Rectangle _restoreBounds;
    private bool _isPseudoMaximized;

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// El constructor conecta el host con un formulario concreto y configura el chrome.
    ///
    /// [QUIÃ‰N LO LLAMA]
    /// Lo llama el formulario que quiere usar esta chrome.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta durante la inicializaciÃ³n del formulario.
    ///
    /// [ENTRADAS]
    /// Recibe el formulario, el tÃ­tulo y las capacidades de ventana.
    ///
    /// [SALIDAS]
    /// Devuelve el host listo para usarse.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Suscribe eventos del formulario y aplica la regiÃ³n inicial.
    ///
    /// [FLUJO ACURATEX]
    /// Formulario -> host -> barra propia.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a configurar un bloque de interfaz antes del loop principal.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana no tendrÃ­a una implementaciÃ³n comÃºn para su chrome personalizada.
    /// </summary>
    public WindowChromeHost(Form form, string title, bool canMinimize, bool canMaximize, int cornerRadius = 18)
    {
        _form = form;
        _cornerRadius = cornerRadius;
        Title = title;
        CanMinimize = canMinimize;
        CanMaximize = canMaximize;

        _form.Resize += OnFormResize;
        _form.HandleCreated += OnFormHandleCreated;
        ApplyRoundedRegion();
    }
    public string Title { get; }
    public bool CanMinimize { get; }
    public bool CanMaximize { get; }
    public bool IsMaximized => !_form.IsDisposed && (_isPseudoMaximized || _form.WindowState == FormWindowState.Maximized);
    public event Action? StateChanged;

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funcion existe para minimizar el formulario desde la chrome personalizada.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el boton de minimizar de la barra propia.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el operador pulsa el icono de minimizar.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea ya completada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia el estado visual del formulario a minimizado.
    ///
    /// [FLUJO ACURATEX]
    /// Boton de chrome -> `MinimizeAsync()` -> WinForms.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner un modulo en modo bajo consumo sin apagarlo.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana no tendria un minimizado controlado por la chrome propia.
    /// </summary>
    public Task MinimizeAsync()
    {
        if (!CanMinimize) {
            return Task.CompletedTask;
        }

        InvokeOnForm(() => _form.WindowState = FormWindowState.Minimized);
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funcion existe para alternar entre el estado normal y el maximizado.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el boton de maximizar/restaurar de la chrome.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que el operador cambia el tamaÃ±o de la ventana.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea ya completada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Ajusta bounds, ventana y region redondeada.
    ///
    /// [FLUJO ACURATEX]
    /// Boton -> `ToggleMaximizeAsync()` -> estado visual -> region.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar entre una vista compacta y una vista a pantalla completa.
    ///
    /// [SI NO EXISTIERA]
    /// La chrome no podria ofrecer restaurar/maximizar con su propia logica.
    /// </summary>
    public Task ToggleMaximizeAsync()
    {
        if (!CanMaximize) {
            return Task.CompletedTask;
        }

        InvokeOnForm(() =>
        {
            if (IsMaximized) {
                _form.WindowState = FormWindowState.Normal;
                if (_isPseudoMaximized && !_restoreBounds.IsEmpty) {
                    _form.Bounds = _restoreBounds;
                }

                _isPseudoMaximized = false;
                ApplyRoundedRegion();
                return;
            }

            _restoreBounds = _form.Bounds;
            _form.WindowState = FormWindowState.Normal;
            _form.Bounds = Screen.FromHandle(_form.Handle).WorkingArea;
            _isPseudoMaximized = true;
            ApplyRoundedRegion();
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funcion existe para cerrar el formulario desde la chrome personalizada.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el boton de cerrar.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el operador decide salir de la ventana.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea ya completada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra la ventana y, si es modal, define un resultado de dialogo.
    ///
    /// [FLUJO ACURATEX]
    /// Boton cerrar -> `CloseAsync()` -> WinForms finaliza la ventana.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a deshabilitar un perifÃ©rico y salir de un menu modal.
    ///
    /// [SI NO EXISTIERA]
    /// La chrome no podria cerrar la ventana sin depender del chrome nativo.
    /// </summary>
    public Task CloseAsync()
    {
        // [FLUJO] Cerrar desde la chrome debe pasar por WinForms para respetar modal/no modal.
        InvokeOnForm(() =>
        {
            if (_form.Modal && _form.DialogResult == DialogResult.None) {
                _form.DialogResult = DialogResult.Cancel;
            }

            _form.Close();
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funcion existe para iniciar el arrastre de la ventana desde un area personalizada.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman la barra de titulo custom y sus controles asociados.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el usuario arrastra la chrome.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea ya completada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Convierte el movimiento del mouse en un arrastre nativo de Windows.
    ///
    /// [FLUJO ACURATEX]
    /// Mouse en chrome -> `BeginDragWindowAsync()` -> mensaje Win32 -> arrastre.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a entregar el control del puntero a una rutina de manejo de ventana.
    ///
    /// [SI NO EXISTIERA]
    /// La barra personalizada no permitiria mover la ventana como una barra normal.
    /// </summary>
    public Task BeginDragWindowAsync()
    {
        if (_form.IsDisposed || !_form.IsHandleCreated) {
            return Task.CompletedTask;
        }

        if (_form.InvokeRequired) {
            _form.BeginInvoke(new Action(() => _ = BeginDragWindowAsync()));
            return Task.CompletedTask;
        }

        if (_isPseudoMaximized && !_restoreBounds.IsEmpty) {
            _form.Bounds = _restoreBounds;
            _isPseudoMaximized = false;
            ApplyRoundedRegion();
        }

        ReleaseCapture();
        SendMessage(_form.Handle, WmNcButtonDown, HtCaption, 0);
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funcion existe para desenganchar eventos y liberar la region visual.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el ciclo de vida del formulario cuando ya no se necesita el host.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al cerrar la ventana o destruir el host.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Quita suscripciones y libera recursos graficos.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre -> `Dispose()` -> limpieza de ventana.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a desactivar interrupciones y liberar memoria asociada.
    ///
    /// [SI NO EXISTIERA]
    /// Quedarian manejadores colgando y recursos graficos sin liberar.
    /// </summary>
    public void Dispose()
    {
        _form.Resize -= OnFormResize;
        _form.HandleCreated -= OnFormHandleCreated;
        _form.Region?.Dispose();
        _form.Region = null;
    }
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para ejecutar acciones sobre el hilo correcto del formulario.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman las operaciones que necesitan tocar la ventana sin salir del hilo de UI.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando una accion puede llegar desde otro hilo o desde el mismo hilo.
    ///
    /// [ENTRADAS]
    /// Recibe la accion que se quiere ejecutar sobre el formulario.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede reencolar la accion en el hilo de WinForms.
    ///
    /// [FLUJO ACURATEX]
    /// Llamador -> `InvokeOnForm()` -> hilo UI -> accion real.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a pasar una rutina a la cola principal del sistema para ejecutarla de forma segura.
    ///
    /// [SI NO EXISTIERA]
    /// La chrome podria tocar la ventana desde un hilo incorrecto.
    /// </summary>
    private void InvokeOnForm(Action action)
    {
        if (_form.IsDisposed) {
            return;
        }

        if (_form.InvokeRequired) {
            if (_form.IsHandleCreated) {
                _form.BeginInvoke(action);
            }

            return;
        }

        action();
    }
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para reaplicar la region redondeada y avisar que la ventana cambio
    /// de tamano.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el evento `Resize` del formulario.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que el usuario cambia el tamaÃ±o o estado de la ventana.
    ///
    /// [ENTRADAS]
    /// Recibe el emisor y los datos del evento.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Recalcula la region visual y dispara `StateChanged`.
    ///
    /// [FLUJO ACURATEX]
    /// Resize -> `OnFormResize()` -> region -> repintado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reconfigurar una ventana embebida cuando cambia su area util.
    ///
    /// [SI NO EXISTIERA]
    /// La forma redondeada podria quedar desalineada al cambiar el tamaÃ±o.
    /// </summary>
    private void OnFormResize(object? sender, EventArgs e)
    {
        // [FLUJO] Resize -> reaplicar region -> repintado de la chrome -> StateChanged.
        ApplyRoundedRegion();
        StateChanged?.Invoke();
    }
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para aplicar la region redondeada cuando ya existe el handle nativo.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el evento `HandleCreated` del formulario.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando WinForms ya creo la ventana nativa.
    ///
    /// [ENTRADAS]
    /// Recibe el emisor y los datos del evento.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Reaplica la region visual del formulario.
    ///
    /// [FLUJO ACURATEX]
    /// Handle creado -> `OnFormHandleCreated()` -> region redondeada.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a esperar a que un perifÃ©rico tenga direccion valida antes de escribirle.
    ///
    /// [SI NO EXISTIERA]
    /// La region redondeada podria intentarse antes de que la ventana exista.
    /// </summary>
    private void OnFormHandleCreated(object? sender, EventArgs e)
    {
        // [EQUIV MCU] Es como esperar a que el periferico este inicializado antes de tocar sus registros.
        ApplyRoundedRegion();
    }
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para aplicar o quitar la region redondeada segun el estado actual de
    /// la ventana.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman el constructor, `OnFormResize()` y `OnFormHandleCreated()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando cambia el tamano o el estado maximizado de la ventana.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica `Region` del formulario.
    ///
    /// [FLUJO ACURATEX]
    /// Estado visual -> `ApplyRoundedRegion()` -> region WinForms.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a recalcular una mascara fisica de pantalla segun el modo de operacion.
    ///
    /// [SI NO EXISTIERA]
    /// El borde redondeado no se mantendria sincronizado con la ventana.
    /// </summary>
    private void ApplyRoundedRegion()
    {
        if (_cornerRadius <= 0 || _form.IsDisposed || !_form.IsHandleCreated) {
            return;
        }

        Region? previous = _form.Region;
        if (IsMaximized) {
            _form.Region = null;
            previous?.Dispose();
            return;
        }

        Rectangle bounds = new(Point.Empty, _form.ClientSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) {
            return;
        }

        using GraphicsPath path = CreateRoundedRectangle(bounds, _cornerRadius);
        _form.Region = new Region(path);
        previous?.Dispose();
    }
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para construir la geometria de un rectangulo redondeado.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `ApplyRoundedRegion()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al fabricar la mascara visual de la ventana.
    ///
    /// [ENTRADAS]
    /// Recibe los limites del rectangulo y el radio de esquina.
    ///
    /// [SALIDAS]
    /// Devuelve un `GraphicsPath`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea una ruta grafica en memoria.
    ///
    /// [FLUJO ACURATEX]
    /// Bounds + radio -> `CreateRoundedRectangle()` -> ruta visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a calcular la forma de una pantalla antes de pintarla.
    ///
    /// [SI NO EXISTIERA]
    /// La region redondeada tendria que construirse en varios sitios.
    /// </summary>
    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        Rectangle arc = new(bounds.Location, new Size(diameter, diameter));
        GraphicsPath path = new();

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}

