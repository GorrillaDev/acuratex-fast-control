// [ACURATEX] Este transporte usa sockets TCP y mantiene un lector continuo más un latido
// para detectar caídas de conexión.
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;

namespace AcuratexControlApp;

// [C#] `sealed` deja claro que esta implementación no está pensada para heredarse.
public sealed class TcpControllerTransport : IControllerTransport
{
    // [C#] `static readonly` fija constantes de temporización para el latido.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(3);
    // [ACURATEX] Comandos fijos del mecanismo de latido.
    private const string HeartbeatCommand = "ping";
    private const string HeartbeatReply = "PONG";

    // [C#] Estos campos guardan el destino de red elegido.
    private readonly string _host;
    private readonly int _port;

    // [ACURATEX] Objetos del stack TCP y su control de lectura/escritura.
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readLoopCts;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _readLoopTask;
    private Task? _heartbeatTask;
    private long _lastReceiveTick;
    // [ACURATEX] Bloquea escrituras concurrentes para no mezclar líneas.
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para fijar el host y puerto a los que debe conectarse TCP.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama la factoría de transportes.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la app selecciona transporte TCP.
    ///
    /// [ENTRADAS]
    /// Recibe host y puerto.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda los valores de destino para la conexión posterior.
    ///
    /// [FLUJO ACURATEX]
    /// Factoría -> TcpControllerTransport(host, port) -> ConnectAsync.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a almacenar la dirección IP y el puerto de un nodo remoto.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte TCP no sabría a qué destino conectar.
    /// </summary>
    public TcpControllerTransport(string host, int port)
    {
        _host = host;
        _port = port;
    }

    // [C#] Propiedad calculada que pregunta al socket subyacente si sigue conectado.
    public bool IsConnected => _client?.Connected == true;

    public event Action<string>? LineReceived;
    public event Action? ConnectionLost;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abrir el socket TCP y preparar lectura/escritura de líneas.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al conectar por red.
    ///
    /// [ENTRADAS]
    /// Recibe `CancellationToken`.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Conecta el socket, crea lectores/escritores, arranca lectura y latido.
    ///
    /// [FLUJO ACURATEX]
    /// ConnectionController -> TcpControllerTransport.ConnectAsync -> socket -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a abrir un canal de comunicación externo y habilitar monitoreo de enlace.
    ///
    /// [SI NO EXISTIERA]
    /// No habría conexión TCP funcional.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsConnected) {
            return;
        }

        // [ACURATEX] La conexión TCP no solo abre socket: también prepara lectura continua y latido.
        // [ACURATEX] El cliente TCP representa el enlace remoto.
        _client = new TcpClient();
        await _client.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
        ConfigureSocket(_client.Client);

        // [ACURATEX] Un mismo NetworkStream alimenta lectura y escritura de texto.
        NetworkStream stream = _client.GetStream();
        _reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        _writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        // [ACURATEX] El latido empieza contando desde esta conexión.
        MarkReceiveActivity();
        _readLoopCts = new CancellationTokenSource();
        _heartbeatCts = new CancellationTokenSource();
        _readLoopTask = Task.Run(() => ReadLoop(_readLoopCts.Token), CancellationToken.None);
        _heartbeatTask = Task.Run(() => HeartbeatLoop(_heartbeatCts.Token), CancellationToken.None);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cerrar el socket, cancelar tareas y limpiar recursos.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController` o `Dispose`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al desconectar o al perder enlace.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cancela lectura, cancela latido, cierra socket y libera objetos.
    ///
    /// [FLUJO ACURATEX]
    /// Controller -> DisconnectAsync -> cancelaciones -> Cleanup.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a bajar una comunicación serie y limpiar buffers/flags.
    ///
    /// [SI NO EXISTIERA]
    /// Podrían quedar tareas de fondo activas o sockets abiertos.
    /// </summary>
    public async Task DisconnectAsync()
    {
        // [FLUJO] Primero se cancelan los bucles de fondo y luego se esperan para cerrar limpio.
        if (_readLoopCts != null) {
            _readLoopCts.Cancel();
        }
        if (_heartbeatCts != null) {
            _heartbeatCts.Cancel();
        }

        // [ACURATEX] Cierra el socket antes de esperar a que terminen las tareas.
        CloseClientSocket();

        if (_readLoopTask != null) {
            try {
                await _readLoopTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }
        }
        if (_heartbeatTask != null) {
            try {
                await _heartbeatTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }
        }

        // [ACURATEX] Limpia lectores, escritores, cliente y cancelaciones.
        Cleanup();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para mandar una línea de texto por TCP sin mezclar escrituras.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la app envía un comando por red.
    ///
    /// [ENTRADAS]
    /// Recibe la línea y un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Encola la escritura bajo un `SemaphoreSlim`.
    ///
    /// [FLUJO ACURATEX]
    /// Controller -> TcpControllerTransport.SendLineAsync -> socket TCP -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una UART con exclusión mutua para que no se entremezclen tramas.
    ///
    /// [SI NO EXISTIERA]
    /// Las escrituras concurrentes podrían corromper las líneas enviadas.
    /// </summary>
    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WriteLineThreadSafeAsync(line, cancellationToken).ConfigureAwait(false);
    }

    // [C#] `Dispose` libera el semáforo además de cerrar la conexión.
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _writeGate.Dispose();
    }

    // [ACURATEX] Bucle de lectura permanente para recibir líneas del firmware.
    private async Task ReadLoop(CancellationToken cancellationToken)
    {
        // [ACURATEX] Este bucle es una tarea permanente de recepción en la interfaz de red.
        // [EQUIV MCU] Este bucle equivale a una ISR de recepción que no debe dormir el hilo UI.
        if (_reader == null) {
            return;
        }

        while (!cancellationToken.IsCancellationRequested) {
            string? line;

            try {
                line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                break;
            } catch (IOException) {
                break;
            } catch (ObjectDisposedException) {
                break;
            }

            if (line == null) {
                break;
            }

            // [ACURATEX] Cada línea recibida actualiza el tiempo del último tráfico.
            MarkReceiveActivity();
            if (!string.IsNullOrWhiteSpace(line)) {
                string trimmed = line.Trim();
                if (!string.Equals(trimmed, HeartbeatReply, StringComparison.OrdinalIgnoreCase)) {
                    LineReceived?.Invoke(trimmed);
                }
            }
        }

        if (!cancellationToken.IsCancellationRequested) {
            Cleanup();
            ConnectionLost?.Invoke();
        }
    }

    // [ACURATEX] Bucle de latido para detectar silencios largos y perder conexión a tiempo.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para mandar un latido periódico y detectar silencios largos en la
    /// conexión TCP.
    ///
    /// [QUIÉN LA USA]
    /// La llama `Task.Run()` desde `ConnectAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta mientras el socket TCP siga vivo.
    ///
    /// [ENTRADAS]
    /// Recibe un token de cancelación.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía `ping`, vigila el tiempo sin tráfico y puede forzar cierre del socket.
    ///
    /// [FLUJO ACURATEX]
    /// TCP conectado -> `HeartbeatLoop()` -> ping / silencio -> conexión viva o caída.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un watchdog por software que revisa si el otro extremo sigue respondiendo.
    ///
    /// [SI NO EXISTIERA]
    /// Una conexión muerta podría parecer viva durante más tiempo.
    /// </summary>
    private async Task HeartbeatLoop(CancellationToken cancellationToken)
    {
        // [ACURATEX] El heartbeat vigila que el otro extremo siga vivo aunque no llegue tráfico útil.
        // [ACURATEX] El latido mantiene viva la sesión y detecta silencios largos de la red.
        using PeriodicTimer timer = new(HeartbeatInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
            if (_writer == null || _client?.Connected != true) {
                break;
            }

            TimeSpan silence = TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastReceiveTick));
            if (silence > HeartbeatTimeout) {
                CloseClientSocket();
                break;
            }

            try {
                await WriteLineThreadSafeAsync(HeartbeatCommand, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                break;
            } catch (IOException) {
                CloseClientSocket();
                break;
            } catch (ObjectDisposedException) {
                break;
            } catch (InvalidOperationException) {
                break;
            }
        }
    }

    // [ACURATEX] Envoltorio con exclusión mutua para una sola escritura a la vez.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para escribir una línea con exclusión mutua y evitar que dos
    /// hilos mezclen texto en el mismo socket.
    ///
    /// [QUIÉN LA USA]
    /// La usa `SendLineAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cada vez que el transporte TCP manda una línea.
    ///
    /// [ENTRADAS]
    /// Recibe el texto y un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Toma el semáforo, escribe al socket y libera el bloqueo.
    ///
    /// [FLUJO ACURATEX]
    /// SendLineAsync -> `WriteLineThreadSafeAsync()` -> socket TCP.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una sección crítica de transmisión UART con bloqueo de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// Dos comandos simultáneos podrían entremezclar texto en la misma salida.
    /// </summary>
    private async Task WriteLineThreadSafeAsync(string line, CancellationToken cancellationToken)
    {
        // [C#] `SemaphoreSlim` serializa los envíos para que dos escrituras no se mezclen.
        // [C#] `SemaphoreSlim` serializa las escrituras para que dos hilos no mezclen texto.
        cancellationToken.ThrowIfCancellationRequested();
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_writer == null) {
                throw new InvalidOperationException("La conexion TCP no esta conectada.");
            }

            LogOutgoingLineIfFileData(line);
            await _writer.WriteLineAsync(line).ConfigureAwait(false);
        } finally {
            _writeGate.Release();
        }
    }

    // [ACURATEX] Solo ayuda a depurar transmisiones de archivo; no altera el envío real.
    private static void LogOutgoingLineIfFileData(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.StartsWith("FILE_DATA|0|", StringComparison.Ordinal)) {
            return;
        }

        string head = line[..Math.Min(24, line.Length)];
        string tail = line[^Math.Min(24, line.Length)..];
        Trace.WriteLine(
            $"[TcpControllerTransport] write rawLen={line.Length} head={head} tail={tail} newline=\\n");
    }

    // [ACURATEX] Ajusta keep-alive y no-delay para que la conexión reaccione rápido.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para ajustar el socket TCP y detectar mejor silencios o caídas.
    ///
    /// [QUIÉN LA USA]
    /// La usa `ConnectAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta justo después de abrir el socket.
    ///
    /// [ENTRADAS]
    /// Recibe el socket conectado.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Activa keep-alive y desactiva Nagle con `NoDelay`.
    ///
    /// [FLUJO ACURATEX]
    /// ConnectAsync -> `ConfigureSocket()` -> socket listo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a configurar tiempos de vigilancia y latido en un enlace de comunicación.
    ///
    /// [SI NO EXISTIERA]
    /// La conexión podría tardar más en notar una caída.
    /// </summary>
    private void ConfigureSocket(Socket socket)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 2);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 2);
        socket.NoDelay = true;
    }

    // [ACURATEX] Guarda el instante de la última respuesta útil.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para registrar el momento de la última actividad útil recibida.
    ///
    /// [QUIÉN LA USA]
    /// La usa el bucle de latido.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cada vez que llega una línea o tráfico válido.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el contador temporal compartido.
    ///
    /// [FLUJO ACURATEX]
    /// Recepción -> `MarkReceiveActivity()` -> watchdog TCP.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a refrescar un watchdog al recibir tráfico de bus.
    ///
    /// [SI NO EXISTIERA]
    /// El latido no sabría cuándo fue el último tráfico real.
    /// </summary>
    private void MarkReceiveActivity()
    {
        Interlocked.Exchange(ref _lastReceiveTick, Environment.TickCount64);
    }

    // [ACURATEX] Cierra el socket sin lanzar la excepción al primer problema de desconexión.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cerrar el socket sin hacer ruido si la desconexión ya estaba
    /// en curso.
    ///
    /// [QUIÉN LA USA]
    /// La usan `DisconnectAsync()` y el bucle de lectura al detectar una caída.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta antes de limpiar los objetos administrados.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Intenta hacer shutdown y close del socket.
    ///
    /// [FLUJO ACURATEX]
    /// Cleanup TCP -> `CloseClientSocket()` -> socket liberado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a bajar una línea de comunicación antes de apagar un periférico.
    ///
    /// [SI NO EXISTIERA]
    /// Un cierre normal podría lanzar excepciones innecesarias.
    /// </summary>
    private void CloseClientSocket()
    {
        try {
            _client?.Client.Shutdown(SocketShutdown.Both);
        } catch (SocketException) {
        } catch (ObjectDisposedException) {
        }

        try {
            _client?.Close();
        } catch (ObjectDisposedException) {
        }
    }

    // [ACURATEX] Libera los objetos administrados y reinicia el estado interno.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para liberar todos los objetos administrados y dejar el transporte
    /// como si nunca hubiera conectado.
    ///
    /// [QUIÉN LA USA]
    /// La usan `DisconnectAsync()` y el bucle de lectura cuando detecta una caída.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al terminar la conexión.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Libera lector, escritor, cliente y CTSs.
    ///
    /// [FLUJO ACURATEX]
    /// Disconnect/caída -> `Cleanup()` -> transporte reiniciado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a limpiar buffers y punteros de un módulo de comunicación.
    ///
    /// [SI NO EXISTIERA]
    /// Podrían quedar recursos TCP vivos después de desconectar.
    /// </summary>
    private void Cleanup()
    {
        // [ACURATEX] La limpieza deja al transporte como si nunca hubiera abierto la red.
        _reader?.Dispose();
        _reader = null;

        _writer?.Dispose();
        _writer = null;

        _client?.Dispose();
        _client = null;

        _readLoopTask = null;
        _heartbeatTask = null;

        if (_readLoopCts != null) {
            _readLoopCts.Dispose();
            _readLoopCts = null;
        }
        if (_heartbeatCts != null) {
            _heartbeatCts.Dispose();
            _heartbeatCts = null;
        }
    }
}
