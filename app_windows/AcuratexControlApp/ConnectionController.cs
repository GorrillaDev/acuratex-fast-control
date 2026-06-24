// [ACURATEX] Este objeto centraliza el uso del transporte correcto y evita que la UI
// hable directamente con USB, TCP o serial.
namespace AcuratexControlApp;

    public sealed class ConnectionController : IConnectionController
{
    private IControllerTransport? _transport;

    private readonly ControllerTransportFactory _transportFactory;

    private int _suppressConnectionLost;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para crear el controlador con su factoría por defecto.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama `Form1` cuando arranca la aplicación.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta una sola vez al construir el controlador.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea internamente una `ControllerTransportFactory`.
    ///
    /// [FLUJO ACURATEX]
    /// Form1 -> ConnectionController() -> factoría interna.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar un módulo de comunicaciones con su configuración base.
    ///
    /// [SI NO EXISTIERA]
    /// El controlador no tendría una factoría predeterminada para crear transportes.
    /// </summary>
    public ConnectionController()
        : this(new ControllerTransportFactory())
    {
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para permitir inyección de una factoría personalizada.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama el constructor por defecto y, en pruebas, cualquier código que quiera
    /// sustituir la creación de transportes.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al crear el controlador.
    ///
    /// [ENTRADAS]
    /// Recibe una `ControllerTransportFactory`.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda la referencia a la factoría.
    ///
    /// [FLUJO ACURATEX]
    /// Construcción -> almacenamiento de factoría -> creación diferida de transporte.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a pasar una tabla de configuración a un gestor de periféricos.
    ///
    /// [SI NO EXISTIERA]
    /// No se podría reemplazar la forma de crear transportes.
    /// </summary>
    public ConnectionController(ControllerTransportFactory transportFactory)
    {
        _transportFactory = transportFactory;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para que la UI consulte rápido si hay enlace activo.
    ///
    /// [QUIÉN LA USA]
    /// La usan el panel principal, los servicios y los botones de estado.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al decidir si se puede enviar o mostrar acciones.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el transporte actual está conectado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `IsConnected` -> habilitar o bloquear acciones.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un bit de enlace listo en un periférico.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que consultar el transporte concreto todo el tiempo.
    /// </summary>
    public bool IsConnected => _transport?.IsConnected == true;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para mostrar qué transporte concreto está activo.
    ///
    /// [QUIÉN LA USA]
    /// La usan diagnósticos y trazas internas.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al depurar la conexión.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve un texto descriptivo del transporte.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Transporte activo -> `ActiveTransportKind` -> diagnóstico.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a imprimir qué interfaz física quedó seleccionada.
    ///
    /// [SI NO EXISTIERA]
    /// Depurar qué medio de conexión está en uso sería más difícil.
    /// </summary>
    public string ActiveTransportKind => _transport switch
    {
        WinUsbControllerTransport => "USB/WinUSB",
        TcpControllerTransport => "TCP directo",
        SerialControllerTransport => "Serial",
        null => "Sin transporte",
        _ => _transport.GetType().Name
    };

    public event Action<string>? LineReceived;
    public event Action<string>? LineSent;
    public event Action? ConnectionLost;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para reemplazar el transporte actual por uno nuevo y conectar.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la ventana principal.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al pulsar Conectar o al cambiar de modo de conexión.
    ///
    /// [ENTRADAS]
    /// Recibe el modo, dispositivo USB opcional, host, puerto TCP, puerto serial,
    /// baudrate y token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desconecta el transporte anterior, crea uno nuevo, suscribe eventos y puede dejar
    /// activo el enlace recién creado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ConnectionController.ConnectAsync -> factoría -> transporte -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar un periférico y encender otro en una máquina de estados.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podría cambiar de medio de comunicación de forma centralizada.
    /// </summary>
    public async Task ConnectAsync(
        ConnectionMode mode,
        UsbVendorDeviceInfo? device,
        string host,
        int tcpPort,
        string serialPort,
        int baudRate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisconnectAsync().ConfigureAwait(false);

        IControllerTransport transport = _transportFactory.Create(mode, device, host, tcpPort, serialPort, baudRate);
        transport.LineReceived += HandleLineReceived;
        transport.ConnectionLost += HandleConnectionLost;

        try {
            await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _transport = transport;
        } catch {
            transport.LineReceived -= HandleLineReceived;
            transport.ConnectionLost -= HandleConnectionLost;
            await transport.DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cerrar el transporte actual y dejar el controlador sin enlace.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman la UI, el cierre de la aplicación y el propio controlador al cambiar de modo.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando hay una desconexión manual o implícita.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desuscribe eventos, bloquea temporalmente `ConnectionLost` y libera el transporte.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ConnectionController.DisconnectAsync -> transporte concreto -> Dispose/cleanup.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a deshabilitar interrupciones antes de apagar un periférico.
    ///
    /// [SI NO EXISTIERA]
    /// No habría una desconexión limpia ni forma de evitar falsas alarmas al cerrar.
    /// </summary>
    public async Task DisconnectAsync()
    {
        IControllerTransport? transport = _transport;
        if (transport == null) {
            return;
        }

        _transport = null;
        Interlocked.Exchange(ref _suppressConnectionLost, 1);
        transport.LineReceived -= HandleLineReceived;
        transport.ConnectionLost -= HandleConnectionLost;

        try {
            await transport.DisconnectAsync().ConfigureAwait(false);
        } finally {
            Interlocked.Exchange(ref _suppressConnectionLost, 0);
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para mandar una línea textual al transporte activo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la ventana principal o cualquier servicio que use `IConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI necesita enviar un comando al firmware.
    ///
    /// [ENTRADAS]
    /// Recibe el texto y un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Lanza el evento `LineSent` y escribe en el transporte.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ConnectionController.SendLineAsync -> LineSent -> transporte -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una trama en un canal de salida y registrar eco local.
    ///
    /// [SI NO EXISTIERA]
    /// No habría una ruta única para el envío de comandos.
    /// </summary>
    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_transport == null || !_transport.IsConnected) {
            throw new InvalidOperationException("No hay conexion activa.");
        }

        LineSent?.Invoke(line);
        await _transport.SendLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para reenviar al resto de la app una línea recibida por el transporte.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el transporte concreto mediante su evento `LineReceived`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando llega una línea desde el firmware.
    ///
    /// [ENTRADAS]
    /// Recibe la línea textual ya separada.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara el evento `LineReceived` del controlador central.
    ///
    /// [FLUJO ACURATEX]
    /// Transporte -> HandleLineReceived -> IConnectionController.LineReceived -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una rutina de interrupción de recepción que reenvía la trama.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no recibiría los datos entrantes del firmware.
    /// </summary>
    private void HandleLineReceived(string line)
    {
        // [EQUIV MCU] Este callback se parece a una interrupcion de recepcion que entrega la trama al supervisor.
        LineReceived?.Invoke(line);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para transformar la caída del transporte en un evento de enlace
    /// perdido para el resto de la aplicación.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el transporte concreto cuando detecta desconexión.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al perder el enlace sin una desconexión manual previa.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara `ConnectionLost` si la caída no fue suprimida.
    ///
    /// [FLUJO ACURATEX]
    /// Transporte -> HandleConnectionLost -> ConnectionLost -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupción de fallo de enlace.
    ///
    /// [SI NO EXISTIERA]
    /// La aplicación no sabría que el cable o el enlace se perdió.
    /// </summary>
    private void HandleConnectionLost()
    {
        if (Volatile.Read(ref _suppressConnectionLost) != 0) {
            return;
        }

        if (_transport == null) {
            return;
        }

        ConnectionLost?.Invoke();
    }
}
