// [ACURATEX] Esta es la ventana principal de la aplicación.
// Coordina conexión, autenticación, estado visual y el puente con componentes Blazor.
using System.Drawing;
using System.IO.Ports;
using AcuratexControlApp.Components;
using AcuratexControlApp.Services;
using AcuratexControlApp.Services.Auth;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AcuratexControlApp;

// [C#] `partial` indica que la definición de la clase está repartida en varios archivos.
// [C#] `Form` la convierte en una ventana WinForms.
// [ACURATEX] `IMainControlPanelHost` es el contrato que Blazor usa para hablar con esta ventana.
public partial class Form1 : Form, IMainControlPanelHost
{
    // [C#] `readonly` evita reemplazar la referencia después de construir la ventana.
    // [ACURATEX] Este objeto centraliza toda la comunicación con el ESP32 o el transporte elegido.
    private readonly IConnectionController _connection = new ConnectionController();
    // [ACURATEX] Este estado guarda lo que la interfaz muestra: modo, host, puerto, logs, etc.
    private readonly MainControlPanelState _state = new();
    // [ACURATEX] Servicio de alertas visuales para mostrar mensajes al usuario.
    private readonly AppAlertService _alertService = new();
    // [ACURATEX] Servicio de roles y permisos para decidir qué acciones puede ejecutar cada usuario.
    private readonly RoleService _roleService = new();
    // [ACURATEX] Servicio que administra usuarios de demostración.
    private readonly DemoUsersService _demoUsersService = new();
    // [C#] No se inicializa aquí porque necesita `_roleService`.
    // [ACURATEX] Representa el estado de autenticación de la sesión actual.
    private readonly AuthStateService _authState;
    // [C#] También depende de otros servicios, por eso se asigna en el constructor.
    // [ACURATEX] Decide si una acción concreta está permitida.
    private readonly PermissionService _permissionService;
    // [C#] `List<T>` es una lista genérica de elementos del tipo indicado.
    // [ACURATEX] Cache local de dispositivos USB encontrados por WinUSB.
    private readonly List<UsbVendorDeviceInfo> _usbDevices = new();
    // [ACURATEX] Cache local de puertos seriales del equipo.
    private readonly List<string> _serialPorts = new();
    // [ACURATEX] Marca que la desconexión fue intencional y no una caída del enlace.
    private bool _intentionalDisconnect;
    // [ACURATEX] Indica que la ventana principal está escondida porque se abrió otra interfaz.
    private bool _hiddenForSystemInterface;
    // [ACURATEX] Guarda si la ventana estaba visible en la barra de tareas antes de ocultarse.
    private bool _showInTaskbarBeforeSystemInterface = true;
    // [ACURATEX] Guarda el estado de ventana para restaurarlo al cerrar la interfaz secundaria.
    private FormWindowState _windowStateBeforeSystemInterface = FormWindowState.Normal;
    // [ACURATEX] Referencia a la interfaz secundaria actualmente abierta.
    private Form? _activeSystemInterface;
    // [C#] `ServiceProvider?` es el contenedor de dependencias de la vista Razor.
    private ServiceProvider? _blazorServices;
    // [ACURATEX] Icono principal de la ventana.
    private Icon? _appIcon;
    // [ACURATEX] Bloquea la apertura simultánea de varios diálogos de login.
    private bool _loginDialogOpen;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para montar la ventana principal y dejarla lista para operar
    /// con la conexión, el estado visual y el host Blazor.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `Application.Run(new Form1())` desde `Program.Main()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta una sola vez cuando la app ya terminó el splash.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea servicios, registra eventos, fija valores iniciales y carga Blazor.
    ///
    /// [FLUJO ACURATEX]
    /// Program.Main -> new Form1() -> eventos de conexión -> host Blazor -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al montaje de un sistema embebido donde se crean controladores,
    /// se enlazan callbacks y luego se entra al funcionamiento normal.
    ///
    /// [SI NO EXISTIERA]
    /// No habría ventana principal ni coordinación entre UI y conexión.
    /// </summary>
    public Form1()
    {
        // [ACURATEX] Primero se construyen los servicios que dependen de otros servicios.
        _authState = new AuthStateService(_roleService);
        _permissionService = new PermissionService(_authState, _roleService);

        // [ACURATEX] Monta la ventana WinForms y sus controles base.
        InitializeComponent();
        // [ACURATEX] Crea el icono visual de la ventana.
        _appIcon = AppIconFactory.CreateIcon();
        Icon = _appIcon;

        // [C#] `+=` suscribe métodos a eventos del controlador de conexión.
        // [ACURATEX] La app escucha líneas entrantes y caídas de enlace.
        _connection.LineReceived += OnLineReceived;
        _connection.LineSent += OnLineSent;
        _connection.ConnectionLost += OnConnectionLost;

        // [ACURATEX] Valores iniciales visibles en la UI antes de conectar.
        _state.Host = "192.168.137.2";
        _state.Port = "3333";
        _state.Command = "320 07";
        _state.Mode = ConnectionMode.Usb;

        // [ACURATEX] Ajusta la UI al modo inicial y prepara endpoints.
        ApplyConnectionModeUi(refreshEndpoints: true);
        // [ACURATEX] Sincroniza flags de botones y estado.
        UpdateUiState(false);
        // [ACURATEX] Conecta WinForms con Blazor para que la UI Razor funcione dentro de la ventana.
        ConfigureBlazor();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad expone el estado compartido de la pantalla principal para los
    /// componentes Razor que viven dentro de WinForms.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los componentes Blazor y el contenedor de dependencias.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se evalúa cada vez que la interfaz necesita leer el estado actual.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve el objeto `MainControlPanelState` administrado por la ventana.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica nada por sí misma; solo expone una referencia.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> host WinForms -> State.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer registros de estado compartidos.
    ///
    /// [SI NO EXISTIERA]
    /// La UI Blazor no tendría acceso al estado centralizado.
    /// </summary>
    public MainControlPanelState State => _state;

    // [C#] `event Action?` permite notificar que algo cambió sin acoplar la ventana
    // a quién escucha.
    public event Action? StateChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cambiar el modo de conexión que la UI va a usar.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los componentes Razor cuando el usuario cambia USB, WiFi o Serial.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario selecciona un modo distinto de conexión.
    ///
    /// [ENTRADAS]
    /// Recibe el modo solicitado.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` para integrarse con llamadas `async` de Razor, aunque aquí
    /// la operación sea inmediata.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza estado, refresca endpoints y notifica cambios visuales.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.SetConnectionModeAsync() -> estado de UI -> botones y endpoints.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar la configuración de un periférico antes de usarlo.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría sincronizar el modo de transporte con la ventana principal.
    /// </summary>
    public Task SetConnectionModeAsync(ConnectionMode mode)
    {
        // [C#] Esta validación evita dejar la app en un modo todavía no soportado por la UI.
        if (mode == ConnectionMode.Serial) {
            mode = ConnectionMode.Usb;
        }

        if (_state.Mode == mode) {
            return Task.CompletedTask;
        }

        _state.Mode = mode;
        ApplyConnectionModeUi(refreshEndpoints: true);
        UpdateUiState(_connection.IsConnected);
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para guardar el dispositivo seleccionado por la UI.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la pantalla Razor cuando el usuario elige un endpoint USB o serial.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando cambia la selección del elemento de conexión.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador del endpoint.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` para encajar con el modelo asíncrono de la UI.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza `_state.SelectedEndpointId`.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.SetSelectedEndpointAsync() -> estado compartido.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una selección en una variable global de configuración.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría recordar qué dispositivo eligió el usuario.
    /// </summary>
    public Task SetSelectedEndpointAsync(string endpointId)
    {
        // [ACURATEX] La selección queda almacenada aunque el enlace todavía no esté abierto.
        _state.SelectedEndpointId = endpointId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para guardar el host de red que el usuario escribió.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI Razor cuando cambia el cuadro de texto de host.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario modifica la dirección del equipo remoto.
    ///
    /// [ENTRADAS]
    /// Recibe una cadena con el host.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` sin resultado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica `_state.Host`.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.SetHostAsync() -> estado -> conexión posterior.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a actualizar una dirección de destino en una tabla de configuración.
    ///
    /// [SI NO EXISTIERA]
    /// El formulario no sabría qué IP usar al conectar por red.
    /// </summary>
    public Task SetHostAsync(string host)
    {
        // [ACURATEX] El host se escribe aquí para que la pantalla recuerde el destino antes de conectar.
        _state.Host = host;
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para guardar el puerto TCP escrito por el usuario.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI Razor al editar el puerto.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cada vez que el usuario cambia el número de puerto.
    ///
    /// [ENTRADAS]
    /// Recibe una cadena con el puerto.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica `_state.Port`.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.SetPortAsync() -> estado -> futura conexión TCP.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar un parámetro de comunicación antes de abrir el canal.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podría recordar el puerto TCP objetivo.
    /// </summary>
    public Task SetPortAsync(string port)
    {
        // [ACURATEX] El puerto se guarda como texto porque ese es el formato natural del input HTML.
        _state.Port = port;
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para guardar el baudrate elegido para comunicación serial.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI cuando el usuario modifica la velocidad serial.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al editar el valor de baud.
    ///
    /// [ENTRADAS]
    /// Recibe la cadena con el baudrate.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica `_state.BaudValue`.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.SetBaudAsync() -> estado -> futura apertura serial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar la velocidad UART antes de inicializarla.
    ///
    /// [SI NO EXISTIERA]
    /// No habría forma de recordar la velocidad serial pedida por el usuario.
    /// </summary>
    public Task SetBaudAsync(string baud)
    {
        // [ACURATEX] El baudrate también llega como texto desde la UI y se valida después.
        _state.BaudValue = baud;
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para guardar el texto del comando manual que el usuario va a enviar.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI Razor al editar el cuadro de comando.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cada vez que el usuario cambia el comando.
    ///
    /// [ENTRADAS]
    /// Recibe una cadena de comando.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica `_state.Command`.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.SetCommandAsync() -> estado -> envío posterior.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una trama en un buffer antes de transmitirla.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana no podría almacenar el comando manual editable.
    /// </summary>
    public Task SetCommandAsync(string command)
    {
        // [ACURATEX] La cadena se guarda primero en el estado compartido para que la UI la repinte
        // y luego pueda enviarse sin volver a escribirla.
        _state.Command = command;
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para refrescar la lista de endpoints detectados.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI cuando el usuario pulsa refrescar.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta bajo demanda, no en segundo plano.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` completado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Reescanea USB o puertos seriales según el modo actual.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.RefreshEndpointsAsync() -> enumeración de transporte -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a releer puertos o periféricos conectados.
    ///
    /// [SI NO EXISTIERA]
    /// El usuario no tendría una acción explícita para volver a detectar dispositivos.
    /// </summary>
    public Task RefreshEndpointsAsync()
    {
        // [ACURATEX] Este refresco no habla con el firmware; solo vuelve a enumerar medios disponibles.
        RefreshEndpointList();
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para buscar equipos Acuratex accesibles por red local.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI cuando el usuario pulsa buscar por WiFi.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta bajo demanda, solo cuando el modo seleccionado es WiFi.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` porque la búsqueda de red es asíncrona.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica `_state.IsDiscovering`, `Host`, `Port` y el log.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> Form1.DiscoverWifiAsync() -> UDP discovery -> selección de dispositivo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un barrido de red o a un escaneo de buses en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// El usuario tendría que escribir manualmente la IP y el puerto del equipo.
    /// </summary>
    public async Task DiscoverWifiAsync()
    {
        // [ACURATEX] Discovery busca un tester por red sin pedir IP manual.
        // [FLUJO] Boton descubrir -> AcuratexNetworkDiscovery -> lista de candidatos -> UI.
        if (_state.IsDiscovering || _connection.IsConnected || GetSelectedMode() != ConnectionMode.Wifi) {
            return;
        }

        _state.IsDiscovering = true;
        UpdateUiState(_connection.IsConnected);
        NotifyStateChanged();

        try {
            // [ACURATEX] El log explica al usuario que se está buscando el dispositivo por red.
            AppendLog($"Buscando equipos Acuratex por UDP/{AcuratexNetworkDiscovery.DiscoveryPort}...");
            IReadOnlyList<AcuratexNetworkDeviceInfo> devices = await AcuratexNetworkDiscovery
                .DiscoverAsync(TimeSpan.FromSeconds(3), CancellationToken.None)
                .ConfigureAwait(true);

            if (devices.Count == 0) {
                AppendLog("No se detectaron equipos por red.");
                return;
            }

            // [ACURATEX] Se toma el primer resultado como candidato principal.
            AcuratexNetworkDeviceInfo selected = devices[0];
            _state.Host = selected.Host;
            _state.Port = selected.TcpPort.ToString();

            AppendLog($"Detectado: {selected}");
            foreach (AcuratexNetworkDeviceInfo device in devices.Skip(1)) {
                AppendLog($"Detectado adicional: {device}");
            }
        } catch (Exception ex) {
            AppendLog($"ERROR discovery: {ex.Message}");
        } finally {
            _state.IsDiscovering = false;
            UpdateUiState(_connection.IsConnected);
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abrir el transporte elegido y dejar la app lista para
    /// enviar y recibir líneas.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI cuando el usuario pulsa Conectar.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al iniciar una sesión de comunicación con el ESP32 o el transporte
    /// seleccionado.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros directos; usa el estado compartido.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` porque la apertura del enlace puede esperar operaciones de E/S.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia flags de estado, abre transporte, refresca endpoints y escribe en el log.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> Form1.ConnectAsync() -> IConnectionController -> transporte -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar un periférico serie o un enlace de comunicación antes de usarlo.
    ///
    /// [SI NO EXISTIERA]
    /// No habría una única ruta para conectar desde la pantalla principal.
    /// </summary>
    public async Task ConnectAsync()
    {
        // [ACURATEX] Conectar es una secuencia completa: preparar UI, abrir enlace y sincronizar estado.
        if (_state.IsBusy || _connection.IsConnected) {
            return;
        }

        _state.IsBusy = true;
        UpdateUiState(false);
        NotifyStateChanged();

        try {
            // [ACURATEX] El modo decide si se usa USB, WiFi o serial.
            ConnectionMode mode = GetSelectedMode();
            if (mode == ConnectionMode.Usb || mode == ConnectionMode.Serial) {
                // [ACURATEX] Antes de abrir, conviene leer de nuevo qué endpoints existen.
                RefreshEndpointList();
            }

            await _connection.ConnectAsync(
                mode,
                GetSelectedUsbDevice(),
                _state.Host.Trim(),
                ParsePortFromUi(),
                GetSelectedSerialPort(),
                ParseBaudFromUi(),
                CancellationToken.None);

            // [ACURATEX] El log confirma qué transporte quedó activo.
            AppendLog($"Conectado por {mode}.");
            UpdateUiState(true);
        } catch (Exception ex) {
            AppendLog($"ERROR connect: {ex.Message}");
            UpdateUiState(false);
        } finally {
            _state.IsBusy = false;
            UpdateUiState(_connection.IsConnected);
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cerrar el enlace de manera explícita desde la UI.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI cuando el usuario pulsa Desconectar.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario decide terminar la sesión de comunicación.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra el transporte y actualiza el estado de la ventana.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> Form1.DisconnectAsync() -> ConnectionController.DisconnectAsync().
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a deshabilitar un periférico antes de apagarlo o desconectarlo.
    ///
    /// [SI NO EXISTIERA]
    /// El usuario no tendría un cierre limpio del enlace.
    /// </summary>
    public async Task DisconnectAsync()
    {
        // [ACURATEX] Esta ruta solo delega en la desconexion centralizada del controlador.
        await DisconnectTransportAsync();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para enviar el comando escrito manualmente por el usuario.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI al pulsar Enviar o una acción equivalente.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando hay un comando manual listo para transmitirse.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros; usa `_state.Command`.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía una línea al firmware, escribe en el log y puede disparar manejo de fallo.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> Form1.SendManualLineAsync() -> ConnectionController.SendLineAsync() -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar una trama en UART y esperar respuesta.
    ///
    /// [SI NO EXISTIERA]
    /// No habría envío manual de comandos desde el panel.
    /// </summary>
    public async Task SendManualLineAsync()
    {
        string line = _state.Command.Trim();
        if (string.IsNullOrWhiteSpace(line)) {
            return;
        }

        if (!_connection.IsConnected) {
            AppendLog("No hay conexion activa.");
            return;
        }

        try {
            await _connection.SendLineAsync(line, CancellationToken.None);
        } catch (Exception ex) {
            AppendLog($"ERROR send: {ex.Message}");
            await HandleTransportFaultAsync();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para disparar un comando predefinido sin que la UI repita lógica.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI al pulsar un botón de preset.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario selecciona un comando guardado.
    ///
    /// [ENTRADAS]
    /// Recibe el texto del comando preset.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el estado, notifica cambios y envía el comando.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> Form1.SendPresetAsync() -> estado -> SendManualLineAsync() -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a pulsar un botón que carga una orden fija en una máquina de estados.
    ///
    /// [SI NO EXISTIERA]
    /// Cada botón tendría que duplicar el envío manual.
    /// </summary>
    public async Task SendPresetAsync(string command)
    {
        _state.Command = command;
        NotifyStateChanged();
        await Task.Yield();
        await SendManualLineAsync();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abrir la interfaz gráfica secundaria del sistema.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los componentes Razor cuando el usuario pide abrir la GUI.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la app ya tiene sesión válida y quiere mostrar la interfaz de pruebas.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` para integrarse con eventos de la UI.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Programa una llamada al hilo de UI mediante `BeginInvoke`.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.LaunchGuiAsync() -> BeginInvoke -> interfaz del sistema.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a disparar un callback diferido desde una interrupción o tarea diferida.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendría un punto central para abrir la interfaz secundaria.
    /// </summary>
    public Task LaunchGuiAsync()
    {
        if (IsDisposed) {
            return Task.CompletedTask;
        }

        BeginInvoke(OpenSystemInterfaceWithSessionGuard);
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abrir el cuadro de login desde el hilo correcto.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los componentes Razor cuando el usuario necesita iniciar sesión.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pulsa entrar o cuando la UI requiere autenticación.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Programa el diálogo y evita abrir más de una instancia a la vez.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> Form1.OpenLoginAsync() -> BeginInvoke -> LoginForm.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a encolar una acción de interfaz para ejecutarla cuando el sistema esté libre.
    ///
    /// [SI NO EXISTIERA]
    /// El formulario de login podría abrirse desde un hilo incorrecto.
    /// </summary>
    public Task OpenLoginAsync()
    {
        if (_loginDialogOpen || IsDisposed) {
            return Task.CompletedTask;
        }

        BeginInvoke(ShowLoginDialog);
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para comprobar primero si hay sesión iniciada antes de abrir
    /// la interfaz secundaria.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `BeginInvoke` desde `LaunchGuiAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI ya programó la apertura de la interfaz del sistema.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor porque es `async void`, algo habitual en handlers de UI.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mostrar una alerta o abrir la interfaz del sistema.
    ///
    /// [FLUJO ACURATEX]
    /// BeginInvoke -> OpenSystemInterfaceWithSessionGuard -> validación de sesión -> interfaz.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un filtro previo antes de activar otra rutina de control.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz gráfica podría abrirse sin validar autenticación.
    /// </summary>
    private async void OpenSystemInterfaceWithSessionGuard()
    {
        if (!_authState.IsAuthenticated) {
            await _alertService.ShowAsync(
                "Sesion requerida",
                "Inicia sesion para abrir la interfaz grafica de pruebas.",
                AppAlertKind.Warning);
            return;
        }

        await OpenSystemInterfaceFlowAsync();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para mostrar el diálogo modal de inicio de sesión.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `BeginInvoke` desde `OpenLoginAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario solicita autenticarse.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Abre `LoginForm`, puede cambiar el estado de sesión y escribe en el log.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ShowLoginDialog -> LoginForm -> AuthStateService.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a abrir una ventana de configuración bloqueante antes de continuar.
    ///
    /// [SI NO EXISTIERA]
    /// No habría una rutina centralizada para el login modal.
    /// </summary>
    private void ShowLoginDialog()
    {
        if (_loginDialogOpen || IsDisposed) {
            return;
        }

        _loginDialogOpen = true;
        using LoginForm login = new(_appIcon, _authState);
        try {
            if (login.ShowDialog(this) == DialogResult.OK) {
                string userLabel = _authState.CurrentUser?.Username ?? "desconocido";
                string roleLabel = _authState.CurrentRole?.Name ?? "sin rol";
                AppendLog($"Sesion iniciada: {userLabel} ({roleLabel}).");
            }
        } finally {
            _loginDialogOpen = false;
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abrir la interfaz del sistema solo si hay conexión activa.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `OpenSystemInterfaceWithSessionGuard()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta después de confirmar que la sesión es válida.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` porque la validación de alertas y la apertura pueden ser asincrónicas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede abrir el selector de sistema, ocultar la ventana principal o cancelar.
    ///
    /// [FLUJO ACURATEX]
    /// Sesión válida -> conexión activa -> selector -> interfaz de sistema.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bifurcación de estado: solo si el sistema está listo pasa al siguiente paso.
    ///
    /// [SI NO EXISTIERA]
    /// La GUI secundaria no tendría control central de apertura.
    /// </summary>
    private async Task OpenSystemInterfaceFlowAsync()
    {
        // [ACURATEX] Esta ruta solo abre la interfaz secundaria si existe conexion activa.
        if (!_connection.IsConnected) {
            await _alertService.ShowAsync(
                "Sin conexión",
                "Error: el sistema no está conectado.",
                AppAlertKind.Warning);
            return;
        }

        using SystemSelectorForm selector = new();
        selector.SetConnection(_connection);
        selector.SetAuthContext(_roleService, _authState, _permissionService);
        if (selector.ShowDialog(this) != DialogResult.OK || selector.SelectedSystemForm == null) {
            return;
        }

        ShowSystemInterface(selector.SelectedSystemForm);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para montar los servicios que necesita la UI Razor incrustada.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el constructor de `Form1`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta una sola vez, al crear la ventana principal.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Registra servicios, crea el `ServiceProvider` y conecta el host HTML con el componente
    /// raíz Blazor.
    ///
    /// [FLUJO ACURATEX]
    /// Form1 -> ConfigureBlazor -> DI -> BlazorWebView -> AppShell.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a registrar módulos y callbacks antes de arrancar una interfaz embebida.
    ///
    /// [SI NO EXISTIERA]
    /// Los componentes Razor no tendrían servicios ni punto de entrada visual.
    /// </summary>
    private void ConfigureBlazor()
    {
        // [ACURATEX] WinForms hospeda componentes Razor dentro del mismo proceso y comparte servicios por DI.
        // [C#] `ServiceCollection` es la lista donde se registran dependencias.
        ServiceCollection services = new();
        services.AddWindowsFormsBlazorWebView();
        // [ACURATEX] El host de ventana expone a Razor acciones de chrome y alertas.
        services.AddSingleton<IWindowChromeHost>(_ => new WindowChromeHost(this, "Acuratex Control App", canMinimize: true, canMaximize: true));
        services.AddSingleton<IAppAlertService>(_alertService);
        services.AddSingleton<IMainControlPanelHost>(this);
        services.AddSingleton(_roleService);
        services.AddSingleton(_demoUsersService);
        services.AddSingleton(_authState);
        services.AddSingleton(_permissionService);
        services.AddSingleton<ICommandFileTransferService>(_ => new CommandFileTransferService(_connection));
        services.AddSingleton<ITesterWifiConfigService>(_ => new TesterWifiConfigService(_connection));
        services.AddSingleton<IHeadProfileService, HeadProfileService>();
        services.AddSingleton<IAppScriptExecutionService, AppScriptExecutionService>();
        services.AddSingleton<IHeadStateEventParser, HeadStateEventParser>();
        services.AddSingleton<ILocalTempFileService, LocalTempFileService>();

        // [C#] `BuildServiceProvider()` materializa el contenedor de DI.
        _blazorServices = services.BuildServiceProvider();
        // [ACURATEX] La página host HTML contiene el ancla donde Blazor inserta la UI.
        blazorWebView.HostPage = "wwwroot\\index-main.html";
        blazorWebView.Services = _blazorServices;
        blazorWebView.RootComponents.Add<AppShell>("#app");
    }

    // [ACURATEX] Esta rutina solo registra el texto recibido desde el transporte.
    // [FLUJO] Transporte -> evento LineReceived -> OnLineReceived -> log.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para registrar en el log cada linea que llega desde el transporte.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el evento `LineReceived` del controlador de conexion.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que el firmware o el transporte entrega una linea de texto.
    ///
    /// [ENTRADAS]
    /// Recibe la linea recibida.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Agrega una entrada al log visual.
    ///
    /// [FLUJO ACURATEX]
    /// Transporte -> `OnLineReceived()` -> `AppendLog()`.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una rutina de recepcion serial que registra la trama entrante.
    ///
    /// [SI NO EXISTIERA]
    /// Las lineas recibidas no tendrian un punto unico de observacion.
    /// </summary>
    private void OnLineReceived(string line)
    {
        // [EQUIV MCU] Es parecido a una interrupción de recepción que entrega la trama a la capa superior.
        AppendLog($"<< {line}");
    }

    private void OnLineSent(string line)
    {
        AppendLog($">> {RedactSensitiveCommandForLog(line)}");
    }

    private static string RedactSensitiveCommandForLog(string line)
    {
        if (line.StartsWith("WIFI_CONFIG_SET", StringComparison.OrdinalIgnoreCase)) {
            return "WIFI_CONFIG_SET|PASS=<redacted>";
        }

        return line;
    }

    // [C#] Método auxiliar `private` que centraliza la lectura del modo seleccionado.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para leer el modo de conexion actual desde el estado compartido.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman las rutinas de refresco y validacion de UI.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que alguna logica necesita saber si la pantalla esta en USB,
    /// WiFi o Serial.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el modo activo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado compartido -> `GetSelectedMode()` -> decisiones de interfaz.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una bandera de configuracion para decidir que periferico usar.
    ///
    /// [SI NO EXISTIERA]
    /// Cada llamada tendria que leer el campo directamente.
    /// </summary>
    private ConnectionMode GetSelectedMode()
    {
        return _state.Mode;
    }

    // [ACURATEX] Este método adapta la pantalla al modo de conexión elegido.
    // [FLUJO] Cambio de modo -> UI habilita paneles -> refresca endpoints si hace falta.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para adaptar textos, banderas y listas segun el modo de conexion.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman `SetConnectionModeAsync()` y los flujos de refresco.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el usuario cambia entre USB, WiFi o Serial.
    ///
    /// [ENTRADAS]
    /// Recibe una bandera que indica si tambien hay que refrescar endpoints.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Reescribe etiquetas, habilitaciones y listas visibles.
    ///
    /// [FLUJO ACURATEX]
    /// Modo -> `ApplyConnectionModeUi()` -> campos de la UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reconfigurar un panel segun el periférico que se va a usar.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no cambiaria sus textos y flags al cambiar de medio.
    /// </summary>
    private void ApplyConnectionModeUi(bool refreshEndpoints)
    {
        // [ACURATEX] El modo afecta etiquetas, listas y permisos visibles de la pantalla principal.
        // [C#] Esta rutina solo reorganiza la vista; no abre ni cierra ninguna conexión.
        ConnectionMode mode = GetSelectedMode();
        bool usb = mode == ConnectionMode.Usb;
        bool wifi = mode == ConnectionMode.Wifi;
        bool serial = mode == ConnectionMode.Serial;

        // [ACURATEX] Estas banderas permiten que Blazor sepa qué panel mostrar o habilitar.
        _state.IsUsbPanelEnabled = usb || serial;
        _state.IsWifiPanelEnabled = wifi;

        if (serial) {
            // [ACURATEX] En modo serial la pantalla cambia etiquetas para no hablar de WinUSB.
            _state.UsbPanelTitle = "Serial rescate";
            _state.EndpointLabel = "Puerto:";
            _state.BaudLabel = "Baud:";
            _state.BaudReadOnly = false;
            if (!int.TryParse(_state.BaudValue.Trim(), out _)) {
                _state.BaudValue = "115200";
            }

            if (refreshEndpoints) {
                RefreshSerialPorts();
            }

            return;
        }

        // [ACURATEX] En USB nativo la guía visual vuelve a referirse a WinUSB.
        _state.UsbPanelTitle = "USB nativo (WinUSB)";
        _state.EndpointLabel = "Dispositivo";
        _state.BaudLabel = "GUID:";
        _state.BaudReadOnly = true;
        _state.BaudValue = AcuratexUsbConstants.InterfaceGuidString;

        if (usb && refreshEndpoints) {
            RefreshUsbDevices();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para decidir qué lista de endpoints debe actualizarse según
    /// el modo actual.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman `ConnectAsync()`, `SetConnectionModeAsync()` y la UI al refrescar.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario necesita volver a ver dispositivos o puertos disponibles.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza la colección de endpoints.
    ///
    /// [FLUJO ACURATEX]
    /// Modo -> RefreshEndpointList -> USB o Serial -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a elegir qué periférico escanear según el modo de funcionamiento.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que duplicar la lógica de refresco.
    /// </summary>
    private void RefreshEndpointList()
    {
        // [ACURATEX] Este punto único decide qué transporte enumerar según el modo activo.
        // [C#] Un `if` simple aquí reemplaza ramas duplicadas en la UI.
        if (GetSelectedMode() == ConnectionMode.Serial) {
            RefreshSerialPorts();
            return;
        }

        if (GetSelectedMode() == ConnectionMode.Usb) {
            RefreshUsbDevices();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para enumerar dispositivos WinUSB detectables por el sistema.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `RefreshEndpointList()` cuando el modo activo es USB.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al refrescar la lista de dispositivos USB.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Reescribe `_usbDevices`, `_state.Endpoints` y `_state.SelectedEndpointId`.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB enumerator -> Form1.RefreshUsbDevices() -> lista visible en la UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer la tabla de dispositivos conectados en un bus.
    ///
    /// [SI NO EXISTIERA]
    /// El panel USB no sabría qué dispositivos están conectados.
    /// </summary>
    private void RefreshUsbDevices()
    {
        // [ACURATEX] Se vuelve a leer WinUSB para reconstruir la lista visible sin asumir cache vieja.
        // [C#] `OrderBy(...).ToArray()` produce una lista nueva ordenada para la pantalla.
        IReadOnlyList<UsbVendorDeviceInfo> devices = WinUsbDeviceEnumerator
            .Enumerate(AcuratexUsbConstants.InterfaceGuid)
            .OrderBy(static x => x.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _usbDevices.Clear();
        _usbDevices.AddRange(devices);
        _state.Endpoints = _usbDevices
            .Select(static (device, index) => new MainEndpointOption(
                device.DevicePath,
                BuildUsbEndpointLabel(device.DevicePath, index + 1)))
            .ToArray();
        _state.SelectedEndpointId = _usbDevices.Count > 0
            ? _usbDevices[0].DevicePath
            : string.Empty;

        AppendLog($"USB WinUSB detectados: {devices.Count}");
        NotifyStateChanged();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para construir una etiqueta breve y legible para cada dispositivo
    /// USB detectado.
    ///
    /// [QUIÉN LA USA]
    /// La usa la lista de endpoints de la UI.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al refrescar dispositivos WinUSB.
    ///
    /// [ENTRADAS]
    /// Recibe la ruta del dispositivo y un índice visible.
    ///
    /// [SALIDAS]
    /// Devuelve una cadena para mostrar en pantalla.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB detectado -> `BuildUsbEndpointLabel()` -> lista visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a asignar un nombre amigable a un periférico identificado por ID.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que mostrar rutas largas de Windows sin formato.
    /// </summary>
    // [ACURATEX] Construye el texto amigable que verá el usuario para cada dispositivo USB.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para convertir una ruta WinUSB en una etiqueta humana breve.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `RefreshUsbDevices()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al reconstruir la lista de dispositivos USB visibles.
    ///
    /// [ENTRADAS]
    /// Recibe la ruta del dispositivo y su indice de presentacion.
    ///
    /// [SALIDAS]
    /// Devuelve un texto para la lista de endpoints.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Ruta USB -> `BuildUsbEndpointLabel()` -> nombre amigable.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a traducir un identificador tecnico a una etiqueta legible por el operador.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que mostrar rutas largas y poco amigables.
    /// </summary>
    private static string BuildUsbEndpointLabel(string devicePath, int index)
    {
        // [ACURATEX] La etiqueta intenta mostrar VID/PID para que el operador identifique mejor el adaptador.
        if (TryExtractVidPid(devicePath, out string vid, out string pid)) {
            return $"WinUSB VID_{vid}:PID_{pid} #{index}";
        }

        return $"Dispositivo WinUSB detectado #{index}";
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para extraer VID y PID de la ruta nativa de Windows.
    ///
    /// [QUIÉN LA USA]
    /// La usa `BuildUsbEndpointLabel()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al convertir la ruta USB en texto legible.
    ///
    /// [ENTRADAS]
    /// Recibe la ruta del dispositivo y devuelve VID/PID por parámetros de salida.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si pudo extraer ambos valores.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Ruta USB -> `TryExtractVidPid()` -> etiqueta amigable.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer campos fijos dentro de una cadena de identificación.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría mostrar VID/PID en la etiqueta.
    /// </summary>
    // [ACURATEX] Busca VID y PID dentro de la ruta del dispositivo para mostrar una etiqueta
    // más útil que una cadena larga de sistema.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para sacar VID y PID de una ruta USB de Windows.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `BuildUsbEndpointLabel()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando se intenta mejorar la etiqueta del dispositivo.
    ///
    /// [ENTRADAS]
    /// Recibe la ruta del dispositivo y dos salidas para VID y PID.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si encontro ambos codigos.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Ruta USB -> `TryExtractVidPid()` -> texto VID/PID.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a decodificar campos fijos dentro de una cadena de identificacion.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria mostrar VID y PID de forma resumida.
    /// </summary>
    private static bool TryExtractVidPid(string devicePath, out string vid, out string pid)
    {
        vid = string.Empty;
        pid = string.Empty;

        // [C#] `ToUpperInvariant` y `IndexOf` facilitan buscar marcas fijas en la ruta nativa.
        if (string.IsNullOrWhiteSpace(devicePath)) {
            return false;
        }

        string normalizedPath = devicePath.ToUpperInvariant();
        int vidMarker = normalizedPath.IndexOf("VID_", StringComparison.Ordinal);
        int pidMarker = normalizedPath.IndexOf("PID_", StringComparison.Ordinal);
        if (vidMarker < 0 || pidMarker < 0) {
            return false;
        }

        int vidValueStart = vidMarker + 4;
        int pidValueStart = pidMarker + 4;
        if (vidValueStart + 4 > normalizedPath.Length || pidValueStart + 4 > normalizedPath.Length) {
            return false;
        }

        string parsedVid = normalizedPath.Substring(vidValueStart, 4);
        string parsedPid = normalizedPath.Substring(pidValueStart, 4);
        if (!IsHex4(parsedVid) || !IsHex4(parsedPid)) {
            return false;
        }

        vid = parsedVid;
        pid = parsedPid;
        return true;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para verificar que un fragmento tenga exactamente cuatro dígitos
    /// hexadecimales.
    ///
    /// [QUIÉN LA USA]
    /// La usa `TryExtractVidPid()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al validar partes del VID y PID.
    ///
    /// [ENTRADAS]
    /// Recibe el texto a validar.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el texto es un bloque hexadecimal de cuatro caracteres.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Fragmento -> `IsHex4()` -> válido o rechazado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar una palabra de configuración de 16 bits.
    ///
    /// [SI NO EXISTIERA]
    /// El parser aceptaría cadenas incorrectas como si fueran IDs válidos.
    /// </summary>
    // [ACURATEX] Valida que un fragmento tenga exactamente cuatro dígitos hexadecimales.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para validar que una cadena tenga exactamente cuatro digitos hexadecimales.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `TryExtractVidPid()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al validar los fragmentos VID y PID.
    ///
    /// [ENTRADAS]
    /// Recibe un texto.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el texto es hexadecimal de 4 caracteres.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Fragmento de ruta -> `IsHex4()` -> validacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a verificar si un campo cabe en un registro de 16 bits.
    ///
    /// [SI NO EXISTIERA]
    /// El parseo de VID/PID aceptaria cadenas invalidas.
    /// </summary>
    private static bool IsHex4(string value)
    {
        if (value.Length != 4) {
            return false;
        }

        foreach (char ch in value) {
            if (!Uri.IsHexDigit(ch)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para enumerar puertos COM disponibles cuando el modo es serial.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `RefreshEndpointList()` cuando el modo activo es Serial.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al refrescar puertos seriales o al cambiar a ese modo.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza `_serialPorts`, `_state.Endpoints` y la selección visible.
    ///
    /// [FLUJO ACURATEX]
    /// SerialPort.GetPortNames -> Form1.RefreshSerialPorts() -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enumerar UARTs o puertos serie disponibles.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla no podría ofrecer una lista de puertos COM detectados.
    /// </summary>
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para enumerar los puertos seriales disponibles cuando la app esta
    /// en modo Serial.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `ApplyConnectionModeUi()` y el refresco manual de endpoints.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al cambiar al modo serial o al pedir un nuevo escaneo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Reescribe `_serialPorts`, `_state.Endpoints` y la seleccion actual.
    ///
    /// [FLUJO ACURATEX]
    /// Modo Serial -> `RefreshSerialPorts()` -> lista visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a consultar una lista de UART disponibles antes de abrir una.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria que puertos seriales existen.
    /// </summary>
    private void RefreshSerialPorts()
    {
        // [C#] `SerialPort.GetPortNames()` pregunta a Windows qué COM existen en este momento.
        // [ACURATEX] El listado cambia según lo que Windows tenga disponible en ese instante.
        string previous = _state.SelectedEndpointId;
        string[] ports = SerialPort.GetPortNames()
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _serialPorts.Clear();
        _serialPorts.AddRange(ports);
        _state.Endpoints = _serialPorts
            .Select(static port => new MainEndpointOption(port, port))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(previous) && _serialPorts.Contains(previous)) {
            _state.SelectedEndpointId = previous;
        } else {
            _state.SelectedEndpointId = _serialPorts.Count > 0
                ? _serialPorts[0]
                : string.Empty;
        }

        AppendLog($"Puertos seriales detectados: {ports.Length}");
        NotifyStateChanged();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para centralizar el registro visual de mensajes y respuestas.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman métodos de conexión, eventos y rutinas de UI.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cada vez que la app quiere mostrar una línea en el historial.
    ///
    /// [ENTRADAS]
    /// Recibe el texto que se va a registrar.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Agrega una entrada al log, recorta el historial y notifica cambios.
    ///
    /// [FLUJO ACURATEX]
    /// Evento o acción -> AppendLog -> `_state.Logs` -> Blazor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un trazado serial o una traza de depuración.
    ///
    /// [SI NO EXISTIERA]
    /// Cada sitio tendría que duplicar la lógica de mensaje, truncado y notificación.
    /// </summary>
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para acumular el historial visible de mensajes de la app.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman casi todos los flujos de conexion y diagnostico.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que hay que mostrar una novedad al operador.
    ///
    /// [ENTRADAS]
    /// Recibe el texto a agregar.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica la lista de logs y fuerza repintado.
    ///
    /// [FLUJO ACURATEX]
    /// Evento o accion -> `AppendLog()` -> panel de mensajes.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una linea en una consola serial de diagnostico.
    ///
    /// [SI NO EXISTIERA]
    /// Los mensajes de estado no tendrian un registro central.
    /// </summary>
    private void AppendLog(string text)
    {
        if (IsDisposed) {
            return;
        }

        if (InvokeRequired) {
            if (IsHandleCreated) {
                // [ACURATEX] Si el log llega desde otro hilo, se reencola al hilo UI.
                BeginInvoke(() => AppendLog(text));
            }

            return;
        }

        // [ACURATEX] Cada línea lleva hora para reconstruir el orden de eventos.
        _state.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
        if (_state.Logs.Count > 300) {
            _state.Logs.RemoveRange(0, _state.Logs.Count - 300);
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para mostrar una interfaz secundaria del sistema y mantener una
    /// sola instancia visible.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `OpenSystemInterfaceFlowAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el selector de sistema entrega un formulario concreto.
    ///
    /// [ENTRADAS]
    /// Recibe la ventana secundaria ya construida.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede activar una ventana existente o esconder la principal y mostrar otra.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> ShowSystemInterface -> ventana secundaria -> regreso al cerrar.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar de pantalla activa en un sistema embebido con varias vistas.
    ///
    /// [SI NO EXISTIERA]
    /// Podrían abrirse múltiples ventanas secundarias a la vez.
    /// </summary>
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para mostrar una ventana secundaria y esconder la principal
    /// mientras esa interfaz especial esta activa.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los botones que abren una interfaz de sistema.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al entrar en un modo de trabajo externo a la ventana principal.
    ///
    /// [ENTRADAS]
    /// Recibe el formulario secundario que se va a mostrar.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Oculta la ventana principal y muestra la secundaria.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ShowSystemInterface()` -> ventana secundaria -> regreso posterior.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar a una pantalla de servicio sin apagar el sistema principal.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz secundaria no podria tomar el foco de forma ordenada.
    /// </summary>
    private void ShowSystemInterface(Form systemForm)
    {
        if (_activeSystemInterface != null && !_activeSystemInterface.IsDisposed) {
            // [ACURATEX] Si ya hay una interfaz abierta, solo se reactiva la existente.
            _activeSystemInterface.Activate();
            systemForm.Dispose();
            return;
        }

        _activeSystemInterface = systemForm;
        systemForm.FormClosed += ActiveSystemInterface_FormClosed;

        try {
            HideMainWindowForSystemInterface();
            systemForm.Show();
            systemForm.Activate();
        } catch {
            systemForm.FormClosed -= ActiveSystemInterface_FormClosed;
            _activeSystemInterface = null;
            RestoreMainWindowAfterSystemInterface();
            systemForm.Dispose();
            throw;
        }
    }

    // [ACURATEX] Oculta la ventana principal sin destruirla para que la interfaz secundaria
    // quede al frente.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para ocultar temporalmente la ventana principal sin destruirla.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `ShowSystemInterface()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta justo antes de abrir la interfaz secundaria.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda el estado visual y llama a `Hide()`.
    ///
    /// [FLUJO ACURATEX]
    /// Interfaz secundaria -> ocultar principal -> foco externo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner en segundo plano una pantalla principal mientras una auxiliar toma el control.
    ///
    /// [SI NO EXISTIERA]
    /// No habria forma de esconder la ventana principal sin perder su estado visual.
    /// </summary>
    private void HideMainWindowForSystemInterface()
    {
        if (_hiddenForSystemInterface) {
            return;
        }

        _showInTaskbarBeforeSystemInterface = ShowInTaskbar;
        _windowStateBeforeSystemInterface = WindowState;
        ShowInTaskbar = false;
        Hide();
        _hiddenForSystemInterface = true;
    }

    // [ACURATEX] Devuelve la ventana principal al estado anterior cuando la interfaz secundaria
    // termina.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para devolver la ventana principal a su estado anterior cuando la
    /// interfaz secundaria termina.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman `ShowSystemInterface()` en errores y `ActiveSystemInterface_FormClosed()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al cerrar la pantalla secundaria o al abortar su apertura.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Vuelve a mostrar el formulario principal y restaura su estado.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de interfaz secundaria -> `RestoreMainWindowAfterSystemInterface()` -> regreso a la app principal.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a volver a habilitar la pantalla principal tras salir de un modo especial.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana principal podria quedar oculta para siempre.
    /// </summary>
    private void RestoreMainWindowAfterSystemInterface()
    {
        if (!_hiddenForSystemInterface || IsDisposed) {
            return;
        }

        _hiddenForSystemInterface = false;
        ShowInTaskbar = _showInTaskbarBeforeSystemInterface;
        Show();

        WindowState = _windowStateBeforeSystemInterface == FormWindowState.Minimized
            ? FormWindowState.Normal
            : _windowStateBeforeSystemInterface;

        Activate();
    }

    // [ACURATEX] Este handler limpia la referencia de la interfaz secundaria cuando se cierra.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para limpiar la referencia a la interfaz secundaria cuando se cierra.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el evento `FormClosed` de la ventana secundaria.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al cerrar cualquier interfaz de sistema abierta desde la ventana principal.
    ///
    /// [ENTRADAS]
    /// Recibe el emisor y los datos del cierre.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desengancha el evento y restaura la ventana principal.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de interfaz -> `ActiveSystemInterface_FormClosed()` -> limpieza y retorno.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un handler de cierre que libera el modo especial.
    ///
    /// [SI NO EXISTIERA]
    /// La referencia a la ventana secundaria quedaria colgada.
    /// </summary>
    private void ActiveSystemInterface_FormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is Form form) {
            form.FormClosed -= ActiveSystemInterface_FormClosed;
        }

        if (ReferenceEquals(_activeSystemInterface, sender)) {
            _activeSystemInterface = null;
        }

        RestoreMainWindowAfterSystemInterface();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para recalcular qué botones y acciones deben estar disponibles.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los flujos de conexión, desconexión, descubrimiento y errores de transporte.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cada vez que cambia el estado conectado o ocupado.
    ///
    /// [ENTRADAS]
    /// Recibe si el enlace está conectado.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica flags de capacidad en `_state`.
    ///
    /// [FLUJO ACURATEX]
    /// Estado de conexión -> UpdateUiState -> botones y paneles Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a habilitar o deshabilitar salidas según una máquina de estados.
    ///
    /// [SI NO EXISTIERA]
    /// La UI podría mostrar acciones incorrectas o inconsistentes.
    /// </summary>
    private void UpdateUiState(bool connected)
    {
        // [ACURATEX] Estas banderas son la fuente de verdad visual para los botones de la UI Razor.
        _state.IsConnected = connected;
        _state.CanConnect = !connected && !_state.IsBusy;
        _state.CanDisconnect = connected && !_state.IsBusy;
        _state.CanSend = connected && !_state.IsBusy;
        _state.CanSendPreset = connected && !_state.IsBusy;
        _state.CanDiscoverWifi = !connected
            && !_state.IsBusy
            && !_state.IsDiscovering
            && GetSelectedMode() == ConnectionMode.Wifi;
        _state.CanRefreshEndpoint = !connected
            && !_state.IsBusy
            && GetSelectedMode() != ConnectionMode.Wifi;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cerrar el transporte real sin duplicar la lógica.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman `DisconnectAsync()`, `OnFormClosing()` y el manejo de fallos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la app necesita cerrar el enlace activo.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra el controlador de conexión, actualiza estados y escribe en el log.
    ///
    /// [FLUJO ACURATEX]
    /// UI o cierre -> DisconnectTransportAsync -> ConnectionController -> transporte.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar ordenadamente un periférico antes de cortar energía.
    ///
    /// [SI NO EXISTIERA]
    /// Cada cierre tendría que repetir la secuencia de desconexión.
    /// </summary>
    private async Task DisconnectTransportAsync()
    {
        // [ACURATEX] La secuencia interna limpia la sesión visual y después corta el transporte.
        if (!_connection.IsConnected) {
            UpdateUiState(false);
            NotifyStateChanged();
            return;
        }

        _state.IsBusy = true;
        UpdateUiState(true);
        NotifyStateChanged();

        try {
            // [ACURATEX] Se marca la intención para que el evento de caída no muestre alarma.
            _intentionalDisconnect = true;
            await _connection.DisconnectAsync();
            AppendLog("Conexion cerrada.");
        } catch (Exception ex) {
            AppendLog($"ERROR disconnect: {ex.Message}");
        } finally {
            _intentionalDisconnect = false;
            _state.IsBusy = false;
            UpdateUiState(false);
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para reaccionar cuando el transporte falla durante un envío.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `SendManualLineAsync()` si el envío lanza una excepción.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta solo ante una caída o desconexión detectada.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra el transporte y reenumera endpoints si aplica.
    ///
    /// [FLUJO ACURATEX]
    /// Error de envío -> HandleTransportFaultAsync -> desconectar -> refrescar lista.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una rutina de recuperación tras un fallo de bus.
    ///
    /// [SI NO EXISTIERA]
    /// Un error de envío dejaría la UI sin una reacción centralizada.
    /// </summary>
    private async Task HandleTransportFaultAsync()
    {
        // [ACURATEX] Si la conexión cae, se ejecuta la misma ruta segura de salida y refresco.
        if (_connection.IsConnected) {
            return;
        }

        AppendLog("La conexion se cerro. Reenumerando transporte.");
        await DisconnectTransportAsync();

        if (GetSelectedMode() == ConnectionMode.Usb || GetSelectedMode() == ConnectionMode.Serial) {
            RefreshEndpointList();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cerrar limpiamente la conexión cuando la ventana principal
    /// está por salir.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama WinForms cuando el usuario cierra la ventana.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta durante el cierre de la aplicación.
    ///
    /// [ENTRADAS]
    /// Recibe los argumentos del cierre.
    ///
    /// [SALIDAS]
    /// No devuelve valor porque es un override `async void` del ciclo de WinForms.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desconecta el transporte y libera el controlador.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de ventana -> OnFormClosing -> DisconnectTransportAsync -> Dispose.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una secuencia de apagado antes de cortar recursos.
    ///
    /// [SI NO EXISTIERA]
    /// La aplicación podría salir dejando el enlace abierto.
    /// </summary>
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        // [ACURATEX] WinForms llama este override antes de cerrar para dar oportunidad de limpiar recursos.
        // [C#] `async void` se usa aquí porque WinForms espera un override de cierre, no un `Task`.
        await DisconnectTransportAsync();
        _connection.Dispose();
        base.OnFormClosing(e);
    }

    // [ACURATEX] Al cerrar definitivamente, se liberan servicios y recursos gráficos propios.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para liberar recursos graficos y el contenedor Blazor al cerrar la
    /// ventana principal.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama WinForms cuando la ventana ya se cerro.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al terminar la vida util del formulario principal.
    ///
    /// [ENTRADAS]
    /// Recibe los argumentos de cierre.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Libera `_blazorServices` y `_appIcon`.
    ///
    /// [FLUJO ACURATEX]
    /// Cierre de ventana -> `OnFormClosed()` -> limpieza final.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar la pantalla principal y soltar sus recursos antes de desconectar.
    ///
    /// [SI NO EXISTIERA]
    /// El formulario podria dejar servicios o iconos vivos despues del cierre.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // [ACURATEX] Al salir definitivamente se liberan servicios y recursos gráficos propios.
        _blazorServices?.Dispose();
        _appIcon?.Dispose();
        base.OnFormClosed(e);
    }

    // [ACURATEX] Busca el dispositivo USB marcado por la UI cuando el modo activo es USB.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para resolver que dispositivo USB eligio el usuario en la lista.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman las rutas de conexion USB.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta antes de abrir un enlace WinUSB.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el dispositivo seleccionado o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `GetSelectedUsbDevice()` -> transporte USB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer que periferico concreto selecciono el operador.
    ///
    /// [SI NO EXISTIERA]
    /// El conector USB no sabria que dispositivo abrir.
    /// </summary>
    private UsbVendorDeviceInfo? GetSelectedUsbDevice()
    {
        if (GetSelectedMode() != ConnectionMode.Usb) {
            return null;
        }

        return _usbDevices.FirstOrDefault(device =>
            string.Equals(device.DevicePath, _state.SelectedEndpointId, StringComparison.OrdinalIgnoreCase));
    }

    // [ACURATEX] Devuelve el puerto serial seleccionado solo cuando el modo activo es Serial.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para resolver el puerto serial seleccionado cuando el modo activo es Serial.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman las rutas de conexion serial.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta antes de abrir el puerto COM.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el nombre del puerto o una cadena vacia.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `GetSelectedSerialPort()` -> transporte serial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a elegir que UART usar antes de inicializarla.
    ///
    /// [SI NO EXISTIERA]
    /// La conexion serial no podria saber que puerto abrir.
    /// </summary>
    private string GetSelectedSerialPort()
    {
        return GetSelectedMode() == ConnectionMode.Serial
            ? _state.SelectedEndpointId
            : string.Empty;
    }

    // [ACURATEX] Convierte el texto del cuadro de puerto en un número TCP válido.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para convertir el texto del cuadro de puerto TCP en un numero valido.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman las rutas de conexion por red.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta justo antes de abrir el socket TCP.
    ///
    /// [ENTRADAS]
    /// No recibe entradas directas; lee `_state.Port`.
    ///
    /// [SALIDAS]
    /// Devuelve el puerto numerico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede lanzar una excepcion si el texto no es valido.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ParsePortFromUi()` -> puerto TCP numerico.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar un registro de configuracion antes de usarlo.
    ///
    /// [SI NO EXISTIERA]
    /// La app podria intentar conectarse con un puerto mal formado.
    /// </summary>
    private int ParsePortFromUi()
    {
        if (!int.TryParse(_state.Port.Trim(), out int tcpPort) || tcpPort <= 0) {
            throw new InvalidOperationException("Puerto TCP invalido.");
        }

        return tcpPort;
    }

    // [ACURATEX] Convierte el baudrate textual a entero solo cuando se usa Serial.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para convertir el baudrate escrito en la UI a un entero valido.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman las rutas de conexion serial.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta antes de abrir el enlace serial.
    ///
    /// [ENTRADAS]
    /// No recibe entradas directas; lee `_state.BaudValue`.
    ///
    /// [SALIDAS]
    /// Devuelve el baudrate numerico o cero si el modo no es Serial.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede lanzar una excepcion si el texto no es valido.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ParseBaudFromUi()` -> velocidad serial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar la velocidad de una UART antes de habilitarla.
    ///
    /// [SI NO EXISTIERA]
    /// El puerto serial podria abrirse con una velocidad incorrecta.
    /// </summary>
    private int ParseBaudFromUi()
    {
        if (GetSelectedMode() != ConnectionMode.Serial) {
            return 0;
        }

        if (!int.TryParse(_state.BaudValue.Trim(), out int baudRate) || baudRate <= 0) {
            throw new InvalidOperationException("Baudrate serial invalido.");
        }

        return baudRate;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para reaccionar cuando el controlador avisa que la conexión cayó.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el evento `ConnectionLost` del `IConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el transporte deja de estar disponible sin una desconexión
    /// intencional.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor porque es un handler `async void`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza la UI, reproduce un sonido y puede mostrar una alerta.
    ///
    /// [FLUJO ACURATEX]
    /// Transporte -> ConnectionLost -> OnConnectionLost -> UI/alerta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupción o callback de error de enlace.
    ///
    /// [SI NO EXISTIERA]
    /// La app no reaccionaría de forma centralizada ante una caída de comunicación.
    /// </summary>
    private async void OnConnectionLost()
    {
        if (_intentionalDisconnect) {
            return;
        }

        if (InvokeRequired) {
            if (IsHandleCreated) {
                // [C#] `BeginInvoke` reprograma el callback en el hilo UI sin bloquear el hilo actual.
                BeginInvoke(OnConnectionLost);
            }

            return;
        }

        if (!_connection.IsConnected) {
            UpdateUiState(false);
            NotifyStateChanged();
            return;
        }

        if (_hiddenForSystemInterface) {
            UpdateUiState(false);
            NotifyStateChanged();
            CloseActiveSystemInterface();
            return;
        }

        // [ACURATEX] Señal sonora breve para avisar que el enlace se perdió.
        System.Media.SystemSounds.Exclamation.Play();
        await _alertService.ShowAsync(
            "Conexión perdida",
            "El sistema está desconectado. Se cerrará la interfaz gráfica si está abierta.",
            AppAlertKind.Warning);
        UpdateUiState(false);
        NotifyStateChanged();
    }

    // [ACURATEX] Cierra la interfaz secundaria abierta, si todavía existe.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para cerrar la interfaz secundaria abierta desde la ventana principal.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los flujos de cierre y recuperacion de la UI.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando hay que abandonar la ventana auxiliar.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra y limpia la referencia de la interfaz secundaria.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `CloseActiveSystemInterface()` -> ventana secundaria cerrada.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a salir de una pantalla auxiliar y volver al menu principal.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz auxiliar podria quedarse abierta o referenciada despues del cierre.
    /// </summary>
    private void CloseActiveSystemInterface()
    {
        // [FLUJO] Cerrar la interfaz secundaria restaura primero el estado visual de la ventana principal.
        Form? systemForm = _activeSystemInterface;
        if (systemForm == null || systemForm.IsDisposed) {
            return;
        }

        systemForm.Close();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para avisar a los componentes Razor que el estado cambió.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman casi todos los métodos que modifican `_state`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta después de cambios visibles para la UI.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara el evento `StateChanged`.
    ///
    /// [FLUJO ACURATEX]
    /// Estado cambia -> NotifyStateChanged -> `StateChanged?.Invoke()` -> Blazor refresca.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una notificación de actualización de variables compartidas.
    ///
    /// [SI NO EXISTIERA]
    /// La UI Razor no sabría que debe repintarse.
    /// </summary>
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para avisar a la UI Blazor que el estado compartido cambio y debe
    /// repintarse.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los flujos que modifican `_state`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta despues de cualquier cambio visible de estado.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara el evento `StateChanged`.
    ///
    /// [FLUJO ACURATEX]
    /// Cambio de estado -> `NotifyStateChanged()` -> `StateChanged` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de refresco que obliga a redibujar la pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no se enteraria de cambios en botones o datos.
    /// </summary>
    private void NotifyStateChanged()
    {
        // [C#] `?.Invoke()` llama al evento solo si alguien se suscribió.
        if (IsDisposed) {
            return;
        }

        if (InvokeRequired) {
            if (IsHandleCreated) {
                BeginInvoke(NotifyStateChanged);
            }

            return;
        }

        // [C#] `?.` llama al evento solo si hay suscriptores.
        StateChanged?.Invoke();
    }
}
