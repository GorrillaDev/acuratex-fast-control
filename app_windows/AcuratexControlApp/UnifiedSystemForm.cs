// [ACURATEX] Host WinForms para la shell unificada.
// [FLUJO] Conexion -> servicios -> BlazorWebView -> shell unificada.
using System.Drawing;
using AcuratexControlApp.Components;
using AcuratexControlApp.Services;
using AcuratexControlApp.Services.Auth;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para alojar la shell unificada dentro de una ventana WinForms.
///
/// [QUIEN LA LLAMA]
/// La llama el selector de sistema cuando el usuario eligió el modo unificado.
///
/// [CUANDO SE EJECUTA]
/// Se ejecuta mientras la interfaz unificada permanece abierta.
///
/// [ENTRADAS]
/// Recibe la conexion, la sesion y el servicio de permisos.
///
/// [SALIDAS]
/// Devuelve una ventana preparada para trabajar con Blazor.
///
/// [EFECTOS SECUNDARIOS]
/// Registra servicios, escucha desconexion y puede cerrar la ventana.
///
/// [FLUJO ACURATEX]
/// Selector -> `UnifiedSystemForm` -> BlazorWebView -> shell unificada.
///
/// [EQUIVALENCIA MCU]
/// Se parece al firmware principal que monta drivers, protocolos y la interfaz de operacion.
///
/// [SI NO EXISTIERA]
/// La vista unificada no tendria un host WinForms que sostenga su ciclo de vida.
/// </summary>
public sealed class UnifiedSystemForm : Form, IUnifiedSystemShellHost
{
    // [ACURATEX] Conexiones y servicios que la shell usa durante toda su vida.
    private readonly IConnectionController _connection;
    private readonly AuthStateService _authState;
    private readonly PermissionService _permissionService;
    private readonly EmergencyStopService _emergencyStopService;
    // [ACURATEX] Control Razor hospedado dentro de WinForms.
    private readonly BlazorWebView _blazorWebView = new();
    // [ACURATEX] Catalogo de comandos cargado una sola vez.
    private readonly IReadOnlyList<CommandDefinition> _commands = CommandCatalog.Load();
    // [ACURATEX] Servicio para informar fallos de conexion al operador.
    private readonly AppAlertService _alertService = new();
    private ServiceProvider? _blazorServices;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// El constructor existe para recibir los servicios base y preparar la shell unificada.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el selector de sistema.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al abrir la interfaz unificada.
    ///
    /// [ENTRADAS]
    /// Recibe la conexion, la autenticacion y los permisos.
    ///
    /// [SALIDAS]
    /// Devuelve la ventana ya inicializada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea el servicio de emergencia y registra eventos de conexion.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> constructor -> servicios -> shell unificada.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un arranque de sistema que enlaza comunicación, seguridad y UI.
    ///
    /// [SI NO EXISTIERA]
    /// La shell unificada no podria recibir sus dependencias principales.
    /// </summary>
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para reunir las dependencias que necesita la shell unificada y
    /// crear el servicio de emergencia asociado.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el flujo de arranque cuando el usuario elige el sistema unificado.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al crear la ventana principal del modo unificado.
    ///
    /// [ENTRADAS]
    /// Recibe la conexion, la autenticacion y el servicio de permisos.
    ///
    /// [SALIDAS]
    /// Devuelve la ventana lista para hospedar Razor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Construye el servicio de emergencia, conecta el evento de perdida de enlace y
    /// prepara Blazor.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `UnifiedSystemForm()` -> servicios -> shell unificada.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar un sistema completo con sus buses, seguridad y pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// La shell unificada no podria recibir sus dependencias principales.
    /// </summary>
    public UnifiedSystemForm(
        IConnectionController connection,
        AuthStateService authState,
        PermissionService permissionService)
    {
        // [FLUJO] El formulario guarda dependencias y luego monta la shell Razor.
        _connection = connection;
        _authState = authState;
        _permissionService = permissionService;
        _emergencyStopService = new EmergencyStopService(_connection, _authState, "Sistema Unificado");

        InitializeComponent();
        _connection.ConnectionLost += OnConnectionLost;
        ConfigureBlazor();
    }

    // [ACURATEX] La UI lee este catalogo para construir botones y paneles.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para exponer el catalogo de comandos que la UI mostrara como
    /// acciones disponibles.
    ///
    /// [QUIEN LA USA]
    /// La usan los componentes Razor de la shell unificada.
    ///
    /// [CUANDO SE USA]
    /// Se consulta al construir botones, paneles y accesos rapidos.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la lista de comandos cargada desde `CommandCatalog`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Shell unificada -> `Commands` -> botones de comando.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a consultar una tabla de acciones permitidas antes de habilitar una pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que reconstruir su catalogo de comandos por su cuenta.
    /// </summary>
    public IReadOnlyList<CommandDefinition> Commands => _commands;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para mandar un comando textual al firmware a traves de la conexion.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los componentes Razor de la shell unificada.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el operador pulsa un comando disponible.
    ///
    /// [ENTRADAS]
    /// Recibe la definicion del comando a enviar.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea asincrona.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía una linea al firmware o muestra una alerta si no hay conexion.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `SendCommandAsync()` -> `ConnectionController.SendLineAsync()` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una orden en el puerto serial o en el bus de comunicacion.
    ///
    /// [SI NO EXISTIERA]
    /// Cada componente tendria que hablar con la conexion directamente.
    /// </summary>
    public async Task SendCommandAsync(CommandDefinition command)
    {
        // [ACURATEX] Si no hay conexión, no se intenta mandar un comando vacío hacia el firmware.
        if (!_connection.IsConnected) {
            // [ACURATEX] El aviso mantiene visible la causa del rechazo sin lanzar una excepción al operador.
            await _alertService.ShowAsync(
                "Sin conexión",
                "Error: el sistema no está conectado.",
                AppAlertKind.Warning);
            return;
        }

        try {
            await _connection.SendLineAsync(command.Command, CancellationToken.None);
        } catch (Exception ex) {
            MessageBox.Show(
                this,
                $"No se pudo enviar el comando: {ex.Message}",
                "Error de envio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // [ACURATEX] Libera la shell y el host Blazor al cerrar la ventana.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para liberar el servicio de emergencia y el host Blazor cuando la
    /// ventana se cierra.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms al cerrar la shell unificada.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante el cierre del formulario.
    ///
    /// [ENTRADAS]
    /// Recibe la indicacion de si hay que liberar recursos administrados.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispone `_emergencyStopService` y `_blazorWebView`.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de shell -> `Dispose(true)` -> limpieza.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar un sistema de control y liberar sus perifericos.
    ///
    /// [SI NO EXISTIERA]
    /// La shell podria dejar recursos activos al salir.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _emergencyStopService.Dispose();
            _blazorWebView.Dispose();
        }

        base.Dispose(disposing);
    }

    // [ACURATEX] Configura el formulario WinForms sin depender del Designer.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para construir manualmente la ventana de la shell unificada.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor.
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
    /// Configura tamaño, titulo y control Blazor principal.
    ///
    /// [FLUJO ACURATEX]
    /// `UnifiedSystemForm()` -> `InitializeComponent()` -> shell visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a definir el marco de una pantalla principal antes de arrancar el firmware.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana no tendria su base visual.
    /// </summary>
    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Sistema Unificado";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(980, 620);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        Font = new Font("Segoe UI", 9F);

        _blazorWebView.Dock = DockStyle.Fill;
        _blazorWebView.Location = new Point(0, 0);
        _blazorWebView.Name = "unifiedSystemBlazorWebView";
        _blazorWebView.Size = ClientSize;
        _blazorWebView.TabIndex = 0;

        Controls.Add(_blazorWebView);
        ResumeLayout(false);
    }

    // [ACURATEX] Registra servicios para que la shell Razor funcione dentro del form.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para registrar los servicios que la shell Razor necesita para
    /// funcionar dentro de WinForms.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor despues de crear la parte visual.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez al iniciar la shell.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Construye el provider y monta `UnifiedSystemShell`.
    ///
    /// [FLUJO ACURATEX]
    /// Constructor -> `ConfigureBlazor()` -> DI -> Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a preparar el runtime de una pantalla compleja antes de mostrarla.
    ///
    /// [SI NO EXISTIERA]
    /// La shell no podria renderizar sus componentes.
    /// </summary>
    private void ConfigureBlazor()
    {
        // [FLUJO] El host WinForms actúa como contenedor de servicios para la shell Razor.
        ServiceCollection services = new();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<IWindowChromeHost>(_ => new WindowChromeHost(this, "Sistema Unificado", canMinimize: true, canMaximize: true));
        services.AddSingleton<IAppAlertService>(_alertService);
        services.AddSingleton<IConnectionController>(_connection);
        // [ACURATEX] La sesión y los permisos también viajan por DI para que la shell no los recree.
        services.AddSingleton(_authState);
        services.AddSingleton(_permissionService);
        services.AddSingleton<ICommandFileTransferService>(_ => new CommandFileTransferService(_connection));
        services.AddSingleton<IHeadProfileService, HeadProfileService>();
        services.AddSingleton<IAppScriptExecutionService, AppScriptExecutionService>();
        services.AddSingleton<IHeadStateEventParser, HeadStateEventParser>();
        services.AddSingleton<IEmergencyStopService>(_emergencyStopService);
        services.AddSingleton<IUnifiedSystemShellHost>(this);
        services.AddScoped<IServoDashboardUnificadoCommandService, ServoDashboardUnificadoCommandService>();
        services.AddScoped<ICabezalDashboardUnificadoCommandService, CabezalDashboardUnificadoCommandService>();

        _blazorServices = services.BuildServiceProvider();
        _blazorWebView.HostPage = @"wwwroot\index-unified-system-shell.html";
        _blazorWebView.Services = _blazorServices;
        _blazorWebView.RootComponents.Add<UnifiedSystemShell>("#app");
    }

    // [ACURATEX] Cierra la ventana si la conexion con el equipo se pierde.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para cerrar la shell unificada cuando la conexion se pierde.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el evento `ConnectionLost` del controlador.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el enlace cae mientras la shell esta abierta.
    ///
    /// [ENTRADAS]
    /// No recibe entradas directas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Caida de conexion -> `OnConnectionLost()` -> cierre de shell.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de fallo que obliga a salir de la pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// La shell podria quedarse abierta sin enlace activo.
    /// </summary>
    private void OnConnectionLost()
    {
        // [FLUJO] La shell unificada se cierra si el enlace cae durante la sesión.
        if (InvokeRequired) {
            // [C#] `BeginInvoke` evita tocar WinForms desde un hilo de evento ajeno.
            // [ACURATEX] La ventana solo puede cerrarse desde su propio hilo de UI.
            BeginInvoke(OnConnectionLost);
            return;
        }

        Close();
    }

    // [ACURATEX] Desuscribe eventos y libera el contenedor de servicios.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para desuscribir el evento de perdida de conexion y liberar el
    /// contenedor de servicios de la shell unificada.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms al cerrar la ventana.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al terminar la vida util del formulario.
    ///
    /// [ENTRADAS]
    /// Recibe los argumentos del cierre.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desengancha `ConnectionLost` y descarta `_blazorServices`.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre -> `OnFormClosed()` -> limpieza de la shell.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a retirar callbacks y liberar runtime antes de apagar.
    ///
    /// [SI NO EXISTIERA]
    /// Quedarian suscripciones y servicios vivos despues del cierre.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // [ACURATEX] La interfaz unificada libera el host Blazor para no dejar servicios vivos.
        _connection.ConnectionLost -= OnConnectionLost;
        _blazorServices?.Dispose();
        base.OnFormClosed(e);
    }
}
