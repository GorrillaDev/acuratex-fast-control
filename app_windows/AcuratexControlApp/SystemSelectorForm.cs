// [ACURATEX] Selector de sistema que abre la interfaz unificada o modular segun sesion y conexion.
// [FLUJO] UI de selector -> validacion de conexion -> apertura de Form unificado o modular.
using System.Drawing;
using AcuratexControlApp.Components;
using AcuratexControlApp.Services;
using AcuratexControlApp.Services.Auth;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para mostrar un selector de sistema antes de abrir la shell final.
///
/// [QUIEN LA LLAMA]
/// La llama el arranque principal cuando el usuario ya se autentico.
///
/// [CUANDO SE EJECUTA]
/// Se ejecuta al decidir entre sistema unificado o modular.
///
/// [ENTRADAS]
/// Recibe o usa en memoria la conexion y el contexto de autenticacion.
///
/// [SALIDAS]
/// Devuelve la ventana seleccionada o cancela el flujo.
///
/// [EFECTOS SECUNDARIOS]
/// Suscribe/desuscribe eventos de conexion y crea el formulario elegido.
///
/// [FLUJO ACURATEX]
/// Login -> selector -> validacion de conexion -> `UnifiedSystemForm` o `CardSystemForm`.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un menu de arranque que decide que modo operativo va a cargar.
///
/// [SI NO EXISTIERA]
/// El usuario tendria que abrir manualmente el sistema correcto sin ayuda de la app.
/// </summary>
public sealed class SystemSelectorForm : Form, ISystemSelectorHost
{
    // [ACURATEX] Host Blazor que dibuja el selector visual.
    private readonly BlazorWebView _blazorWebView = new();
    // [ACURATEX] Servicio de alertas para mostrar errores de conexion.
    private readonly AppAlertService _alertService = new();
    private RoleService? _roleService;
    private AuthStateService? _authState;
    private PermissionService? _permissionService;
    private IConnectionController? _connection;
    private ServiceProvider? _services;

    // [ACURATEX] Guarda el formulario final elegido por el usuario.
    public Form? SelectedSystemForm { get; private set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// El constructor existe para preparar la ventana y su contenido Blazor.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el flujo de arranque despues del login.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al crear el selector de sistema.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve la ventana lista para mostrar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Inicializa UI y registra dependencias del selector.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `SystemSelectorForm` -> `ConfigureBlazor()` -> vista selector.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un menu de boot que pregunta que perfil cargar.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una pantalla intermedia para escoger el sistema operativo de la app.
    /// </summary>
    public SystemSelectorForm()
    {
        InitializeComponent();
        ConfigureBlazor();
    }

    // [ACURATEX] Inyecta o reemplaza la conexion activa y mantiene sincronizado el evento de perdida.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para inyectar o reemplazar la conexion activa que el selector va a
    /// usar al abrir una de las dos shells del sistema.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el codigo de arranque que prepara el selector antes de mostrarlo.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta antes de seleccionar el sistema unificado o el modular.
    ///
    /// [ENTRADAS]
    /// Recibe la instancia de conexion actual.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desuscribe y vuelve a suscribir el evento `ConnectionLost`.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `SetConnection()` -> selector listo para abrir shells.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enchufar el bus de comunicacion antes de elegir que periferico usar.
    ///
    /// [SI NO EXISTIERA]
    /// El selector no sabria con que conexion abrir la siguiente ventana.
    /// </summary>
    public void SetConnection(IConnectionController connection)
    {
        // [FLUJO] La referencia antigua se desuscribe antes de registrar la nueva.
        // [C#] Esto evita dejar dos callbacks vivos apuntando a ventanas distintas.
        if (_connection != null) {
            _connection.ConnectionLost -= OnConnectionLost;
        }

        _connection = connection;
        _connection.ConnectionLost += OnConnectionLost;
    }

    // [ACURATEX] Guarda el contexto de autenticacion y permisos para el selector.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para entregar al selector el contexto de autenticacion y permisos
    /// que luego recibiran las ventanas hijas.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la capa de arranque antes de mostrar el selector.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una vez, o cada vez que se quiera cambiar el contexto compartido.
    ///
    /// [ENTRADAS]
    /// Recibe servicios de roles, autenticacion y permisos.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda referencias para construir luego las shells correctas.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `SetAuthContext()` -> selector -> shell elegida.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a precargar tablas de usuario y permisos antes de arrancar un menu.
    ///
    /// [SI NO EXISTIERA]
    /// El selector tendria que recrear servicios en cada apertura.
    /// </summary>
    public void SetAuthContext(RoleService roleService, AuthStateService authState, PermissionService permissionService)
    {
        // [ACURATEX] El selector solo guarda contexto; no decide permisos por su cuenta.
        _roleService = roleService;
        _authState = authState;
        _permissionService = permissionService;
    }

    // [ACURATEX] Abre la shell unificada si la conexion ya esta lista.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para abrir la shell unificada desde el selector.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la vista Razor cuando el operador elige el sistema unificado.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al pulsar la opcion de sistema unificado.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea para integrarse con el flujo asincrono de la UI.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede construir y devolver un `UnifiedSystemForm`.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> `SelectUnifiedSystemAsync()` -> shell unificada.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar el modo de arranque a un perfil de operacion distinto.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no podria abrir la shell unificada desde el selector.
    /// </summary>
    public Task SelectUnifiedSystemAsync()
    {
        // [FLUJO] Si no llegó contexto previo, se crean instancias mínimas para no romper el flujo.
        // [ACURATEX] El selector no monta el sistema por sí mismo: solo fabrica el formulario correcto.
        RoleService roleService = _roleService ?? new RoleService();
        AuthStateService authState = _authState ?? new AuthStateService(roleService);
        PermissionService permissionService = _permissionService ?? new PermissionService(authState, roleService);
        return SelectSystemAsync(() => new UnifiedSystemForm(_connection!, authState, permissionService));
    }

    // [ACURATEX] Abre la shell modular si la conexion ya esta lista.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para abrir la shell modular desde el selector.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la vista Razor cuando el operador elige el sistema modular.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al pulsar la opcion de sistema modular.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea para integrarse con la UI.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede construir y devolver un `CardSystemForm`.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> `SelectCardSystemAsync()` -> shell modular.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a seleccionar otro perfil de funcionamiento en un boot menu.
    ///
    /// [SI NO EXISTIERA]
    /// No podria abrirse la shell modular desde el selector.
    /// </summary>
    public Task SelectCardSystemAsync()
    {
        // [FLUJO] Igual que la shell unificada: el selector solo fabrica el formulario final.
        RoleService roleService = _roleService ?? new RoleService();
        AuthStateService authState = _authState ?? new AuthStateService(roleService);
        PermissionService permissionService = _permissionService ?? new PermissionService(authState, roleService);
        return SelectSystemAsync(() => new CardSystemForm(_connection!, roleService, authState, permissionService));
    }

    // [ACURATEX] Cierra el selector sin elegir sistema.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para cerrar el selector sin elegir ningun sistema.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la vista Razor cuando el usuario cancela la pantalla.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al abandonar el selector.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea completada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Marca la seleccion como cancelada y cierra la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Cancelar -> `CloseAsync()` -> selector cerrado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a salir de un menu sin ejecutar ninguna opcion.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendria una ruta clara para salir del selector.
    /// </summary>
    public Task CloseAsync()
    {
        CancelSelection();
        return Task.CompletedTask;
    }

    // [ACURATEX] Libera el host Blazor y los servicios del selector.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para liberar el host Blazor y el contenedor de servicios cuando el
    /// selector deja de existir.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms al cerrar la ventana.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante el cierre del selector.
    ///
    /// [ENTRADAS]
    /// Recibe la indicacion de si hay que liberar recursos administrados.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Descarta `_services` y `_blazorWebView`.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre del selector -> `Dispose(true)` -> limpieza.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar una pantalla de menu y soltar su runtime.
    ///
    /// [SI NO EXISTIERA]
    /// El selector podria dejar servicios colgando al cerrarse.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _services?.Dispose();
            _blazorWebView.Dispose();
        }

        base.Dispose(disposing);
    }

    // [ACURATEX] Configura el formulario sin Designer.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para construir la ventana del selector de sistema sin Designer.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al crear el selector.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Configura el marco visual y el control Blazor.
    ///
    /// [FLUJO ACURATEX]
    /// `SystemSelectorForm()` -> `InitializeComponent()` -> UI base.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a definir una pantalla de arranque antes de cargar su menu.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana no tendria su estructura inicial.
    /// </summary>
    private void InitializeComponent()
    {
        Text = "Seleccionar Sistema";
        StartPosition = FormStartPosition.CenterParent;
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = Padding.Empty;
        Margin = Padding.Empty;
        AutoScroll = false;
        MinimumSize = new Size(860, 430);
        ClientSize = new Size(860, 430);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(11, 19, 38);

        _blazorWebView.Dock = DockStyle.Fill;
        _blazorWebView.Location = new Point(0, 0);
        _blazorWebView.Margin = Padding.Empty;
        _blazorWebView.BackColor = Color.FromArgb(11, 19, 38);
        _blazorWebView.Name = "systemSelectorBlazorWebView";
        _blazorWebView.Size = ClientSize;
        _blazorWebView.TabIndex = 0;

        Controls.Add(_blazorWebView);
    }

    // [ACURATEX] Monta el host Blazor y registra el chrome propio del selector.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para registrar los servicios que la vista Razor del selector necesita.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor despues de la parte visual.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez al iniciar el selector.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea el provider y monta `SystemSelectorView`.
    ///
    /// [FLUJO ACURATEX]
    /// Constructor -> `ConfigureBlazor()` -> Razor selector.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a preparar los recursos de un menu antes de mostrarlo.
    ///
    /// [SI NO EXISTIERA]
    /// La vista Razor del selector no podria cargarse.
    /// </summary>
    private void ConfigureBlazor()
    {
        ServiceCollection services = new();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<IWindowChromeHost>(_ => new WindowChromeHost(this, "Seleccionar Sistema", canMinimize: false, canMaximize: false));
        services.AddSingleton<IAppAlertService>(_alertService);
        services.AddSingleton<ISystemSelectorHost>(this);

        _services = services.BuildServiceProvider();
        _blazorWebView.HostPage = "wwwroot\\index-system-selector.html";
        _blazorWebView.Services = _services;
        _blazorWebView.RootComponents.Add<SystemSelectorView>("#app");
    }

    // [ACURATEX] Construye y abre el formulario elegido solo si la conexion existe.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para crear el formulario elegido solo cuando la conexion es valida.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman `SelectUnifiedSystemAsync()` y `SelectCardSystemAsync()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al aceptar una opcion de sistema.
    ///
    /// [ENTRADAS]
    /// Recibe una fabrica de formularios.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea asincrona.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede abrir una ventana hija y cerrar el selector.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> `SelectSystemAsync()` -> validacion -> ventana final.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a decidir que modo de trabajo arrancar despues de validar periféricos.
    ///
    /// [SI NO EXISTIERA]
    /// La logica de abrir una shell final quedaria duplicada.
    /// </summary>
    private async Task SelectSystemAsync(Func<Form> formFactory)
    {
        // [FLUJO] La validación de conexión ocurre antes de crear la ventana final.
        if (IsDisposed) {
            return;
        }

        if (InvokeRequired) {
            if (IsHandleCreated) {
                // [C#] `BeginInvoke` vuelve al hilo de UI antes de construir la ventana hija.
                // [ACURATEX] El selector no debe abrir formularios desde un hilo de fondo.
                BeginInvoke(async () => await SelectSystemAsync(formFactory));
            }

            return;
        }

        if (!await EnsureConnectedAsync()) {
            return;
        }

        SelectSystemForm(formFactory());
    }

    // [ACURATEX] Cancela el selector y cierra la ventana.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para salir del selector sin abrir ninguna shell.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el boton o accion de cancelacion.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el operador aborta la seleccion.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Marca el dialogo como cancelado y lo cierra.
    ///
    /// [FLUJO ACURATEX]
    /// Cancelacion -> `CancelSelection()` -> cierre del selector.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a salir de un menu de arranque sin seleccionar una opcion.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una salida clara del selector sin abrir un sistema.
    /// </summary>
    private void CancelSelection()
    {
        // [FLUJO] Cancelar aquí significa cerrar la puerta antes de abrir otra shell.
        if (IsDisposed) {
            return;
        }

        if (InvokeRequired) {
            if (IsHandleCreated) {
                BeginInvoke(CancelSelection);
            }

            return;
        }

        DialogResult = DialogResult.Cancel;
        Close();
    }

    // [ACURATEX] Verifica que haya conexion antes de permitir abrir el sistema.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para verificar que la conexion siga activa antes de abrir una shell.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `SelectSystemAsync()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta antes de abrir el sistema unificado o modular.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` cuando la conexion esta disponible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mostrar una alerta si no hay conexion.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> `EnsureConnectedAsync()` -> validar o mostrar aviso.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar una bandera de enlace antes de cambiar de modo.
    ///
    /// [SI NO EXISTIERA]
    /// La shell podria abrirse aunque el equipo ya no estuviera conectado.
    /// </summary>
    private async Task<bool> EnsureConnectedAsync()
    {
        // [ACURATEX] Sin conexión activa no se deja abrir un sistema operativo completo.
        if (_connection != null && _connection.IsConnected) {
            return true;
        }

        // [ACURATEX] La alerta explica al operador por qué el selector no avanza.
        await _alertService.ShowAsync(
            "Sin conexión",
            "Error: el sistema no está conectado.",
            AppAlertKind.Warning);
        return false;
    }

    // [ACURATEX] Guarda la instancia del sistema elegido y cierra el selector.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para entregar el formulario elegido a la capa de arranque y cerrar
    /// el selector.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `SelectSystemAsync()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando la seleccion fue valida.
    ///
    /// [ENTRADAS]
    /// Recibe el formulario que se va a abrir.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda `SelectedSystemForm`, fija `DialogResult` y cierra la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// Seleccion valida -> `SelectSystemForm()` -> salida del selector.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a pasar un selector a una rutina que ya sabe que periferico abrir.
    ///
    /// [SI NO EXISTIERA]
    /// El formulario elegido no llegaria al punto que lo debe mostrar.
    /// </summary>
    private void SelectSystemForm(Form form)
    {
        // [FLUJO] El selector devuelve el formulario elegido al arranque principal.
        SelectedSystemForm = form;
        DialogResult = DialogResult.OK;
        Close();
    }

    // [ACURATEX] Cierra el selector si la conexion se cae mientras esta abierto.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para cerrar el selector si la conexion cae mientras esta abierto.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el evento `ConnectionLost` de la conexion activa.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el enlace se pierde durante la pantalla de seleccion.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra el selector.
    ///
    /// [FLUJO ACURATEX]
    /// Caida de conexion -> `OnConnectionLost()` -> selector cerrado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de fallo que obliga a salir de un menu.
    ///
    /// [SI NO EXISTIERA]
    /// El selector podria quedar abierto aunque ya no hubiera enlace.
    /// </summary>
    private void OnConnectionLost()
    {
        // [FLUJO] Una caída de enlace mientras el selector está abierto cancela la elección.
        if (InvokeRequired) {
            // [C#] `InvokeRequired` indica que este callback llegó desde otro hilo.
            BeginInvoke(OnConnectionLost);
            return;
        }

        Close();
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para desuscribir el evento de perdida de conexion al cerrar el
    /// selector.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms al terminar la ventana.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante el cierre final del selector.
    ///
    /// [ENTRADAS]
    /// Recibe los argumentos de cierre.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Quita la suscripcion a `ConnectionLost`.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre -> `OnFormClosed()` -> limpieza del selector.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a retirar un callback antes de apagar una pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// Quedaria una suscripcion colgada a la conexion activa.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_connection != null) {
            _connection.ConnectionLost -= OnConnectionLost;
        }

        base.OnFormClosed(e);
    }
}
