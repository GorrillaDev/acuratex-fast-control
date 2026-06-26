// [ACURATEX] Host WinForms para la shell modular.
// [FLUJO] Conexion -> servicios -> BlazorWebView -> shell modular.
using System.Drawing;
using System.Media;
using AcuratexControlApp.Components;
using AcuratexControlApp.Services;
using AcuratexControlApp.Services.Auth;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para alojar la shell modular en una ventana WinForms.
///
/// [QUIEN LA LLAMA]
/// La llama el selector de sistema cuando se elige el modo modular.
///
/// [CUANDO SE EJECUTA]
/// Se ejecuta mientras la interfaz modular esta abierta.
///
/// [ENTRADAS]
/// Recibe la conexion, el catalogo de roles, la autenticacion y los permisos.
///
/// [SALIDAS]
/// Devuelve una ventana lista para hospedar la UI Razor.
///
/// [EFECTOS SECUNDARIOS]
/// Registra servicios, reacciona a perdida de conexion y reproduce un sonido de aviso.
///
/// [FLUJO ACURATEX]
/// Selector -> `CardSystemForm` -> BlazorWebView -> shell modular.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una variante del firmware principal con otra vista de operacion.
///
/// [SI NO EXISTIERA]
/// La shell modular no tendria un host que sostenga su ciclo de vida.
/// </summary>
public sealed class CardSystemForm : Form
{
    // [ACURATEX] Dependencias del modo modular.
    private readonly IConnectionController _connection;
    private readonly RoleService _roleService;
    private readonly AuthStateService _authState;
    private readonly PermissionService _permissionService;
    private readonly EmergencyStopService _emergencyStopService;
    // [ACURATEX] Host Blazor que muestra la interfaz modular.
    private readonly BlazorWebView _blazorWebView = new();
    // [ACURATEX] Servicio de alertas para informar desconexiones.
    private readonly AppAlertService _alertService = new();
    private ServiceProvider? _blazorServices;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// El constructor existe para preparar la shell modular y sus dependencias.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el selector de sistema.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al abrir la ventana modular.
    ///
    /// [ENTRADAS]
    /// Recibe la conexion, roles, autenticacion y permisos.
    ///
    /// [SALIDAS]
    /// Devuelve la ventana preparada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea el servicio de emergencia y suscribe la perdida de conexion.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> constructor -> servicios -> shell modular.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar un modo alterno con sus tablas de permiso.
    ///
    /// [SI NO EXISTIERA]
    /// La shell modular no podria arrancar con sus servicios enlazados.
    /// </summary>
    public CardSystemForm(
        IConnectionController connection,
        RoleService roleService,
        AuthStateService authState,
        PermissionService permissionService)
    {
        // [FLUJO] El formulario modular también monta su propia shell Razor y su servicio de emergencia.
        _connection = connection;
        _roleService = roleService;
        _authState = authState;
        _permissionService = permissionService;
        _emergencyStopService = new EmergencyStopService(_connection, _authState, "Sistema Modular");

        InitializeComponent();
        _connection.ConnectionLost += OnConnectionLost;
        ConfigureBlazor();
    }

    // [ACURATEX] Libera la shell y el host Blazor al cerrar la ventana.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para liberar la shell modular, el host Blazor y los recursos del
    /// formulario cuando la ventana se cierra.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms durante el cierre del formulario.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al salir de la ventana modular.
    ///
    /// [ENTRADAS]
    /// Recibe la indicacion de si hay que liberar recursos administrados.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Destruye el servicio de emergencia y el contenedor Blazor.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre modular -> `Dispose(true)` -> liberacion de UI y seguridad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar un subsistema completo antes de desconectarlo.
    ///
    /// [SI NO EXISTIERA]
    /// La shell modular podria dejar recursos activos al cerrar.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _emergencyStopService.Dispose();
            _blazorWebView.Dispose();
        }

        base.Dispose(disposing);
    }

    // [ACURATEX] Configura el formulario WinForms sin Designer.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para construir manualmente el formulario modular sin diseñador.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor de `CardSystemForm`.
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
    /// Configura el tamaño, el titulo y el control Blazor principal.
    ///
    /// [FLUJO ACURATEX]
    /// `CardSystemForm()` -> `InitializeComponent()` -> shell modular visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a definir el marco de una pantalla de operacion antes de cargar su contenido.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana no tendria su estructura base.
    /// </summary>
    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Sistema Modular";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(980, 620);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        Font = new Font("Segoe UI", 9F);

        _blazorWebView.Dock = DockStyle.Fill;
        _blazorWebView.Location = new Point(0, 0);
        _blazorWebView.Name = "cardSystemBlazorWebView";
        _blazorWebView.Size = ClientSize;
        _blazorWebView.TabIndex = 0;

        Controls.Add(_blazorWebView);
        ResumeLayout(false);
    }

    // [ACURATEX] Registra servicios para que la shell Razor pueda ejecutarse.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para registrar los servicios necesarios para que la shell Razor
    /// pueda operar dentro de WinForms.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor despues de crear la parte visual.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez al iniciar la ventana modular.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Construye el contenedor de servicios y monta `CardSystemShell`.
    ///
    /// [FLUJO ACURATEX]
    /// Constructor -> `ConfigureBlazor()` -> servicios -> Razor modular.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enlazar los perifericos de una pantalla compleja antes de entrar al loop.
    ///
    /// [SI NO EXISTIERA]
    /// La shell modular no podria renderizarse ni hablar con sus servicios.
    /// </summary>
    private void ConfigureBlazor()
    {
        // [C#] `ServiceCollection` es el contenedor donde se registran dependencias para Blazor.
        // [ACURATEX] Aqui se arma el "cableado" de la shell modular antes de mostrarla.
        ServiceCollection services = new();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<IWindowChromeHost>(_ => new WindowChromeHost(this, "Sistema Modular", canMinimize: true, canMaximize: true));
        services.AddSingleton<IAppAlertService>(_alertService);
        services.AddSingleton<IConnectionController>(_connection);
        services.AddSingleton(_roleService);
        services.AddSingleton(_authState);
        services.AddSingleton(_permissionService);
        services.AddSingleton<ICommandFileTransferService>(_ => new CommandFileTransferService(_connection));
        services.AddSingleton<ITesterWifiConfigService>(_ => new TesterWifiConfigService(_connection));
        services.AddSingleton<CanAlarmDetector>();
        services.AddSingleton<IHeadProfileService, HeadProfileService>();
        services.AddSingleton<IAppScriptExecutionService, AppScriptExecutionService>();
        services.AddSingleton<IHeadStateEventParser, HeadStateEventParser>();
        services.AddSingleton<IEmergencyStopService>(_emergencyStopService);
        services.AddScoped<IServoDashboardTarjetasCommandService, ServoDashboardTarjetasCommandService>();
        services.AddScoped<ICabezalDashboardTarjetasCommandService, FastDashboardCommandService>();

        _blazorServices = services.BuildServiceProvider();
        _blazorWebView.HostPage = @"wwwroot\index-card-system-shell.html";
        _blazorWebView.Services = _blazorServices;
        _blazorWebView.RootComponents.Add<CardSystemShell>("#app");
    }

    // [ACURATEX] Responde a la perdida de conexion con aviso sonoro y cierre.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para reaccionar cuando la conexion se pierde y cerrar la ventana
    /// modular de forma controlada.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el evento `ConnectionLost` del controlador de conexion.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el enlace con el equipo cae mientras la shell esta abierta.
    ///
    /// [ENTRADAS]
    /// No recibe entradas directas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Reproduce un sonido, muestra una alerta y cierra el formulario si sigue vivo.
    ///
    /// [FLUJO ACURATEX]
    /// Conexión perdida -> `OnConnectionLost()` -> alerta -> cierre.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de fallo de comunicacion que obliga a salir de pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// La shell modular podria quedarse abierta aunque el enlace ya no exista.
    /// </summary>
    private async void OnConnectionLost()
    {
        // [ACURATEX] La pérdida de conexión corta el flujo y avisa con un sonido y una alerta.
        if (InvokeRequired) {
            // [C#] `BeginInvoke` reprograma la ejecución en el hilo correcto de la ventana.
            BeginInvoke(OnConnectionLost);
            return;
        }

        SystemSounds.Exclamation.Play();
        await _alertService.ShowAsync(
            "Conexión perdida",
            "El sistema está desconectado. Esta ventana se cerrará.",
            AppAlertKind.Warning);

        if (!IsDisposed) {
            Close();
        }
    }

    // [ACURATEX] Desuscribe eventos y libera el contenedor de servicios.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para limpiar la suscripcion de perdida de conexion y liberar el
    /// contenedor de servicios al cerrar la ventana.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms despues de cerrar el formulario.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al terminar la vida util de la ventana.
    ///
    /// [ENTRADAS]
    /// Recibe el evento base de cierre.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desuscribe `ConnectionLost` y descarta `_blazorServices`.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de ventana -> `OnFormClosed()` -> limpieza final.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a retirar una interrupcion y soltar recursos al apagar un subsistema.
    ///
    /// [SI NO EXISTIERA]
    /// Quedarian callbacks y servicios activos despues del cierre.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // [FLUJO] Al cerrar la ventana se limpia el evento para no dejar callbacks vivos.
        // [ACURATEX] También se libera el contenedor de servicios de la shell modular.
        _connection.ConnectionLost -= OnConnectionLost;
        _blazorServices?.Dispose();
        base.OnFormClosed(e);
    }
}



