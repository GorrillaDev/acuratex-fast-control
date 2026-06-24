// [ACURATEX] Dialogo de inicio de sesion embebido en BlazorWebView.
// [FLUJO] LoginView -> LoginForm -> AuthStateService -> cierre del dialogo si la credencial es valida.
using System.Drawing;
using AcuratexControlApp.Components;
using AcuratexControlApp.Services.Auth;
using AcuratexControlApp.Services;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para alojar la pantalla de login dentro de una ventana WinForms
/// mientras el contenido visual real vive en Razor.
///
/// [QUIEN LA LLAMA]
/// La llama el arranque de la aplicacion cuando necesita pedir credenciales.
///
/// [CUANDO SE EJECUTA]
/// Se ejecuta al abrir la ventana de autenticacion y mientras el usuario intenta iniciar sesion.
///
/// [ENTRADAS]
/// Recibe el icono de la app y el servicio de autenticacion.
///
/// [SALIDAS]
/// Devuelve un dialogo WinForms que termina en `OK` o `Cancel`.
///
/// [EFECTOS SECUNDARIOS]
/// Construye el contenedor Blazor, actualiza el estado de UI y puede cerrar la ventana.
///
/// [FLUJO ACURATEX]
/// Program/Main -> `LoginForm` -> `LoginView` -> `AuthStateService` -> cierre.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una rutina de acceso que abre un menu de autenticacion y luego vuelve al programa.
///
/// [SI NO EXISTIERA]
/// El login tendria que vivir repartido entre varias pantallas y no quedaria encapsulado.
/// </summary>
public sealed class LoginForm : Form, ILoginDialogHost
{
    // [ACURATEX] El control Blazor renderiza la vista de login dentro del formulario.
    private readonly BlazorWebView _blazorWebView = new();
    // [ACURATEX] Estado visual compartido con la vista Razor.
    private readonly LoginViewState _state = new();
    // [ACURATEX] Servicio que valida credenciales demo y actualiza la sesion.
    private readonly AuthStateService _authState;
    private ServiceProvider? _services;
    private Icon? _dialogIcon;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// El constructor existe para conectar el icono de la app y el servicio de autenticacion.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el codigo de arranque.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez al crear el dialogo.
    ///
    /// [ENTRADAS]
    /// Recibe el icono general de la app y el servicio de autenticacion.
    ///
    /// [SALIDAS]
    /// Devuelve la ventana preparada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Inicializa controles y monta el host Blazor.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `LoginForm` -> `ConfigureBlazor()` -> vista de login.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar una pantalla de menu antes de entrar al loop de teclado.
    ///
    /// [SI NO EXISTIERA]
    /// No habria forma encapsulada de levantar el dialogo de autenticacion.
    /// </summary>
    public LoginForm(Icon? appIcon, AuthStateService authState)
    {
        _authState = authState;
        InitializeComponent();

        if (appIcon != null) {
            _dialogIcon = (Icon)appIcon.Clone();
            Icon = _dialogIcon;
        }

        ConfigureBlazor();
    }

    // [C#] Propiedad de solo lectura.
    // [ACURATEX] La vista Razor lee este estado para dibujar mensajes de error o campos.
    public LoginViewState State => _state;

    // [C#] `event` permite que la vista o la UI reaccionen al cambio.
    // [ACURATEX] Cuando cambia el estado del login, la interfaz vuelve a pintarse.
    public event Action? StateChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para pedirle al servicio de autenticacion que valide el usuario.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la vista Razor del login cuando el usuario pulsa entrar.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al confirmar credenciales.
    ///
    /// [ENTRADAS]
    /// Recibe usuario y contrasena.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea completada cuando termina el intento de login.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el texto de error o cierra el dialogo si el acceso fue valido.
    ///
    /// [FLUJO ACURATEX]
    /// LoginView -> `SubmitAsync()` -> `AuthStateService.Login()` -> resultado visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar un comando de acceso contra una tabla de credenciales.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no podria completar el flujo de autenticacion.
    /// </summary>
    public Task SubmitAsync(string userId, string password)
    {
        // [ACURATEX] La vista no valida por su cuenta: delega en el servicio de autenticacion.
        if (!_authState.Login(userId, password, out string errorMessage)) {
            // [ACURATEX] El error se guarda en el estado para que la vista Razor lo pinte sin conocer la regla.
            _state.ErrorMessage = errorMessage;
            NotifyStateChanged();
            return Task.CompletedTask;
        }

        _state.ErrorMessage = string.Empty;
        CompleteLogin();
        return Task.CompletedTask;
    }

    // [ACURATEX] Libera servicios, icono y host Blazor cuando la ventana ya no existe.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para liberar el icono clonado, el contenedor Blazor y los
    /// recursos del dialogo cuando la ventana deja de existir.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms al cerrar el formulario.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante el ciclo de cierre del dialogo de login.
    ///
    /// [ENTRADAS]
    /// Recibe la indicacion de si hay que liberar recursos administrados.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Libera el provider, el icono y el host Blazor.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de login -> `Dispose(true)` -> limpieza de recursos visuales.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar una pantalla y soltar todos sus periféricos antes de salir.
    ///
    /// [SI NO EXISTIERA]
    /// El dialogo podria dejar recursos vivos despues de cerrarse.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _services?.Dispose();
            _dialogIcon?.Dispose();
            _blazorWebView.Dispose();
        }

        base.Dispose(disposing);
    }

    // [ACURATEX] Configura el formulario WinForms sin depender de Designer.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para construir manualmente el dialogo de login sin depender del
    /// diseñador.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor de `LoginForm`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al crear la ventana.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Configura el tamano, el host Blazor y la apariencia general del formulario.
    ///
    /// [FLUJO ACURATEX]
    /// `LoginForm()` -> `InitializeComponent()` -> dialogo visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a definir el marco de una pantalla antes de cargar su contenido.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana no tendría su estructura base.
    /// </summary>
    private void InitializeComponent()
    {
        SuspendLayout();

        _blazorWebView.Dock = DockStyle.Fill;
        _blazorWebView.Location = new Point(0, 0);
        _blazorWebView.Name = "loginBlazorWebView";
        _blazorWebView.Size = new Size(980, 560);
        _blazorWebView.TabIndex = 0;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        AutoScroll = false;
        ClientSize = new Size(980, 560);
        Controls.Add(_blazorWebView);
        ControlBox = false;
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(900, 540);
        Name = "LoginForm";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = string.Empty;

        ResumeLayout(false);
    }

    // [ACURATEX] Monta los servicios que la vista Razor necesita para funcionar dentro del form.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para registrar los servicios minimos que necesita la vista Razor
    /// del login.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor despues de crear el control visual.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez al abrir el dialogo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea el provider y conecta el componente `LoginView`.
    ///
    /// [FLUJO ACURATEX]
    /// Constructor -> `ConfigureBlazor()` -> `BlazorWebView` -> Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a preparar el entorno de ejecución antes de mostrar un menu de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// El login no podria renderizar su contenido Razor dentro de WinForms.
    /// </summary>
    private void ConfigureBlazor()
    {
        // [C#] `AddSingleton` deja una sola instancia compartida por el host Blazor.
        // [ACURATEX] Aqui se entrega tanto la chrome como el host del dialogo.
        ServiceCollection services = new();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<IWindowChromeHost>(_ => new WindowChromeHost(this, "Login", canMinimize: false, canMaximize: false));
        services.AddSingleton<ILoginDialogHost>(this);

        _services = services.BuildServiceProvider();
        _blazorWebView.HostPage = "wwwroot\\index-login.html";
        _blazorWebView.Services = _services;
        _blazorWebView.RootComponents.Add<LoginView>("#app");
    }

    // [ACURATEX] Cierra el dialogo cuando el login ya fue aceptado.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para cerrar el dialogo cuando las credenciales fueron aceptadas.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `SubmitAsync()` cuando `AuthStateService` valida el acceso.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta solo despues de un login correcto.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Fija `DialogResult` y cierra la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Login correcto -> `CompleteLogin()` -> cierre del dialogo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a confirmar un acceso valido y salir del menu de autenticacion.
    ///
    /// [SI NO EXISTIERA]
    /// El usuario quedaria autenticado pero el dialogo no cerraria.
    /// </summary>
    private void CompleteLogin()
    {
        // [FLUJO] Un login valido fija `DialogResult=OK` y cierra el dialogo.
        if (InvokeRequired) {
            // [C#] `BeginInvoke` deja la orden de cierre en cola para el hilo de UI.
            BeginInvoke(CompleteLogin);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    // [ACURATEX] Notifica a la UI que el estado del login cambio.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para avisar a la vista Razor que el estado del login cambio.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman `SubmitAsync()` y cualquier ruta futura que modifique el estado.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando cambia el mensaje de error o cuando el login se completa.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara `StateChanged` en el hilo correcto de UI.
    ///
    /// [FLUJO ACURATEX]
    /// Cambio de login -> `NotifyStateChanged()` -> repintado Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion que fuerza refrescar la pantalla de estado.
    ///
    /// [SI NO EXISTIERA]
    /// El mensaje de error o el estado visual no se actualizarian a tiempo.
    /// </summary>
    private void NotifyStateChanged()
    {
        // [C#] `InvokeRequired` evita tocar la UI desde un hilo que no es el de WinForms.
        if (IsDisposed) {
            return;
        }

        if (InvokeRequired) {
            if (IsHandleCreated) {
                BeginInvoke(NotifyStateChanged);
            }

            return;
        }

        StateChanged?.Invoke();
    }
}
