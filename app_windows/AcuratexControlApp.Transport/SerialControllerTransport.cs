// [ACURATEX] Este transporte usa `SerialPort` para hablar por COM como vía de rescate.
// Presenta todo como líneas de texto para mantener el mismo contrato que los demás transportes.
using System.IO.Ports;
using System.Diagnostics;

namespace AcuratexControlApp;

// [C#] `sealed` indica que esta implementación ya es concreta.
public sealed class SerialControllerTransport : IControllerTransport
{
    // [C#] Campos de configuración serial fijados al construir el objeto.
    private readonly string _portName;
    private readonly int _baudRate;
    // [ACURATEX] `SerialPort` representa el puerto COM abierto.
    private SerialPort? _serialPort;
    // [ACURATEX] Controlan el bucle de lectura en segundo plano.
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para guardar el puerto COM y el baudrate a usar.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama la factoría de transportes.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al seleccionar modo serial.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre del puerto y la velocidad.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda los parámetros para la apertura posterior.
    ///
    /// [FLUJO ACURATEX]
    /// Factoría -> SerialControllerTransport(port, baud) -> ConnectAsync.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a configurar una UART con su pin/velocidad objetivo.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte serial no sabría qué COM abrir.
    /// </summary>
    public SerialControllerTransport(string portName, int baudRate)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    // [C#] Propiedad calculada que pregunta al puerto si está abierto.
    public bool IsConnected => _serialPort?.IsOpen == true;

    public event Action<string>? LineReceived;
    public event Action? ConnectionLost;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abrir el puerto COM y arrancar la lectura continua.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al conectar por Serial.
    ///
    /// [ENTRADAS]
    /// Recibe `CancellationToken`.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea y abre `SerialPort`, configura timeouts y arranca el bucle de lectura.
    ///
    /// [FLUJO ACURATEX]
    /// Controller -> SerialControllerTransport.ConnectAsync -> COM -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar una UART con parámetros de trama y habilitar lectura.
    ///
    /// [SI NO EXISTIERA]
    /// No habría conexión serial funcional.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsConnected) {
            return Task.CompletedTask;
        }

        // [ACURATEX] Abrir el puerto serial es equivalente a habilitar una UART con sus parámetros.
        // [ACURATEX] Crea el puerto serial con el nombre y velocidad pedidos por la UI.
        _serialPort = new SerialPort(_portName, _baudRate)
        {
            NewLine = "\n",
            ReadTimeout = 250,
            WriteTimeout = 1000,
            DtrEnable = false,
            RtsEnable = false,
        };
        _serialPort.Open();

        // [ACURATEX] El bucle de lectura corre en segundo plano para recibir respuestas línea a línea.
        _readLoopCts = new CancellationTokenSource();
        _readLoopTask = Task.Run(() => ReadLoop(_readLoopCts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para detener la lectura y cerrar el puerto COM ordenadamente.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController` o `Dispose`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al desconectar o al cerrar la app.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cancela el bucle de lectura y libera el `SerialPort`.
    ///
    /// [FLUJO ACURATEX]
    /// Controller -> DisconnectAsync -> cancelación -> Cleanup serial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a deshabilitar una UART y liberar sus buffers.
    ///
    /// [SI NO EXISTIERA]
    /// El puerto COM podría quedar abierto o el lector seguir corriendo.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_readLoopCts != null) {
            _readLoopCts.Cancel();
        }

        if (_readLoopTask != null) {
            try {
                await _readLoopTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }
        }

        // [ACURATEX] Limpia todo el estado serial.
        Cleanup();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para transmitir una línea completa por el puerto serial.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la app manda un comando por Serial.
    ///
    /// [ENTRADAS]
    /// Recibe el texto y un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Escribe en el puerto COM.
    ///
    /// [FLUJO ACURATEX]
    /// Controller -> SerialControllerTransport.SendLineAsync -> UART/COM -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a mandar una línea por `Serial.println()`.
    ///
    /// [SI NO EXISTIERA]
    /// No habría envío por el canal serial de rescate.
    /// </summary>
    public Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_serialPort == null || !_serialPort.IsOpen) {
            throw new InvalidOperationException("El puerto serie no esta conectado.");
        }

        // [ACURATEX] El log de archivo solo sirve para depuración.
        LogOutgoingLineIfFileData(line);
        _serialPort.WriteLine(line);
        return Task.CompletedTask;
    }

    // [C#] `Dispose` cierra el puerto aunque la clase no se desconecte manualmente.
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para leer continuamente del puerto COM y reconstruir líneas de
    /// texto con delimitación por salto de línea.
    ///
    /// [QUIÉN LA USA]
    /// La llama `Task.Run()` desde `ConnectAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta mientras el puerto serial permanece abierto.
    ///
    /// [ENTRADAS]
    /// Recibe un token de cancelación.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara `LineReceived`, cierra recursos al fallar y puede notificar `ConnectionLost`.
    ///
    /// [FLUJO ACURATEX]
    /// Serial COM -> `ReadLoop()` -> `LineReceived`.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una lectura continua de UART en un loop de recepción.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte serial no recibiría respuestas del firmware.
    /// </summary>
    // [ACURATEX] Bucle de lectura permanente con delimitación por salto de línea.
    private void ReadLoop(CancellationToken cancellationToken)
    {
        // [ACURATEX] Este bucle se parece a una lectura continua desde UART con framing por línea.
        // [EQUIV MCU] Un bucle de lectura serial como el de una UART con delimitador de línea.
        if (_serialPort == null) {
            return;
        }

        while (!cancellationToken.IsCancellationRequested && _serialPort.IsOpen) {
            try {
                string line = _serialPort.ReadLine();
                if (!string.IsNullOrWhiteSpace(line)) {
                    LineReceived?.Invoke(line.Trim());
                }
            } catch (TimeoutException) {
            } catch (InvalidOperationException) {
                break;
            }
        }

        if (!cancellationToken.IsCancellationRequested) {
            Cleanup();
            ConnectionLost?.Invoke();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cerrar el puerto COM y liberar las referencias internas.
    ///
    /// [QUIÉN LA USA]
    /// La usan `DisconnectAsync()` y el bucle de lectura al detectar una caída.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al terminar la conexión serial.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra, dispone y borra el `SerialPort` y el CTS.
    ///
    /// [FLUJO ACURATEX]
    /// Desconexión -> `Cleanup()` -> puerto serial liberado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a deshabilitar una UART y borrar sus buffers.
    ///
    /// [SI NO EXISTIERA]
    /// El puerto COM podría quedar abierto o el lector seguir vivo.
    /// </summary>
    // [ACURATEX] Libera el puerto y resetea referencias internas.
    private void Cleanup()
    {
        // [ACURATEX] El puerto queda cerrado y sin referencias para que no quede nada vivo.
        if (_serialPort != null) {
            if (_serialPort.IsOpen) {
                _serialPort.Close();
            }

            _serialPort.Dispose();
            _serialPort = null;
        }

        _readLoopTask = null;

        if (_readLoopCts != null) {
            _readLoopCts.Dispose();
            _readLoopCts = null;
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para registrar trazas opcionales cuando se envía un bloque de
    /// archivo por serial.
    ///
    /// [QUIÉN LA USA]
    /// La usa `SendLineAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta solo para líneas `FILE_DATA`.
    ///
    /// [ENTRADAS]
    /// Recibe el texto de la línea.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Escribe diagnóstico en `Trace`.
    ///
    /// [FLUJO ACURATEX]
    /// Serial send -> `LogOutgoingLineIfFileData()` -> traza.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a imprimir una traza de depuración solo para bloques de datos largos.
    ///
    /// [SI NO EXISTIERA]
    /// Los envíos de archivo por serial serían más difíciles de depurar.
    /// </summary>
    // [ACURATEX] Trazado opcional para ver transferencias de archivos por serial.
    private static void LogOutgoingLineIfFileData(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.StartsWith("FILE_DATA|0|", StringComparison.Ordinal)) {
            return;
        }

        string head = line[..Math.Min(24, line.Length)];
        string tail = line[^Math.Min(24, line.Length)..];
        Trace.WriteLine(
            $"[SerialControllerTransport] write rawLen={line.Length} head={head} tail={tail} newline=\\n");
    }
}
