// [ACURATEX] Este transporte habla con el firmware por WinUSB, pero lo presenta como líneas
// de texto para que el resto de la app no tenga que manejar bytes crudos.
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;

namespace AcuratexControlApp;

// [C#] `sealed` evita herencia y deja clara la intención de implementación concreta.
public sealed class WinUsbControllerTransport : IControllerTransport
{
    // [C#] `readonly` fija la ruta del dispositivo elegida por la factoría.
    private readonly string _devicePath;
    // [ACURATEX] Acumula bytes hasta reconstruir una línea completa terminada en `\n`.
    private readonly StringBuilder _lineBuilder = new();
    // [ACURATEX] Bloqueo simple para manipular handles nativos sin carreras de ejecución.
    private readonly object _nativeSync = new();

    // [C#] `SafeFileHandle?` representa el handle nativo del dispositivo USB abierto.
    private SafeFileHandle? _deviceHandle;
    // [C#] `nint` es un entero del tamaño de un puntero nativo.
    // [ACURATEX] Guarda el handle WinUSB que maneja los endpoints.
    private nint _winUsbHandle;
    private byte _readPipeId;
    private byte _writePipeId;
    // [ACURATEX] Controlan el ciclo de lectura permanente en segundo plano.
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para fijar la ruta concreta del dispositivo USB a abrir.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama `ControllerTransportFactory`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la app elige WinUSB como transporte.
    ///
    /// [ENTRADAS]
    /// Recibe la ruta del dispositivo enumerado por Windows.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda la ruta para usarla al conectar.
    ///
    /// [FLUJO ACURATEX]
    /// Factoría -> WinUsbControllerTransport(string devicePath) -> apertura posterior.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a guardar una dirección de periférico antes de inicializarlo.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte no sabría qué dispositivo WinUSB abrir.
    /// </summary>
    public WinUsbControllerTransport(string devicePath)
    {
        _devicePath = devicePath;
    }

    // [C#] Propiedad calculada: necesita un handle válido y un WinUSB inicializado.
    public bool IsConnected =>
        _deviceHandle is { IsInvalid: false, IsClosed: false } &&
        _winUsbHandle != nint.Zero;

    // [ACURATEX] Estos eventos llevan las líneas ya reconstruidas hasta la capa superior.
    public event Action<string>? LineReceived;
    public event Action? ConnectionLost;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abrir el dispositivo USB, inicializar WinUSB y arrancar
    /// la lectura continua.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al conectar por USB/WinUSB.
    ///
    /// [ENTRADAS]
    /// Recibe un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` completado porque la apertura se hace de forma síncrona aquí.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Abre el handle, inicializa WinUSB, descubre pipes y lanza el bucle de lectura.
    ///
    /// [FLUJO ACURATEX]
    /// ConnectionController -> WinUsbControllerTransport.ConnectAsync -> WinUSB -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a abrir un periférico USB y habilitar su interrupción de recepción.
    ///
    /// [SI NO EXISTIERA]
    /// No habría conexión USB funcional.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsConnected) {
            return Task.CompletedTask;
        }

        // [ACURATEX] Abrir WinUSB requiere handle nativo, inicializar la capa y descubrir pipes.
        // [ACURATEX] Abre el dispositivo físico por la ruta devuelta por el enumerador.
        _deviceHandle = WinUsbNative.CreateFileW(
            _devicePath,
            WinUsbNative.GenericRead | WinUsbNative.GenericWrite,
            WinUsbNative.FileShareRead | WinUsbNative.FileShareWrite,
            IntPtr.Zero,
            WinUsbNative.OpenExisting,
            WinUsbNative.FileAttributeNormal | WinUsbNative.FileFlagOverlapped,
            IntPtr.Zero);

        if (_deviceHandle.IsInvalid) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo abrir el dispositivo WinUSB.");
        }

        // [ACURATEX] Inicializa la capa WinUSB que permite hablar con los endpoints bulk.
        if (!WinUsbNative.WinUsb_Initialize(_deviceHandle, out _winUsbHandle)) {
            int error = Marshal.GetLastWin32Error();
            _deviceHandle.Dispose();
            _deviceHandle = null;
            throw new Win32Exception(error, $"No se pudo inicializar WinUSB (error {error}).");
        }

        // [ACURATEX] Busca el endpoint de lectura y el de escritura.
        DiscoverBulkPipes();
        ConfigurePipeTimeout(_readPipeId, 200);
        ConfigurePipeTimeout(_writePipeId, 200);

        // [ACURATEX] El bucle de lectura corre en segundo plano y publica líneas completas.
        _readLoopCts = new CancellationTokenSource();
        _readLoopTask = Task.Run(() => ReadLoop(_readLoopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para detener el bucle de lectura y liberar los handles nativos.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController` o el patrón `Dispose`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al desconectar o al cerrar la aplicación.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cancela el bucle, aborta pipes, espera la tarea de lectura y cierra handles.
    ///
    /// [FLUJO ACURATEX]
    /// UI/Controller -> DisconnectAsync -> cancelación -> cleanup WinUSB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a detener una ISR de recepción y deshabilitar el periférico.
    ///
    /// [SI NO EXISTIERA]
    /// La app podría dejar tareas de lectura colgadas o recursos USB abiertos.
    /// </summary>
    public async Task DisconnectAsync()
    {
        CancellationTokenSource? readLoopCts = _readLoopCts;
        Task? readLoopTask = _readLoopTask;

        _readLoopCts = null;
        _readLoopTask = null;

        // [ACURATEX] Cancela el ciclo de lectura asíncrona.
        readLoopCts?.Cancel();
        AbortOpenPipes();

        if (readLoopTask != null) {
            try {
                await readLoopTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            } catch (ObjectDisposedException) {
            } catch (IOException) {
            } catch (Win32Exception) {
            }
        }

        // [ACURATEX] Cierra handles nativos y deja el objeto en estado inactivo.
        CloseNativeHandles();
        readLoopCts?.Dispose();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir una línea de texto en bytes ASCII y enviarla al
    /// endpoint bulk OUT.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al mandar un comando por USB.
    ///
    /// [ENTRADAS]
    /// Recibe la línea y un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` completado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Escribe en el dispositivo y puede cerrar handles ante errores graves.
    ///
    /// [FLUJO ACURATEX]
    /// Controller -> WinUsbControllerTransport.SendLineAsync -> WinUSB pipe OUT -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a mandar una trama por USB CDC/endpoint bulk.
    ///
    /// [SI NO EXISTIERA]
    /// No habría forma de transmitir comandos por WinUSB.
    /// </summary>
    public Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConnected) {
            throw new InvalidOperationException("El dispositivo USB no esta conectado.");
        }

        // [ACURATEX] El firmware espera líneas terminadas en salto de línea.
        byte[] payload = Encoding.ASCII.GetBytes(line + "\n");
        LogOutgoingLineIfFileData(line, payload.Length);
        if (!WinUsbNative.WinUsb_WritePipe(_winUsbHandle, _writePipeId, payload, payload.Length, out int transferred, IntPtr.Zero)) {
            int error = Marshal.GetLastWin32Error();
            if (error == WinUsbNative.ErrorOperationAborted ||
                error == WinUsbNative.ErrorDeviceNotConnected ||
                error == WinUsbNative.ErrorInvalidHandle ||
                error == WinUsbNative.ErrorGenFailure) {
                CloseNativeHandles();
            }

            throw new Win32Exception(error, "No se pudo escribir al dispositivo USB.");
        }

        if (transferred != payload.Length) {
            throw new IOException("La escritura USB fue parcial.");
        }

        return Task.CompletedTask;
    }

    // [C#] `Dispose` permite que el transporte se cierre si la clase se descarta.
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para recorrer los endpoints del dispositivo y descubrir cuáles son
    /// bulk IN y cuáles bulk OUT.
    ///
    /// [QUIÉN LA USA]
    /// La usa `ConnectAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta justo después de inicializar WinUSB.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Lee descriptores USB y guarda los IDs de pipe en campos internos.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `DiscoverBulkPipes()` -> pipes de lectura/escritura.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inspeccionar los endpoints de un periférico USB antes de usarlo.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte no sabría qué endpoint leer y cuál escribir.
    /// </summary>
    // [ACURATEX] Recorre los endpoints del dispositivo y detecta cuáles son bulk IN y OUT.
    private void DiscoverBulkPipes()
    {
        if (!WinUsbNative.WinUsb_QueryInterfaceSettings(_winUsbHandle, 0, out WinUsbNative.UsbInterfaceDescriptor descriptor)) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo consultar la interfaz USB.");
        }

        byte bulkIn = 0;
        byte bulkOut = 0;

        for (byte index = 0; index < descriptor.NumEndpoints; index++) {
            if (!WinUsbNative.WinUsb_QueryPipe(_winUsbHandle, 0, index, out WinUsbNative.WinUsbPipeInformation pipe)) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo consultar los endpoints USB.");
            }

            if (pipe.PipeType != WinUsbNative.UsbdPipeType.Bulk) {
                continue;
            }

            if ((pipe.PipeId & 0x80) != 0) {
                bulkIn = pipe.PipeId;
            } else {
                bulkOut = pipe.PipeId;
            }
        }

        if (bulkIn == 0 || bulkOut == 0) {
            throw new InvalidOperationException("El dispositivo USB no expone endpoints bulk IN/OUT validos.");
        }

        // [ACURATEX] Los identificadores se usan luego para leer y escribir.
        _readPipeId = bulkIn;
        _writePipeId = bulkOut;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para ajustar el timeout de un pipe WinUSB y evitar bloqueos largos.
    ///
    /// [QUIÉN LA USA]
    /// La usa `ConnectAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta después de descubrir los pipes.
    ///
    /// [ENTRADAS]
    /// Recibe el ID del pipe y el timeout.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia la política de timeout del pipe nativo.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `ConfigurePipeTimeout()` -> pipe más predecible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner un watchdog de tiempo por canal de comunicación.
    ///
    /// [SI NO EXISTIERA]
    /// Una lectura o escritura podría quedarse esperando demasiado tiempo.
    /// </summary>
    // [ACURATEX] Ajusta el timeout de lectura/escritura de cada pipe para evitar bloqueos largos.
    private void ConfigurePipeTimeout(byte pipeId, uint timeoutMs)
    {
        if (_winUsbHandle == nint.Zero || pipeId == 0) {
            return;
        }

        WinUsbNative.WinUsb_SetPipePolicy(
            _winUsbHandle,
            pipeId,
            WinUsbNative.PipeTransferTimeoutPolicy,
            (uint)sizeof(uint),
            ref timeoutMs);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para abortar pipes abiertos y desbloquear cualquier operación en curso.
    ///
    /// [QUIÉN LA USA]
    /// La usan el cierre y la recuperación ante fallo.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al desconectar o al detectar una caída USB.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Interrumpe operaciones de pipe pendientes.
    ///
    /// [FLUJO ACURATEX]
    /// Cleanup USB -> `AbortOpenPipes()` -> pipes desbloqueados.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a forzar abortos de transferencias en un endpoint USB.
    ///
    /// [SI NO EXISTIERA]
    /// Una transferencia bloqueada podría quedar viva demasiado tiempo.
    /// </summary>
    // [ACURATEX] Aborta pipes abiertos para desbloquear cualquier operación en curso.
    private void AbortOpenPipes()
    {
        nint handle;
        byte readPipeId;
        byte writePipeId;

        lock (_nativeSync) {
            handle = _winUsbHandle;
            readPipeId = _readPipeId;
            writePipeId = _writePipeId;
        }

        if (handle == nint.Zero) {
            return;
        }

        if (readPipeId != 0) {
            WinUsbNative.WinUsb_AbortPipe(handle, readPipeId);
        }

        if (writePipeId != 0) {
            WinUsbNative.WinUsb_AbortPipe(handle, writePipeId);
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para liberar el handle WinUSB y el handle del dispositivo.
    ///
    /// [QUIÉN LA USA]
    /// La usan el cierre y la ruta de error del bucle de lectura.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al terminar la conexión o ante una caída.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Libera recursos nativos y pone el transporte en estado inactivo.
    ///
    /// [FLUJO ACURATEX]
    /// Cleanup USB -> `CloseNativeHandles()` -> WinUSB liberado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a desregistrar y apagar un periférico USB.
    ///
    /// [SI NO EXISTIERA]
    /// Los handles nativos podrían quedar vivos tras desconectar.
    /// </summary>
    // [ACURATEX] Libera el handle WinUSB y el handle del archivo del dispositivo.
    private void CloseNativeHandles()
    {
        lock (_nativeSync) {
            if (_winUsbHandle != nint.Zero) {
                WinUsbNative.WinUsb_Free(_winUsbHandle);
                _winUsbHandle = nint.Zero;
            }

            if (_deviceHandle != null) {
                _deviceHandle.Dispose();
                _deviceHandle = null;
            }
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para leer continuamente bytes del endpoint bulk IN y reconstruir
    /// líneas de texto.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `Task.Run` desde `ConnectAsync`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta en segundo plano mientras la conexión USB está abierta.
    ///
    /// [ENTRADAS]
    /// Recibe un `CancellationToken`.
    ///
    /// [SALIDAS]
    /// No devuelve valor porque corre como tarea de fondo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede disparar `LineReceived`, cerrar handles y notificar `ConnectionLost`.
    ///
    /// [FLUJO ACURATEX]
    /// USB bulk IN -> ReadLoop -> ProcessIncomingBytes -> LineReceived.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un lazo de lectura permanente en UART con delimitador de fin de línea.
    ///
    /// [SI NO EXISTIERA]
    /// La app no recibiría respuestas desde el firmware por USB.
    /// </summary>
    private void ReadLoop(CancellationToken cancellationToken)
    {
        // [ACURATEX] Este lazo vive en segundo plano y convierte bytes en líneas visibles.
        // [FLUJO] Este lazo vive en segundo plano y convierte bytes en líneas visibles.
        byte[] buffer = new byte[512];

        try {
            while (!cancellationToken.IsCancellationRequested) {
                if (_winUsbHandle == nint.Zero) {
                    break;
                }

                // [ACURATEX] Cada lectura intenta recuperar un bloque de bytes desde el endpoint IN.
                bool ok = WinUsbNative.WinUsb_ReadPipe(_winUsbHandle, _readPipeId, buffer, buffer.Length, out int transferred, IntPtr.Zero);
                if (!ok) {
                    int error = Marshal.GetLastWin32Error();
                    if (error == WinUsbNative.ErrorSemTimeout) {
                        continue;
                    }

                    if (error == WinUsbNative.ErrorOperationAborted ||
                        error == WinUsbNative.ErrorDeviceNotConnected ||
                        error == WinUsbNative.ErrorInvalidHandle ||
                        error == WinUsbNative.ErrorGenFailure) {
                        break;
                    }

                    throw new Win32Exception(error, "Error leyendo desde WinUSB.");
                }

                if (transferred <= 0) {
                    continue;
                }

                // [ACURATEX] Los bytes crudos se convierten en líneas al detectar `\n`.
                ProcessIncomingBytes(buffer, transferred);
            }
        } finally {
            bool unexpected = !cancellationToken.IsCancellationRequested;
            CloseNativeHandles();
            if (unexpected) {
                // [ACURATEX] Si no hubo cancelación, la caída se anuncia a la capa superior.
                ConnectionLost?.Invoke();
            }
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para reconstruir texto carácter por carácter hasta encontrar fin
    /// de línea.
    ///
    /// [QUIÉN LA USA]
    /// La usa `ReadLoop()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta después de recibir bytes válidos por USB.
    ///
    /// [ENTRADAS]
    /// Recibe el buffer y la cantidad de bytes transferidos.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Acumula texto y dispara `LineReceived` al completar una línea.
    ///
    /// [FLUJO ACURATEX]
    /// Bytes USB -> `ProcessIncomingBytes()` -> línea reconstruida.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a ensamblar una trama de bytes hasta detectar fin de mensaje.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte no convertiría correctamente los fragmentos en líneas.
    /// </summary>
    // [ACURATEX] Reconstruye texto carácter por carácter hasta encontrar fin de línea.
    private void ProcessIncomingBytes(byte[] buffer, int transferred)
    {
        // [ACURATEX] USB entrega fragmentos; aquí se reconstruyen líneas completas antes del evento.
        // [ACURATEX] El acumulador reconstruye texto porque USB llega por fragmentos, no por líneas.
        for (int i = 0; i < transferred; i++) {
            char c = (char)buffer[i];

            if (c == '\r') {
                continue;
            }

            if (c == '\n') {
                if (_lineBuilder.Length > 0) {
                    string line = _lineBuilder.ToString().Trim();
                    _lineBuilder.Clear();

                    if (!string.IsNullOrWhiteSpace(line)) {
                        LineReceived?.Invoke(line);
                    }
                }
                continue;
            }

            _lineBuilder.Append(c);
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para escribir trazas cuando se envían bloques de archivo por USB.
    ///
    /// [QUIÉN LA USA]
    /// La usa `SendLineAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta solo para líneas `FILE_DATA`.
    ///
    /// [ENTRADAS]
    /// Recibe la línea y el tamaño total del payload.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Escribe información de diagnóstico en `Trace`.
    ///
    /// [FLUJO ACURATEX]
    /// USB send -> `LogOutgoingLineIfFileData()` -> diagnóstico.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a imprimir una traza de depuración solo para bloques grandes de datos.
    ///
    /// [SI NO EXISTIERA]
    /// Los envíos de archivo por USB serían más difíciles de rastrear.
    /// </summary>
    // [ACURATEX] Este trazado solo ayuda a diagnosticar transferencias de archivos.
    private static void LogOutgoingLineIfFileData(string line, int payloadLength)
    {
        if (string.IsNullOrEmpty(line) || !line.StartsWith("FILE_DATA|0|", StringComparison.Ordinal)) {
            return;
        }

        string head = line[..Math.Min(24, line.Length)];
        string tail = line[^Math.Min(24, line.Length)..];
        Trace.WriteLine(
            $"[WinUsbControllerTransport] write rawLen={line.Length} payloadLen={payloadLength} head={head} tail={tail} newline=\\n");
    }
}
