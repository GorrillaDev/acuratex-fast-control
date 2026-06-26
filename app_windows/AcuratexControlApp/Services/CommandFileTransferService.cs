// [ACURATEX] Este servicio implementa el protocolo de archivos FILE_* contra el tester.
// [FLUJO] UI -> servicio de archivos -> FILE_BEGIN/DATA/END -> firmware.
// [EQUIV MCU] Se parece a mover bloques de memoria entre una RAM temporal y una flash externa.
// Es el puente entre la UI y la memoria de archivos del firmware.
using System.Diagnostics;
using System.Text;

namespace AcuratexControlApp.Services;

// [C#] `sealed` fija que esta clase ya es la implementacion concreta del servicio.
public sealed class CommandFileTransferService : ICommandFileTransferService, IDisposable
{
    // [ACURATEX] Limite duro para no saturar la memoria del firmware.
    private const int MaxCommandFileSizeBytes = 65536;
    // [ACURATEX] Nombre de archivo maximo admitido por el protocolo y la validacion local.
    private const int MaxFileNameLength = 48;
    // [ACURATEX] Tamano de chunk usado para dividir archivos al enviarlos.
    // [C#] Los chunks son segmentos pequeños para que el firmware no reciba una linea enorme.
    private const int FileDataChunkSize = 32;
    // [ACURATEX] Pista de diagnostico para lineas largas de Base64.
    private const int Base64BufferHintLineLength = 120;
    // [ACURATEX] Limite de caracteres por linea que el firmware puede tolerar.
    private const int MaxFirmwareLineLengthChars = 191;
    // [ACURATEX] Cantidad de Base64 que se muestra en trazas para no inundar la consola.
    private const int Base64PreviewChars = 12;
    // [ACURATEX] Tiempo de espera para respuesta del firmware por cada comando FILE_*.
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    // [C#] `_connection` es el transporte unico que usa el servicio para enviar lineas.
    private readonly IConnectionController _connection;
    // [ACURATEX] `SemaphoreSlim` evita que dos operaciones de archivos se mezclen.
    // [C#] Esto funciona como una compuerta que deja pasar una sola transferencia a la vez.
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para conectar el servicio de archivos con el controlador de
    /// conexión real.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama el contenedor de dependencias.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al crear el servicio.
    ///
    /// [ENTRADAS]
    /// Recibe el controlador de conexión.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda la referencia al transporte central.
    ///
    /// [FLUJO ACURATEX]
    /// DI -> CommandFileTransferService -> ConnectionController -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inyectar el driver de comunicación que transportará los archivos.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no tendría por dónde mandar FILE_*.
    /// </summary>
    public CommandFileTransferService(IConnectionController connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para cargar un archivo mínimo de prueba sin depender de la UI.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman diagnósticos o tests internos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando se quiere validar la ruta FILE_* con un texto pequeño.
    ///
    /// [ENTRADAS]
    /// Puede recibir progreso y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de subir `test.txt`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía comandos de archivo al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> UploadMinimalTestFileAsync -> UploadTextFileAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a mandar una trama de prueba para chequear la ruta de almacenamiento.
    ///
    /// [SI NO EXISTIERA]
    /// No habría un caso mínimo para verificar la transferencia.
    /// </summary>
    public Task<CommandFileUploadResult> UploadMinimalTestFileAsync(
        IProgress<CommandFileUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        byte[] fileBytes = Encoding.ASCII.GetBytes("hola");
        return UploadTextFileAsync("test.txt", fileBytes, progress, cancellationToken);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para subir un TXT arbitrario al tester usando chunks Base64.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman la UI de edición y el guardado de archivos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al cargar un archivo hacia el firmware.
    ///
    /// [ENTRADAS]
    /// Recibe nombre, bytes, progreso y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de carga.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Ejecuta `FILE_BEGIN`, `FILE_DATA`, `FILE_END` y `FILE_SELECT`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> UploadTextFileAsync -> FILE_* por `IConnectionController` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un archivo en flash por bloques pequeños.
    ///
    /// [SI NO EXISTIERA]
    /// El tester no podría recibir archivos de texto.
    /// </summary>
    public async Task<CommandFileUploadResult> UploadTextFileAsync(
        string fileName,
        byte[] fileBytes,
        IProgress<CommandFileUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        // [ACURATEX] El nombre se limpia antes de enviar cualquier comando.
        // [C#] La validacion se hace antes del `SemaphoreSlim` para fallar rapido si el archivo no sirve.
        string cleanName = ValidateFileNameOrThrow(fileName);
        ValidateFileBytesOrThrow(fileBytes);

        // [ACURATEX] El semáforo impide que otra operación de archivo interrumpa esta carga.
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            return await UploadTextFileWithChunkSizeAsync(
                cleanName,
                fileBytes,
                FileDataChunkSize,
                progress,
                cancellationToken).ConfigureAwait(false);
        } finally {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para pedir únicamente los nombres de archivos disponibles.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman listados de UI y validaciones previas a descargar/subir.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando hay que refrescar la lista de archivos remotos.
    ///
    /// [ENTRADAS]
    /// Recibe cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve nombres de archivo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Consulta el firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ListFilesAsync -> FILE_LIST -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enumerar archivos de una memoria externa.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría qué archivos remotos existen.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            IReadOnlyList<ParsedFileListEntry> files = await ListFileEntriesCoreAsync(cancellationToken).ConfigureAwait(false);
            return files.Select(static file => file.FileName).ToArray();
        } finally {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para enriquecer la lista remota con tamaño, selección y línea cruda.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la vista de archivos del tester.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al abrir o refrescar la tabla de archivos remotos.
    ///
    /// [ENTRADAS]
    /// Recibe cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve entradas enriquecidas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Hace varias consultas al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ListTesterFilesAsync -> FILE_LIST + FILE_INFO -> filas detalladas.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer descriptores de cada archivo en un filesystem.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla tendría menos contexto para cada archivo.
    /// </summary>
    public async Task<IReadOnlyList<TesterFileEntry>> ListTesterFilesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            IReadOnlyList<ParsedFileListEntry> listedFiles = await ListFileEntriesCoreAsync(cancellationToken).ConfigureAwait(false);
            List<TesterFileEntry> entries = new(listedFiles.Count);

            foreach (ParsedFileListEntry listedFile in listedFiles) {
                CommandFileInfo? info = await GetFileInfoCoreAsync(listedFile.FileName, cancellationToken).ConfigureAwait(false);
                entries.Add(new TesterFileEntry(
                    listedFile.FileName,
                    info?.SizeBytes,
                    info?.IsSelected ?? listedFile.IsSelected,
                    info?.RawLine));
            }

            return entries;
        } finally {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para descargar un archivo completo desde el tester por bloques.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman editores y rutas de copia local.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario abre o copia un archivo remoto.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve bytes, texto y ruta temporal.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Maneja `FILE_GET`, `FILE_GET_NEXT`, `FILE_END` y guarda caché local.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> DownloadFileAsync -> FILE_GET/FILE_GET_NEXT -> archivo local.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer memoria externa en bloques secuenciales.
    ///
    /// [SI NO EXISTIERA]
    /// No se podrían abrir archivos remotos para editar.
    /// </summary>
    public async Task<CommandFileDownloadResult> DownloadFileAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        string cleanName = (fileName ?? string.Empty).Trim();

        try {
            cleanName = ValidateFileNameOrThrow(cleanName);

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                // [ACURATEX] Primero se pide el tamaño y el inicio de la transferencia.
                // [C#] Esta fase inicial usa `TaskCompletionSource` internamente en `SendFileCommandAsync`.
                FileCommandResponse beginResponse = await SendFileCommandAsync(
                    $"FILE_GET|{cleanName}",
                    line => line.StartsWith($"FILE_BEGIN|{cleanName}|", StringComparison.OrdinalIgnoreCase),
                    IsGetErrorLine,
                    cancellationToken).ConfigureAwait(false);
                ThrowIfFileError(beginResponse, "FILE_GET");

                int expectedSize = ParseFileBeginSizeOrThrow(beginResponse.Line, cleanName);
                using MemoryStream received = new(expectedSize);
                int expectedIndex = 0;
                bool completed = false;

                while (!completed) {
                    // [ACURATEX] Cada ciclo pide el siguiente chunk en orden.
                    FileCommandResponse nextResponse = await SendFileCommandAsync(
                        "FILE_GET_NEXT",
                        line => line.StartsWith($"FILE_DATA|{expectedIndex}|", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(line, $"FILE_END|{cleanName}", StringComparison.OrdinalIgnoreCase),
                        IsGetNextErrorLine,
                        cancellationToken).ConfigureAwait(false);
                    ThrowIfFileError(nextResponse, "FILE_GET_NEXT");

                    if (string.Equals(nextResponse.Line, $"FILE_END|{cleanName}", StringComparison.OrdinalIgnoreCase)) {
                        completed = true;
                        continue;
                    }

                    byte[] chunk = ParseFileDataChunkOrThrow(nextResponse.Line, expectedIndex);
                    received.Write(chunk, 0, chunk.Length);
                    if (received.Length > expectedSize) {
                        throw new InvalidOperationException("El tester envio mas bytes que el tamano declarado.");
                    }

                    expectedIndex++;
                }

                byte[] bytes = received.ToArray();
                if (bytes.Length != expectedSize) {
                    throw new InvalidOperationException(
                        $"Descarga incompleta. Esperado={expectedSize} bytes, recibido={bytes.Length} bytes.");
                }

                // [ACURATEX] La copia descargada se guarda en caché para edición local.
                string tempPath = GetFirmwareFileCachePath(cleanName);
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

                return new CommandFileDownloadResult(
                    true,
                    null,
                    cleanName,
                    bytes,
                    Encoding.UTF8.GetString(bytes),
                    tempPath);
            } finally {
                _operationGate.Release();
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return new CommandFileDownloadResult(
                false,
                string.IsNullOrWhiteSpace(ex.Message) ? "No se pudo descargar el archivo del tester." : ex.Message,
                cleanName,
                Array.Empty<byte>(),
                string.Empty,
                string.Empty);
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir el texto editado en una nueva subida al tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el editor cuando el usuario pulsa guardar.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al guardar una edición.
    ///
    /// [ENTRADAS]
    /// Recibe nombre, texto, progreso y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de guardado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Reutiliza `UploadTextFileAsync` y actualiza la caché local.
    ///
    /// [FLUJO ACURATEX]
    /// Editor -> SaveEditedTextAsync -> FILE_* -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a volver a grabar una memoria externa con contenido nuevo.
    ///
    /// [SI NO EXISTIERA]
    /// El editor no podría persistir cambios en el tester.
    /// </summary>
    public async Task<CommandFileSaveResult> SaveEditedTextAsync(
        string fileName,
        string text,
        IProgress<CommandFileUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        string cleanName = (fileName ?? string.Empty).Trim();

        try {
            cleanName = ValidateFileNameOrThrow(cleanName);
            byte[] fileBytes = Encoding.UTF8.GetBytes(text ?? string.Empty);

            CommandFileUploadResult uploadResult = await UploadTextFileAsync(
                cleanName,
                fileBytes,
                progress,
                cancellationToken).ConfigureAwait(false);

            string tempPath = GetFirmwareFileCachePath(cleanName);
            await File.WriteAllBytesAsync(tempPath, fileBytes, cancellationToken).ConfigureAwait(false);

            return new CommandFileSaveResult(
                uploadResult.Success,
                uploadResult.Success ? null : uploadResult.Message,
                cleanName,
                fileBytes.Length,
                tempPath);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return new CommandFileSaveResult(
                false,
                string.IsNullOrWhiteSpace(ex.Message) ? "No se pudo guardar el archivo en el tester." : ex.Message,
                cleanName,
                0,
                null);
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para marcar un archivo como seleccionado en el tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman las vistas de archivos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pide activar un archivo.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `bool`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Manda `FILE_SELECT|...`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SelectFileAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a elegir qué banco de memoria queda activo.
    ///
    /// [SI NO EXISTIERA]
    /// No se podría marcar un archivo como activo.
    /// </summary>
    public async Task<bool> SelectFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        string cleanName = ValidateFileNameOrThrow(fileName);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            FileCommandResponse response = await SendFileCommandAsync(
                $"FILE_SELECT|{cleanName}",
                line => string.Equals(line, $"ACK FILE_SELECT {cleanName}", StringComparison.OrdinalIgnoreCase),
                IsSelectErrorLine,
                cancellationToken).ConfigureAwait(false);

            return !response.IsError;
        } finally {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para seleccionar un programa de cabezal en el firmware.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los servicios de perfil.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando se aplica un programa de cabezal.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `bool`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Manda `HEAD_PROGRAM_SELECT|...`.
    ///
    /// [FLUJO ACURATEX]
    /// Perfil -> SelectHeadProgramAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir cuál perfil de operación queda habilitado.
    ///
    /// [SI NO EXISTIERA]
    /// No habría activación de programa de cabezal.
    /// </summary>
    public async Task<bool> SelectHeadProgramAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        string cleanName = ValidateFileNameOrThrow(fileName);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            FileCommandResponse response = await SendHeadProgramCommandAsync(
                $"HEAD_PROGRAM_SELECT|{cleanName}",
                line => string.Equals(line, $"OK|HEAD_PROGRAM_SELECT|{cleanName}", StringComparison.OrdinalIgnoreCase),
                line => line.StartsWith("ERR|HEAD_PROGRAM_SELECT|", StringComparison.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);

            return !response.IsError;
        } finally {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para consultar tamaño y selección de un archivo remoto.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman las vistas de administración de archivos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al refrescar la información de un archivo.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve información del archivo o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Consulta el firmware con `FILE_INFO`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> GetFileInfoAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una cabecera de archivo desde flash.
    ///
    /// [SI NO EXISTIERA]
    /// No se podrían mostrar metadatos remotos.
    /// </summary>
    public async Task<CommandFileInfo?> GetFileInfoAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        string cleanName = ValidateFileNameOrThrow(fileName);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            return await GetFileInfoCoreAsync(cleanName, cancellationToken).ConfigureAwait(false);
        } finally {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para borrar un archivo remoto del tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI tras confirmar la eliminación.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario elimina un archivo.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `bool`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Manda `FILE_DELETE|...`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> DeleteFileAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a borrar un archivo de una memoria externa.
    ///
    /// [SI NO EXISTIERA]
    /// No habría limpieza remota de archivos.
    /// </summary>
    public async Task<bool> DeleteFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        EnsureConnected();

        string cleanName = ValidateFileNameOrThrow(fileName);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            FileCommandResponse response = await SendFileCommandAsync(
                $"FILE_DELETE|{cleanName}",
                line => string.Equals(line, $"ACK FILE_DELETE {cleanName}", StringComparison.OrdinalIgnoreCase),
                IsDeleteErrorLine,
                cancellationToken).ConfigureAwait(false);

            return !response.IsError;
        } finally {
            _operationGate.Release();
        }
    }

    // [C#] `Dispose` apaga el semáforo y deja el servicio listo para salir.
    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _operationGate.Dispose();
    }

    // [ACURATEX] Núcleo de listados remotos: hace FILE_LIST y parsea la respuesta.
    // [ACURATEX] Lee la lista remota y la convierte en entradas con bandera de seleccion.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para pedir `FILE_LIST` al firmware y convertir la respuesta en una lista util para la app.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llaman `ListFilesAsync()` y `ListTesterFilesAsync()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando la UI quiere refrescar el catalogo remoto de archivos.
    ///
    /// [ENTRADAS]
    /// Recibe cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve una lista de entradas parseadas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envia el comando `FILE_LIST` al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ListFileEntriesCoreAsync()` -> `FILE_LIST` -> parseo -> lista visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un directorio remoto y convertirlo en una tabla de trabajo.
    ///
    /// [SI NO EXISTIERA]
    /// Cada consumidor tendria que parsear `FILE_LIST` por su cuenta.
    /// </summary>
    private async Task<IReadOnlyList<ParsedFileListEntry>> ListFileEntriesCoreAsync(CancellationToken cancellationToken)
    {
        FileCommandResponse response = await SendFileCommandAsync(
            "FILE_LIST",
            line => line.StartsWith("FILE_LIST", StringComparison.OrdinalIgnoreCase),
            IsListErrorLine,
            cancellationToken).ConfigureAwait(false);

        ThrowIfFileError(response, "FILE_LIST");
        return ParseFileList(response.Line);
    }

    // [ACURATEX] Núcleo de consulta FILE_INFO con manejo de no encontrado.
    // [ACURATEX] Consulta el tamano y estado de un archivo concreto en el firmware.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para pedir `FILE_INFO` de un archivo concreto y convertir la respuesta en metadatos.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama la vista de archivos remotos.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al abrir el detalle de un archivo remoto.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre limpio del archivo y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve metadatos del archivo o `null` si el firmware dice que no existe.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envia el comando `FILE_INFO`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `GetFileInfoCoreAsync()` -> `FILE_INFO|...` -> detalle del archivo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a consultar los atributos de un archivo en una memoria externa.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria mostrar tamaño o estado de un archivo remoto.
    /// </summary>
    private async Task<CommandFileInfo?> GetFileInfoCoreAsync(string cleanName, CancellationToken cancellationToken)
    {
        FileCommandResponse response = await SendFileCommandAsync(
            $"FILE_INFO|{cleanName}",
            line => line.StartsWith($"FILE_INFO|{cleanName}|", StringComparison.OrdinalIgnoreCase),
            IsInfoErrorLine,
            cancellationToken).ConfigureAwait(false);

        if (response.IsError) {
            if (response.Line.Contains("FILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            throw new InvalidOperationException($"Tester respondio error: {response.Line}");
        }

        return ParseFileInfo(response.Line);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir la respuesta `FILE_LIST` del firmware en una lista
    /// de entradas fáciles de usar por la app.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ListFileEntriesCoreAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el firmware devuelve la lista de archivos remotos.
    ///
    /// [ENTRADAS]
    /// Recibe una línea de texto cruda.
    ///
    /// [SALIDAS]
    /// Devuelve una lista de entradas parseadas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// `FILE_LIST` -> `ParseFileList()` -> nombres + selección.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a decodificar una lista de directorios recibida por serial.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que interpretar sola la lista textual del firmware.
    /// </summary>
    private static IReadOnlyList<ParsedFileListEntry> ParseFileList(string line)
    {
        if (string.IsNullOrWhiteSpace(line)
            || !line.StartsWith("FILE_LIST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(line.Trim(), "FILE_LIST EMPTY", StringComparison.OrdinalIgnoreCase)) {
            return Array.Empty<ParsedFileListEntry>();
        }

        string payload = line.Length > 9
            ? line[9..].Trim()
            : string.Empty;

        if (payload.Length == 0) {
            return Array.Empty<ParsedFileListEntry>();
        }

        return payload
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static raw =>
            {
                string item = raw.Trim();
                bool isSelected = item.EndsWith("*", StringComparison.Ordinal);
                string fileName = isSelected ? item.TrimEnd('*') : item;
                return new ParsedFileListEntry(fileName, isSelected);
            })
            .Where(static entry => entry.FileName.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir una línea `FILE_INFO` en metadatos del archivo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `GetFileInfoCoreAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al consultar el detalle de un archivo remoto.
    ///
    /// [ENTRADAS]
    /// Recibe la línea cruda del firmware.
    ///
    /// [SALIDAS]
    /// Devuelve metadatos o `null` si la línea no sirve.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// `FILE_INFO` -> `ParseFileInfo()` -> tamaño/selección/estado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un descriptor de bloque o archivo desde memoria externa.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no podría mostrar tamaño ni estado de un archivo concreto.
    /// </summary>
    private static CommandFileInfo? ParseFileInfo(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) {
            return null;
        }

        string[] parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || !string.Equals(parts[0], "FILE_INFO", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        string name = parts[1];
        int size = 0;
        bool selected = false;

        foreach (string part in parts.Skip(2)) {
            if (part.StartsWith("SIZE=", StringComparison.OrdinalIgnoreCase)) {
                _ = int.TryParse(part[5..], out size);
                continue;
            }

            if (part.StartsWith("SELECTED=", StringComparison.OrdinalIgnoreCase)) {
                selected = string.Equals(part[9..], "1", StringComparison.OrdinalIgnoreCase);
            }
        }

        return new CommandFileInfo(name, size, selected, line);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para enviar un comando FILE_* y esperar su respuesta asociada.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman las operaciones de listado, descarga, guardado, selección y borrado.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cada vez que el protocolo de archivos necesita una respuesta concreta.
    ///
    /// [ENTRADAS]
    /// Recibe línea de comando, matcher de éxito, matcher de error y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve la respuesta capturada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Suscribe temporalmente un handler a `LineReceived` y manda la línea al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> SendFileCommandAsync -> LineReceived temporal -> firmware -> respuesta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un request/response con interrupción temporal de recepción.
    ///
    /// [SI NO EXISTIERA]
    /// Cada comando FILE_* tendría que duplicar la lógica de espera.
    /// </summary>
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para enviar un comando `FILE_*` y esperar la respuesta asociada por evento.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llaman las operaciones de listado, descarga, guardado, seleccion y borrado.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que el protocolo de archivos necesita una respuesta concreta del firmware.
    ///
    /// [ENTRADAS]
    /// Recibe la linea de comando, dos predicados para reconocer exito/error y la cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve la respuesta capturada como `FileCommandResponse`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Suscribe temporalmente un handler a `LineReceived` y manda la linea al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `SendFileCommandAsync()` -> handler temporal -> firmware -> respuesta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un request/response con callback temporal de recepcion.
    ///
    /// [SI NO EXISTIERA]
    /// Cada comando `FILE_*` tendria que duplicar la logica de espera.
    /// </summary>
    private async Task<FileCommandResponse> SendFileCommandAsync(
        string commandLine,
        Func<string, bool> successMatcher,
        Func<string, bool> errorMatcher,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            throw new InvalidOperationException("Comando FILE invalido.");
        }

        // [ACURATEX] `TaskCompletionSource` convierte una respuesta futura del firmware en una tarea esperable.
        // [EQUIV MCU] Es parecido a instalar un callback que se completa cuando llega la interrupcion correcta.
        TaskCompletionSource<FileCommandResponse> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // [ACURATEX] El callback temporal captura la respuesta del firmware.
        void HandleLine(string line)
        {
            string clean = line?.Trim() ?? string.Empty;
            if (clean.Length == 0) {
                return;
            }

            if (clean.StartsWith("ERR FILE", StringComparison.OrdinalIgnoreCase)) {
                if (errorMatcher(clean)) {
                    waiter.TrySetResult(new FileCommandResponse(true, clean));
                } else {
                    Trace.WriteLine($"[CommandFileTransfer] Ignored unrelated FILE error while waiting '{commandLine}': {clean}");
                }
                return;
            }

            if (successMatcher(clean)) {
                waiter.TrySetResult(new FileCommandResponse(false, clean));
            }
        }

        // [C#] `+=` registra un handler temporal y luego se quita en el `finally`.
        _connection.LineReceived += HandleLine;
        try {
            LogOutboundLineDiagnostics(commandLine);

            // [ACURATEX] El timeout evita esperar indefinidamente una respuesta de archivos.
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResponseTimeout);
            using CancellationTokenRegistration registration = timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<FileCommandResponse>)state!).TrySetCanceled(),
                waiter);

            await _connection.SendLineAsync(commandLine, cancellationToken).ConfigureAwait(false);

            try {
                return await waiter.Task.ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException($"Timeout esperando respuesta de: {commandLine}");
            }
        } finally {
            _connection.LineReceived -= HandleLine;
        }
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para enviar `HEAD_PROGRAM_SELECT` usando la misma mecanica de espera por evento que los comandos de archivo.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el flujo que activa un programa de cabezal.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando la app selecciona un programa remoto y debe esperar confirmacion.
    ///
    /// [ENTRADAS]
    /// Recibe la linea de comando, dos predicados de exito/error y la cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve la respuesta capturada como `FileCommandResponse`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Suscribe temporalmente un handler a `LineReceived`.
    ///
    /// [FLUJO ACURATEX]
    /// Seleccion de programa -> `SendHeadProgramCommandAsync()` -> firmware -> respuesta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a confirmar la carga de un preset antes de dejarlo activo.
    ///
    /// [SI NO EXISTIERA]
    /// La selección de programa no tendría una ruta de confirmación uniforme.
    /// </summary>
    private async Task<FileCommandResponse> SendHeadProgramCommandAsync(
        string commandLine,
        Func<string, bool> successMatcher,
        Func<string, bool> errorMatcher,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            throw new InvalidOperationException("Comando HEAD invalido.");
        }

        // [ACURATEX] Este segundo `TaskCompletionSource` hace el mismo papel pero para comandos de programa de cabezal.
        // [C#] Se usa una espera manual porque la respuesta llega por evento, no como valor de retorno directo.
        TaskCompletionSource<FileCommandResponse> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleLine(string line)
        {
            string clean = line?.Trim() ?? string.Empty;
            if (clean.Length == 0) {
                return;
            }

            if (errorMatcher(clean)) {
                waiter.TrySetResult(new FileCommandResponse(true, clean));
                return;
            }

            if (successMatcher(clean)) {
                waiter.TrySetResult(new FileCommandResponse(false, clean));
            }
        }

        _connection.LineReceived += HandleLine;
        try {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResponseTimeout);
            using CancellationTokenRegistration registration = timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<FileCommandResponse>)state!).TrySetCanceled(),
                waiter);

            // [ACURATEX] La orden se manda una sola vez; luego se espera la respuesta del firmware.
            await _connection.SendLineAsync(commandLine, cancellationToken).ConfigureAwait(false);

            try {
                return await waiter.Task.ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException($"Timeout esperando respuesta de: {commandLine}");
            }
        } finally {
            _connection.LineReceived -= HandleLine;
        }
    }

    // [ACURATEX] Convierte una respuesta de error en excepción con contexto.
    // [ACURATEX] Convierte respuestas de error del firmware en excepciones de flujo.
    private static void ThrowIfFileError(FileCommandResponse response, string operation)
    {
        if (!response.IsError) {
            return;
        }

        // [ACURATEX] La funcion convierte una respuesta marcada como error en excepcion controlada.
        throw new InvalidOperationException($"Tester respondio error en {operation}: {response.Line}");
    }

    // [ACURATEX] Añade diagnóstico detallado cuando un chunk FILE_DATA falla.
    // [ACURATEX] Interpreta fallos de FILE_DATA y conserva el contexto tecnico.
    private static void ThrowIfFileDataError(
        FileCommandResponse response,
        int index,
        int chunkBytes,
        int base64Length,
        int lineLength,
        string base64Head,
        string base64Tail,
        int fileDataChunkSize)
    {
        if (!response.IsError) {
            return;
        }

        // [ACURATEX] El diagnostico incluye tamanos para detectar si el fallo fue por limite de buffer.
        string diagnostic =
            $"chunkBytes={chunkBytes}, base64Len={base64Length}, lineLen={lineLength}, b64Head={base64Head}, b64Tail={base64Tail}";

        Trace.WriteLine($"[CommandFileTransfer] FILE_DATA error index={index}. {diagnostic}. firmware='{response.Line}'");

        if (response.Line.Contains("FILE_B64", StringComparison.OrdinalIgnoreCase)) {
            if (lineLength > Base64BufferHintLineLength) {
                Trace.WriteLine(
                    $"[CommandFileTransfer] Firmware rechazo Base64. Posible limite de buffer. Reducir FileDataChunkSize (actual={fileDataChunkSize}).");
            }

            throw new FileDataRejectedException(
                $"Error al enviar FILE_DATA index {index}. El firmware rechazo el Base64. Detalle: {diagnostic}. Respuesta: {response.Line}",
                true,
                index,
                lineLength,
                response.Line);
        }

        throw new InvalidOperationException($"Tester respondio error en FILE_DATA index={index}: {response.Line}. {diagnostic}");
    }

    // [ACURATEX] Ejecuta la subida en varias fases y reporta progreso.
    // [ACURATEX] Parte el archivo en chunks y aplica el protocolo FILE_BEGIN/DATA/END.
    private async Task<CommandFileUploadResult> UploadTextFileWithChunkSizeAsync(
        string cleanName,
        byte[] fileBytes,
        int chunkSize,
        IProgress<CommandFileUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // [FLUJO] `FILE_BEGIN` abre la transferencia; luego `FILE_DATA` recorre los chunks y `FILE_END` la cierra.
        // [C#] `Math.Max` evita un total de chunks cero incluso si el archivo es pequenio.
        int totalChunks = Math.Max(1, (fileBytes.Length + chunkSize - 1) / chunkSize);
        progress?.Report(new CommandFileUploadProgress(0, totalChunks, 0, $"Iniciando transferencia (chunk={chunkSize})..."));

        FileCommandResponse beginResponse = await SendFileCommandAsync(
            $"FILE_BEGIN|{cleanName}|{fileBytes.Length}",
            line => string.Equals(line, $"ACK FILE_BEGIN {cleanName}", StringComparison.OrdinalIgnoreCase),
            IsBeginErrorLine,
            cancellationToken).ConfigureAwait(false);
        ThrowIfFileError(beginResponse, "FILE_BEGIN");

        for (int index = 0; index < totalChunks; index++) {
            int offset = index * chunkSize;
            int chunkLength = Math.Min(chunkSize, fileBytes.Length - offset);
            byte[] chunk = new byte[chunkLength];
            Buffer.BlockCopy(fileBytes, offset, chunk, 0, chunkLength);
            string base64 = Convert.ToBase64String(chunk);
            ValidateGeneratedBase64OrThrow(base64, chunk, index);

            string dataLine = $"FILE_DATA|{index}|{base64}";
            EnsureDataLineFitsFirmwareBufferOrThrow(index, dataLine.Length);
            LogDataChunkDiagnostics(index, chunkLength, base64, dataLine.Length);

            FileCommandResponse dataResponse = await SendFileCommandAsync(
                dataLine,
                line => string.Equals(line, $"ACK FILE_DATA {index}", StringComparison.OrdinalIgnoreCase),
                IsDataErrorLine,
                cancellationToken).ConfigureAwait(false);
            ThrowIfFileDataError(
                dataResponse,
                index,
                chunkLength,
                base64.Length,
                dataLine.Length,
                HeadPreview(base64),
                TailPreview(base64),
                chunkSize);

            int sentChunks = index + 1;
            int progressPercent = (int)Math.Round(sentChunks * 100d / totalChunks);
            progress?.Report(new CommandFileUploadProgress(
                sentChunks,
                totalChunks,
                progressPercent,
                $"Subiendo chunk {sentChunks}/{totalChunks} (chunk={chunkSize})"));
        }

        FileCommandResponse endResponse = await SendFileCommandAsync(
            $"FILE_END|{cleanName}",
            line => line.StartsWith($"ACK FILE_END {cleanName}", StringComparison.OrdinalIgnoreCase),
            IsEndErrorLine,
            cancellationToken).ConfigureAwait(false);
        ThrowIfFileError(endResponse, "FILE_END");

        FileCommandResponse selectResponse = await SendFileCommandAsync(
            $"FILE_SELECT|{cleanName}",
            line => string.Equals(line, $"ACK FILE_SELECT {cleanName}", StringComparison.OrdinalIgnoreCase),
            IsSelectErrorLine,
            cancellationToken).ConfigureAwait(false);
        ThrowIfFileError(selectResponse, "FILE_SELECT");

        FileCommandResponse infoResponse = await SendFileCommandAsync(
            $"FILE_INFO|{cleanName}",
            line => line.StartsWith($"FILE_INFO|{cleanName}|", StringComparison.OrdinalIgnoreCase),
            IsInfoErrorLine,
            cancellationToken).ConfigureAwait(false);
        ThrowIfFileError(infoResponse, "FILE_INFO");

        progress?.Report(new CommandFileUploadProgress(
            totalChunks,
            totalChunks,
            100,
            $"Archivo cargado y seleccionado en el tester (chunk={chunkSize})."));

        return new CommandFileUploadResult(
            true,
            "Archivo cargado en el tester correctamente.",
            cleanName,
            fileBytes.Length,
            totalChunks,
            infoResponse.Line);
    }

    // [ACURATEX] Extrae y valida el tamaño anunciado por FILE_BEGIN.
    // [ACURATEX] Extrae el tamano confirmado por el firmware al iniciar FILE_BEGIN.
    private static int ParseFileBeginSizeOrThrow(string line, string expectedName)
    {
        string[] parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !string.Equals(parts[0], "FILE_BEGIN", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[1], expectedName, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[2], out int size)
            || size < 0
            || size > MaxCommandFileSizeBytes) {
            throw new InvalidOperationException($"Respuesta FILE_BEGIN invalida: {line}");
        }

        return size;
    }

    // [ACURATEX] Convierte un FILE_DATA de Base64 a bytes crudos.
    // [ACURATEX] Decodifica un chunk Base64 que el firmware confirma como recibido.
    private static byte[] ParseFileDataChunkOrThrow(string line, int expectedIndex)
    {
        string[] parts = line.Split('|', 3, StringSplitOptions.None);
        if (parts.Length != 3
            || !string.Equals(parts[0], "FILE_DATA", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[1], out int index)
            || index != expectedIndex) {
            throw new InvalidOperationException($"Respuesta FILE_DATA invalida: {line}");
        }

        try {
            return Convert.FromBase64String(parts[2]);
        } catch (FormatException ex) {
            throw new InvalidOperationException($"Base64 invalido recibido en FILE_DATA index={expectedIndex}", ex);
        }
    }

    // [ACURATEX] Ruta local de caché para archivos descargados desde el tester.
    // [ACURATEX] Construye la ruta temporal donde se guarda la copia local del archivo remoto.
    private static string GetFirmwareFileCachePath(string cleanName)
    {
        // [ACURATEX] El cache temporal se crea junto al perfil de app para no ensuciar la carpeta del usuario.
        string cacheDirectory = Path.Combine(Path.GetTempPath(), "AccuratexControlApp", "firmware-files");
        Directory.CreateDirectory(cacheDirectory);
        return Path.Combine(cacheDirectory, cleanName);
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para fallar de inmediato si el servicio ya fue destruido.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llaman los flujos publicos antes de usar la conexion.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta antes de cualquier operacion de archivo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor; lanza excepcion si el servicio ya no es utilizable.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Ninguno si el servicio sigue vivo; si no, interrumpe la ejecucion.
    ///
    /// [FLUJO ACURATEX]
    /// Llamada public -> `ThrowIfDisposed()` -> continuidad o error.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar si un periferico ya fue apagado antes de usarlo.
    ///
    /// [SI NO EXISTIERA]
    /// La app podria seguir usando un servicio ya liberado.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(CommandFileTransferService));
        }
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para asegurar que el tester esta conectado antes de mover archivos.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llaman las operaciones de subida, descarga, listado y borrado.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta justo antes de iniciar una operacion FILE_*.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor; lanza excepcion si no hay conexion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Detiene la operacion si el transporte esta cerrado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `EnsureConnected()` -> conexion valida o error.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar que el bus serial o USB este habilitado antes de escribir.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio intentaria hablar con el firmware sin enlace activo.
    /// </summary>
    private void EnsureConnected()
    {
        // [ACURATEX] Ninguna operacion FILE_* puede avanzar si el transporte no esta abierto.
        if (!_connection.IsConnected) {
            throw new InvalidOperationException("No hay conexion activa con el tester.");
        }
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para limpiar y validar el nombre de archivo antes de enviarlo por protocolo.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llaman los metodos que suben, seleccionan o borran archivos.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta antes de mandar cualquier nombre al firmware.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre candidato.
    ///
    /// [SALIDAS]
    /// Devuelve el nombre limpio o lanza excepcion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No cambia estado; solo rechaza nombres peligrosos.
    ///
    /// [FLUJO ACURATEX]
    /// Nombre de UI -> `ValidateFileNameOrThrow()` -> nombre seguro.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar una direccion de memoria antes de escribir.
    ///
    /// [SI NO EXISTIERA]
    /// Un nombre con ruta o caracteres especiales podria romper el protocolo.
    /// </summary>
    private static string ValidateFileNameOrThrow(string fileName)
    {
        string cleanName = (fileName ?? string.Empty).Trim();
        if (cleanName.Length == 0 || cleanName.Length > MaxFileNameLength) {
            throw new InvalidOperationException("Nombre de archivo invalido.");
        }

        if (string.Equals(cleanName, ".selected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cleanName, ".upload.tmp", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Nombre de archivo invalido.");
        }

        if (cleanName.Contains('/') || cleanName.Contains('\\') || cleanName.Contains('|')
            || cleanName.Contains("..", StringComparison.Ordinal)
            || cleanName.Contains('\r') || cleanName.Contains('\n')) {
            throw new InvalidOperationException("Nombre de archivo invalido.");
        }

        return cleanName;
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para asegurar que el archivo tenga bytes y no exceda el tamaño permitido.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama la ruta de subida de archivos.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta antes de dividir el archivo en chunks.
    ///
    /// [ENTRADAS]
    /// Recibe el arreglo de bytes del archivo.
    ///
    /// [SALIDAS]
    /// No devuelve valor; lanza excepcion si el contenido no sirve.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Detiene la subida si el contenido es invalido.
    ///
    /// [FLUJO ACURATEX]
    /// Bytes -> `ValidateFileBytesOrThrow()` -> subida o error.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar que un buffer de escritura no este vacio ni desbordado.
    ///
    /// [SI NO EXISTIERA]
    /// La transferencia podria intentar mandar archivos vacios o demasiado grandes.
    /// </summary>
    private static void ValidateFileBytesOrThrow(byte[] fileBytes)
    {
        if (fileBytes == null) {
            throw new InvalidOperationException("No se pudo leer el archivo.");
        }

        if (fileBytes.Length == 0) {
            throw new InvalidOperationException("El archivo esta vacio");
        }

        if (fileBytes.Length > MaxCommandFileSizeBytes) {
            throw new InvalidOperationException($"El archivo supera {MaxCommandFileSizeBytes} bytes");
        }
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para identificar si una linea corresponde a un error de la fase `FILE_BEGIN`.
    ///
    /// [QUIEN LA USA]
    /// La usa el parser de respuestas FILE_*.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el servicio espera la confirmacion del inicio de transferencia.
    ///
    /// [ENTRADAS]
    /// Recibe una linea de texto.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Linea remota -> `IsBeginErrorLine()` -> clasificacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reconocer una bandera de error especifica en una ISR.
    ///
    /// [SI NO EXISTIERA]
    /// Cada fase tendria que comparar cadenas de error por su cuenta.
    /// </summary>
    private static bool IsBeginErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_BEGIN", "ERR FILE_SIZE", "ERR FILE_FS");
    }

    private static bool IsDataErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_DATA", "ERR FILE_B64", "ERR FILE_SIZE", "ERR FILE_FS", "ERR FILE_STATE");
    }

    private static bool IsEndErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_END", "ERR FILE_SIZE", "ERR FILE_FS", "ERR FILE_STATE");
    }

    private static bool IsSelectErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_NOT_FOUND", "ERR FILE_FS");
    }

    private static bool IsInfoErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_NOT_FOUND", "ERR FILE_FS");
    }

    private static bool IsListErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_FS");
    }

    private static bool IsDeleteErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_NOT_FOUND", "ERR FILE_FS");
    }

    private static bool IsGetErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_NOT_FOUND", "ERR FILE_FS");
    }

    private static bool IsGetNextErrorLine(string line)
    {
        return StartsWithAny(line, "ERR FILE_GET_STATE", "ERR FILE_B64", "ERR FILE_FS");
    }

    // [ACURATEX] Utilidad para comparar varios prefijos de error sin repetir código.
    private static bool StartsWithAny(string line, params string[] prefixes)
    {
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        foreach (string prefix in prefixes) {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para verificar que el Base64 generado localmente reconstruye exactamente el chunk original.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama la ruta de subida de archivos antes de mandar `FILE_DATA`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta por cada chunk codificado.
    ///
    /// [ENTRADAS]
    /// Recibe el texto Base64, los bytes originales y el indice del chunk.
    ///
    /// [SALIDAS]
    /// No devuelve valor; lanza excepcion si el Base64 no coincide.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Detiene la transferencia si la conversion no es exacta.
    ///
    /// [FLUJO ACURATEX]
    /// Bytes -> Base64 -> `ValidateGeneratedBase64OrThrow()` -> envio seguro.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comparar una codificacion previa con los bytes que saldran por el bus.
    ///
    /// [SI NO EXISTIERA]
    /// Un error de conversion podria pasar desapercibido hasta llegar al firmware.
    /// </summary>
    private static void ValidateGeneratedBase64OrThrow(string base64, byte[] originalChunkBytes, int index)
    {
        if (string.IsNullOrEmpty(base64)) {
            throw new InvalidOperationException($"Base64 local invalido en chunk {index}");
        }

        byte[] decoded;
        try {
            decoded = Convert.FromBase64String(base64);
        } catch {
            throw new InvalidOperationException($"Base64 local invalido en chunk {index}");
        }

        if (!decoded.SequenceEqual(originalChunkBytes)) {
            throw new InvalidOperationException("Base64 local no reconstruye el chunk original");
        }

        if (base64.Contains(' ') || base64.Contains('\r') || base64.Contains('\n') || base64.Contains('\t')) {
            throw new InvalidOperationException($"Base64 local invalido en chunk {index}");
        }
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para garantizar que una linea `FILE_DATA` no exceda lo que el firmware puede recibir.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama la ruta de subida antes de enviar cada chunk.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta por cada linea generada para el archivo.
    ///
    /// [ENTRADAS]
    /// Recibe el indice y la longitud total de la linea.
    ///
    /// [SALIDAS]
    /// No devuelve valor; lanza excepcion si la linea es demasiado larga.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Detiene la subida para evitar fragmentos que el firmware rechace.
    ///
    /// [FLUJO ACURATEX]
    /// Chunk -> longitud -> `EnsureDataLineFitsFirmwareBufferOrThrow()` -> envio o error.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a medir el tamaño de una trama antes de meterla en un buffer serial.
    ///
    /// [SI NO EXISTIERA]
    /// La app podria mandar lineas que exceden el buffer del tester.
    /// </summary>
    private static void EnsureDataLineFitsFirmwareBufferOrThrow(int index, int lineLength)
    {
        if (lineLength > MaxFirmwareLineLengthChars) {
            throw new InvalidOperationException(
                $"Linea FILE_DATA demasiado larga en chunk {index}. lineLen={lineLength}, max={MaxFirmwareLineLengthChars}");
        }
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para registrar diagnostico de cada chunk enviado sin mostrar todo el Base64 completo.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama la ruta de subida de archivos.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al preparar cada linea `FILE_DATA`.
    ///
    /// [ENTRADAS]
    /// Recibe el indice, tamaño del chunk, Base64 y longitud de la linea.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Escribe trazas en `Trace`.
    ///
    /// [FLUJO ACURATEX]
    /// Chunk -> `LogDataChunkDiagnostics()` -> traza resumida.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un log de osciloscopio que muestra solo la cabecera y el final de la trama.
    ///
    /// [SI NO EXISTIERA]
    /// Diagnosticar problemas de tamaño o codificacion seria mucho mas dificil.
    /// </summary>
    private static void LogDataChunkDiagnostics(int index, int chunkBytes, string base64, int lineLength)
    {
        Trace.WriteLine(
            $"[CommandFileTransfer] FILE_DATA index={index} chunkBytes={chunkBytes} base64Len={base64.Length} lineLen={lineLength} b64Head={HeadPreview(base64)} b64Tail={TailPreview(base64)}");
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para mostrar solo el inicio del Base64 en diagnosticos.
    ///
    /// [QUIEN LO USA]
    /// Lo usa `LogDataChunkDiagnostics()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al construir trazas resumidas.
    ///
    /// [ENTRADAS]
    /// Recibe un texto Base64.
    ///
    /// [SALIDAS]
    /// Devuelve los primeros caracteres o una cadena vacia.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Base64 -> `HeadPreview()` -> resumen visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer solo los primeros bytes de un buffer para diagnostico.
    ///
    /// [SI NO EXISTIERA]
    /// El log tendria que mostrar la cadena completa.
    /// </summary>
    private static string HeadPreview(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        return value[..Math.Min(Base64PreviewChars, value.Length)];
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para mostrar solo el final del Base64 en diagnosticos.
    ///
    /// [QUIEN LO USA]
    /// Lo usa `LogDataChunkDiagnostics()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al construir trazas resumidas.
    ///
    /// [ENTRADAS]
    /// Recibe un texto Base64.
    ///
    /// [SALIDAS]
    /// Devuelve los ultimos caracteres o una cadena vacia.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Base64 -> `TailPreview()` -> resumen visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inspeccionar la cola de una trama para ver el CRC o el final.
    ///
    /// [SI NO EXISTIERA]
    /// El diagnostico perderia la parte final del contenido codificado.
    /// </summary>
    private static string TailPreview(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        int length = Math.Min(Base64PreviewChars, value.Length);
        return value[^length..];
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para registrar una vista resumida de las lineas FILE_* antes de enviarlas.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama `SendFileCommandAsync()` y otras rutas de envio de archivos.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta justo antes de mandar el comando al firmware.
    ///
    /// [ENTRADAS]
    /// Recibe la linea cruda.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Escribe trazas diagnósticas.
    ///
    /// [FLUJO ACURATEX]
    /// Comando FILE_* -> `LogOutboundLineDiagnostics()` -> traza y envio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un log de bus antes de emitir la trama.
    ///
    /// [SI NO EXISTIERA]
    /// Perderiamos contexto de las lineas enviadas al tester.
    /// </summary>
    private void LogOutboundLineDiagnostics(string rawLine)
    {
        if (string.IsNullOrEmpty(rawLine)) {
            return;
        }

        if (!rawLine.StartsWith("FILE_", StringComparison.Ordinal)) {
            return;
        }

        string transportKind = GetTransportKindForDiagnostics();
        string head = rawLine[..Math.Min(24, rawLine.Length)];
        string tail = rawLine[^Math.Min(24, rawLine.Length)..];

        Trace.WriteLine(
            $"[CommandFileTransfer] outbound transport={transportKind} rawLineLength={rawLine.Length} rawLineHead={head} rawLineTail={tail}");

        // FILE_* via IConnectionController uses the same raw send path as manual console.
        // In current app transports (USB/WinUSB, TCP directo, Serial) no URL encoding is applied.
        Trace.WriteLine(
            $"[CommandFileTransfer] transportLinePreview={head}...{tail} urlEncoded=false sendPath=IConnectionController.SendLineAsync");
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para informar en el log que tipo de transporte esta usando la conexion actual.
    ///
    /// [QUIEN LO USA]
    /// Lo usa `LogOutboundLineDiagnostics()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al crear trazas de salida.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve un nombre legible del transporte.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Conexion -> `GetTransportKindForDiagnostics()` -> texto de log.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer el nombre del bus activo antes de depurar.
    ///
    /// [SI NO EXISTIERA]
    /// El log no indicaria si el envio fue por USB, TCP o Serial.
    /// </summary>
    private string GetTransportKindForDiagnostics()
    {
        if (_connection is ConnectionController controller) {
            return controller.ActiveTransportKind;
        }

        return _connection.GetType().Name;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta excepcion existe para distinguir un rechazo de FILE_DATA con contexto tecnico
    /// especifico.
    ///
    /// [QUIÉN LA USA]
    /// La usan los metodos que validan y transmiten chunks de archivo.
    ///
    /// [CUÁNDO SE USA]
    /// Se lanza cuando el firmware rechaza un chunk o el Base64 local no coincide.
    ///
    /// [ENTRADAS]
    /// Recibe mensaje, indicador de error Base64, indice, longitud de linea y respuesta del firmware.
    ///
    /// [SALIDAS]
    /// Devuelve una excepcion con datos de diagnostico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Interrumpe la transferencia y preserva contexto para la UI.
    ///
    /// [FLUJO ACURATEX]
    /// Transferencia -> `FileDataRejectedException` -> diagnostico/UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un error especifico de transferencia que deja trazabilidad de la trama rechazada.
    ///
    /// [SI NO EXISTIERA]
    /// Todos los rechazos de chunk tendrian el mismo tipo de error generico.
    /// </summary>
    private sealed class FileDataRejectedException : Exception
    {
        public FileDataRejectedException(string message, bool isBase64Error, int index, int lineLength, string firmwareResponse)
            : base(message)
        {
            IsBase64Error = isBase64Error;
            Index = index;
            LineLength = lineLength;
            FirmwareResponse = firmwareResponse;
        }

        public bool IsBase64Error { get; }

        public int Index { get; }

        public int LineLength { get; }

        public string FirmwareResponse { get; }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este record existe para guardar una respuesta puntual de FILE_* con su bandera de error.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan los parsers internos de transferencia de archivos.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al esperar una confirmacion o rechazo del firmware.
    ///
    /// [ENTRADAS]
    /// Recibe si la respuesta es error y la linea cruda.
    ///
    /// [SALIDAS]
    /// Devuelve una estructura inmutable de respuesta.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Firmware -> `FileCommandResponse` -> parser.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una trama de respuesta con su bit de error.
    ///
    /// [SI NO EXISTIERA]
    /// Los helpers tendrian que transportar ambos datos por separado.
    /// </summary>
    private sealed record FileCommandResponse(bool IsError, string Line);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este record existe para representar una entrada de listado de archivos remotos.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan los metodos que listan y enriquecen archivos.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa mientras se reconstruye la lista de archivos del tester.
    ///
    /// [ENTRADAS]
    /// Recibe nombre de archivo y si esta seleccionado.
    ///
    /// [SALIDAS]
    /// Devuelve una entrada de solo datos.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// LISTADO FILE_* -> `ParsedFileListEntry` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una fila de indice en un filesystem embebido.
    ///
    /// [SI NO EXISTIERA]
    /// El listado tendria que usar tuplas o variables sueltas.
    /// </summary>
    private sealed record ParsedFileListEntry(string FileName, bool IsSelected);
}
