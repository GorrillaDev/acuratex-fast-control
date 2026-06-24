// [ACURATEX] Esta ventana existe solo para el arranque.
// Muestra una pantalla intermedia mientras la app prepara servicios y luego se cierra.
using System.Drawing;
using AcuratexControlApp.Components;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AcuratexControlApp;

// [C#] `sealed` impide que otra clase herede de esta ventana.
// [C#] `Form` es la clase base de una ventana WinForms.
public sealed class SplashForm : Form
{
    // [C#] `readonly` significa que la referencia se asigna aquí o en el constructor,
    // pero no se puede reemplazar luego.
    // [ACURATEX] El `BlazorWebView` permite incrustar una pantalla Razor dentro de WinForms.
    private readonly BlazorWebView _blazorWebView = new();
    // [ACURATEX] Este temporizador controla cuánto tiempo permanece visible el splash.
    private readonly System.Windows.Forms.Timer _closeTimer = new();
    // [C#] `ServiceProvider?` usa `?` para indicar que puede ser `null`.
    // [ACURATEX] Aquí se guardan los servicios que necesita la vista Razor del splash.
    private ServiceProvider? _blazorServices;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para dejar lista la ventana splash antes de mostrarla.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `Program.Main()` cuando la app está arrancando.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta una sola vez, justo al crear la ventana splash.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Configura la UI, prepara Blazor y arma el temporizador de cierre.
    ///
    /// [FLUJO ACURATEX]
    /// Windows -> Program.Main -> new SplashForm() -> InitializeComponent() -> ConfigureBlazor().
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bloque de inicialización que configura periféricos antes de arrancar.
    ///
    /// [SI NO EXISTIERA]
    /// El splash no tendría configuración ni contenido Blazor.
    /// </summary>
    public SplashForm()
    {
        // [ACURATEX] Primero se construye la parte visual de la ventana.
        InitializeComponent();
        // [ACURATEX] Después se conecta Blazor para poder renderizar la pantalla Razor.
        ConfigureBlazor();

        // [C#] `Interval` está en milisegundos.
        // [ACURATEX] El splash permanece visible unos segundos antes de cerrarse solo.
        _closeTimer.Interval = 5000;
        // [C#] `+=` suscribe un método al evento `Tick`.
        // [ACURATEX] Cuando el temporizador vence, se ejecuta `CloseTimer_Tick`.
        _closeTimer.Tick += CloseTimer_Tick;
    }

    // [ACURATEX] Este método reemplaza el diseño generado por diseñador para dejar clara
    // la estructura de la ventana splash.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para construir manualmente la ventana splash sin depender del
    /// diseno generado por el diseñador.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor de `SplashForm`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante el arranque de la ventana, antes de mostrarla.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Configura propiedades visuales y agrega el control Blazor a la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// `SplashForm()` -> `InitializeComponent()` -> estructura visual base.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a configurar los pines y el marco de una pantalla antes de encenderla.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana no tendria su estructura visual ni el control que hospeda Razor.
    /// </summary>
    private void InitializeComponent()
    {
        // [C#] Propiedades de la base `Form` que controlan la apariencia y el tamaño.
        AutoScaleMode = AutoScaleMode.None;
        AutoScroll = false;
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = string.Empty;
        Padding = Padding.Empty;
        Margin = Padding.Empty;
        BackColor = Color.FromArgb(11, 19, 38);
        ClientSize = new Size(720, 405);
        MinimumSize = ClientSize;
        MaximumSize = ClientSize;

        // [C#] `_blazorWebView` es un control hijo que ocupa toda la ventana.
        _blazorWebView.Dock = DockStyle.Fill;
        _blazorWebView.Location = new Point(0, 0);
        _blazorWebView.Margin = Padding.Empty;
        _blazorWebView.Padding = Padding.Empty;
        _blazorWebView.BackColor = BackColor;
        _blazorWebView.Name = "splashBlazorWebView";
        _blazorWebView.Size = ClientSize;
        _blazorWebView.TabIndex = 0;

        Controls.Add(_blazorWebView);
    }

    // [ACURATEX] Aquí se montan los servicios mínimos para que Razor pueda dibujar el splash.
    // [FLUJO] WinForms Form -> ServiceCollection -> BuildServiceProvider -> BlazorWebView.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para crear el contenedor minimo que necesita BlazorWebView.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor despues de crear la parte visual.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una vez al iniciar el splash.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Registra servicios, crea el provider y enlaza la pagina host con el componente Razor.
    ///
    /// [FLUJO ACURATEX]
    /// `SplashForm()` -> `ConfigureBlazor()` -> `BlazorWebView`.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a preparar un entorno minimo de runtime antes de cargar una pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// El splash no podria renderizar su contenido Razor.
    /// </summary>
    private void ConfigureBlazor()
    {
        // [C#] `ServiceCollection` arma el contenedor que luego se convierte en `ServiceProvider`.
        // [ACURATEX] El splash solo necesita lo justo para dibujar su componente Razor.
        // [C#] `ServiceCollection` es el contenedor donde se registran dependencias.
        ServiceCollection services = new();
        // [ACURATEX] Habilita la integración Blazor dentro de Windows Forms.
        services.AddWindowsFormsBlazorWebView();

        // [C#] `BuildServiceProvider()` crea el contenedor final que entregará servicios a Razor.
        _blazorServices = services.BuildServiceProvider();
        // [ACURATEX] Página host HTML que sirve como base para incrustar el componente splash.
        _blazorWebView.HostPage = "wwwroot\\index-splash.html";
        // [C#] Asigna el contenedor de servicios que usará Blazor.
        _blazorWebView.Services = _blazorServices;
        // [ACURATEX] `RootComponents.Add<SplashScreen>` conecta el componente Razor principal.
        _blazorWebView.RootComponents.Add<SplashScreen>("#app");
    }

    // [C#] `override` reemplaza el comportamiento base de `OnShown`.
    // [ACURATEX] Se usa para arrancar el temporizador justo cuando el splash ya se ve.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para arrancar el temporizador justo cuando el splash ya es visible.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms cuando la ventana termino de mostrarse.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una vez por cada vez que la ventana aparece.
    ///
    /// [ENTRADAS]
    /// Recibe el evento base de dibujo y aparicion.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Arranca `_closeTimer`.
    ///
    /// [FLUJO ACURATEX]
    /// WinForms -> `OnShown()` -> temporizador -> cierre automatico.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a habilitar un watchdog despues de que la pantalla ya encendio.
    ///
    /// [SI NO EXISTIERA]
    /// El splash no sabria cuando comenzar la cuenta regresiva para cerrarse.
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        // [ACURATEX] El temporizador empieza cuando el splash ya es visible, no antes.
        base.OnShown(e);
        _closeTimer.Start();
    }

    // [C#] `Dispose(bool)` libera recursos administrados y no administrados.
    // [ACURATEX] Aquí se apaga el temporizador, se libera el contenedor Blazor y se destruye
    // el control web para no dejar recursos vivos.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para liberar los recursos del splash cuando la ventana se destruye.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms al cerrar o descartar la ventana.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante el ciclo de cierre del formulario.
    ///
    /// [ENTRADAS]
    /// Recibe la indicacion de si hay que liberar recursos administrados.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Detiene el temporizador, quita la suscripcion y libera Blazor.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de ventana -> `Dispose(true)` -> liberacion de recursos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar perifericos y liberar memoria antes de reiniciar un sistema.
    ///
    /// [SI NO EXISTIERA]
    /// El splash podria dejar recursos vivos y suscripciones colgando.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            // [ACURATEX] Se quita la suscripcion antes de destruir el temporizador.
            _closeTimer.Tick -= CloseTimer_Tick;
            _closeTimer.Dispose();
            _blazorServices?.Dispose();
            _blazorWebView.Dispose();
        }

        base.Dispose(disposing);
    }

    // [FLUJO] Temporizador -> Tick -> esta función -> Stop() -> Close().
    // [EQUIV MCU] Es parecido a una interrupción de tiempo que dispara una rutina de cierre.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para cerrar el splash cuando expira el temporizador.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el evento `Tick` del temporizador.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando pasan los segundos definidos en `_closeTimer.Interval`.
    ///
    /// [ENTRADAS]
    /// Recibe el emisor del evento y los datos de evento.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Detiene el temporizador y cierra la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Temporizador -> `CloseTimer_Tick()` -> `Close()`.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion por tiempo que dispara una rutina de salida.
    ///
    /// [SI NO EXISTIERA]
    /// El splash no se cerraria solo cuando termina su tiempo visible.
    /// </summary>
    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        Close();
    }
}
