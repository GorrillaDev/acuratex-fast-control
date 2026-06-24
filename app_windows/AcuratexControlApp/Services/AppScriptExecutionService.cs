using System.Globalization;

namespace AcuratexControlApp.Services;

public sealed class AppScriptExecutionService : IAppScriptExecutionService
{
    private static readonly TimeSpan HeadActionResponseTimeout = TimeSpan.FromSeconds(60);

    private readonly IConnectionController _connection;
    private readonly ICommandFileTransferService _commandFiles;
    private readonly IHeadProfileService _profiles;
    private readonly SemaphoreSlim _executionGate = new(1, 1);

    public AppScriptExecutionService(
        IConnectionController connection,
        ICommandFileTransferService commandFiles,
        IHeadProfileService profiles)
    {
        _connection = connection;
        _commandFiles = commandFiles;
        _profiles = profiles;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad fija la política de ejecución que usa el servicio para resolver
    /// acciones.
    ///
    /// [QUIÉN LA USA]
    /// La usa `ExecuteActionAsync`.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta en cada acción de cabezal.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el modo activo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// Acción UI -> `ExecutionMode` -> firmware o script.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera que selecciona la ruta de control.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio tendría que guardar el modo en otra variable oculta.
    /// </summary>
    public HeadScriptExecutionMode ExecutionMode { get; } = HeadScriptExecutionMode.FirmwareHeadAction;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para recibir una acción de alto nivel y decidir la ruta correcta de
    /// ejecución.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `CabezalDashboardUnificadoCommandService`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI dispara una acción sobre el cabezal unificado.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, instancia, acción y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve un resultado con éxito o error controlado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar `HEAD_ACTION` o ejecutar un script descargado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> AppScriptExecutionService.ExecuteActionAsync -> firmware o script.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un dispatcher que elige entre ejecución inmediata o script almacenado.
    ///
    /// [SI NO EXISTIERA]
    /// La capa de comandos tendría que duplicar la decisión de ejecución.
    /// </summary>
    public async Task<AppScriptExecutionResult> ExecuteActionAsync(
        HeadSystemKind systemKind,
        string instanceName,
        string actionName,
        CancellationToken cancellationToken = default)
    {
        string cleanInstance = NormalizeToken(instanceName);
        string cleanAction = NormalizeToken(actionName);
        if (cleanInstance.Length == 0 || cleanAction.Length == 0) {
            return new AppScriptExecutionResult(
                false,
                "No se pudo resolver la accion solicitada.",
                null,
                0);
        }

        if (!_profiles.HasActiveProfile(systemKind)) {
            return new AppScriptExecutionResult(
                false,
                $"No hay programa de Cabezal activo para {BuildSystemLabel(systemKind)}.",
                null,
                0);
        }

        if (ExecutionMode == HeadScriptExecutionMode.FirmwareHeadAction) {
            return await ExecuteHeadActionAsync(
                $"{cleanInstance}.{cleanAction}",
                cancellationToken).ConfigureAwait(false);
        }

        HeadBindingResolveResult resolve = _profiles.Resolve(systemKind, instanceName, actionName);
        if (!resolve.Success || resolve.Binding is null) {
            return new AppScriptExecutionResult(
                false,
                resolve.ErrorMessage ?? "No se pudo resolver la accion configurada.",
                null,
                0);
        }

        return await ExecuteBindingAsync(resolve.Binding, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para enviar un `HEAD_ACTION|...` directo al firmware y esperar la
    /// confirmación `OK|HEAD_ACTION|...`.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el servicio de comandos o la propia ruta de ejecución directa.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la acción elegida debe validarse por firmware.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre de la acción y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve un resultado con éxito o error.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Registra un handler temporal en `LineReceived`, manda la línea y espera respuesta.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ExecuteHeadActionAsync -> HEAD_ACTION -> firmware -> OK/ERR.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a mandar un comando y esperar la respuesta de confirmación.
    ///
    /// [SI NO EXISTIERA]
    /// La ruta de `HEAD_ACTION` no podría esperar OK/ERR de forma robusta.
    /// </summary>
    public async Task<AppScriptExecutionResult> ExecuteHeadActionAsync(
        string actionName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string cleanAction = (actionName ?? string.Empty).Trim().ToUpperInvariant();
        if (cleanAction.Length == 0) {
            return new AppScriptExecutionResult(false, "Accion HEAD_ACTION invalida.", null, 0);
        }

        if (!_connection.IsConnected) {
            return new AppScriptExecutionResult(false, "No hay conexion activa con el tester.", null, 0);
        }

        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            return await SendFirmwareHeadActionAsync(cleanAction, cancellationToken).ConfigureAwait(false);
        } finally {
            _executionGate.Release();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para ejecutar una binding que apunta a un script descargable.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ExecuteActionAsync` cuando el modo de ejecución es por scripts.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la acción tiene un TXT asociado en el perfil.
    ///
    /// [ENTRADAS]
    /// Recibe la binding y el token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de la ejecución.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Lista, descarga y envía línea por línea el contenido del script.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ExecuteBindingAsync -> download FILE_* -> envío de líneas -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar un script desde memoria externa y ejecutarlo paso a paso.
    ///
    /// [SI NO EXISTIERA]
    /// Los scripts asociados a acciones no tendrían motor de ejecución.
    /// </summary>
    public async Task<AppScriptExecutionResult> ExecuteBindingAsync(
        HeadButtonBinding binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_connection.IsConnected) {
            return new AppScriptExecutionResult(
                false,
                "No hay conexion activa con el tester.",
                binding.ScriptFileName,
                0);
        }

        string scriptFileName = Path.GetFileName((binding.ScriptFileName ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(scriptFileName)) {
            return new AppScriptExecutionResult(
                false,
                "La accion configurada no tiene script asociado.",
                null,
                0);
        }

        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            IReadOnlyList<string> availableFiles = await _commandFiles.ListFilesAsync(cancellationToken).ConfigureAwait(false);
            if (!availableFiles.Any(file => string.Equals(file, scriptFileName, StringComparison.OrdinalIgnoreCase))) {
                return new AppScriptExecutionResult(
                    false,
                    $"No se encontro el script asociado: {scriptFileName}.",
                    scriptFileName,
                    0);
            }

            CommandFileDownloadResult download = await _commandFiles
                .DownloadFileAsync(scriptFileName, cancellationToken)
                .ConfigureAwait(false);

            if (!download.Success) {
                string message = string.IsNullOrWhiteSpace(download.ErrorMessage)
                    ? $"No se pudo descargar el script asociado: {scriptFileName}."
                    : download.ErrorMessage!;
                return new AppScriptExecutionResult(false, message, scriptFileName, 0);
            }

            int sentCommands = 0;
            foreach (ScriptLine line in EnumerateScriptLines(download.Text)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryParseDelay(line.Content, out int delayMs)) {
                    if (delayMs > 0) {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    }
                    continue;
                }

                await _connection.SendLineAsync(line.Content, cancellationToken).ConfigureAwait(false);
                sentCommands++;
            }

            return new AppScriptExecutionResult(
                true,
                $"Script ejecutado: {scriptFileName}. Comandos enviados: {sentCommands}.",
                scriptFileName,
                sentCommands);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            string message = string.IsNullOrWhiteSpace(ex.Message)
                ? $"No se pudo ejecutar el script asociado: {scriptFileName}."
                : ex.Message;
            return new AppScriptExecutionResult(false, message, scriptFileName, 0);
        } finally {
            _executionGate.Release();
        }
    }

    private async Task<AppScriptExecutionResult> SendFirmwareHeadActionAsync(
        string cleanAction,
        CancellationToken cancellationToken)
    {
        string commandLine = $"HEAD_ACTION|{cleanAction}";
        TaskCompletionSource<AppScriptExecutionResult> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleLine(string line)
        {
            string cleanLine = line?.Trim() ?? string.Empty;
            if (cleanLine.Length == 0) {
                return;
            }

            if (string.Equals(cleanLine, $"OK|HEAD_ACTION|{cleanAction}", StringComparison.OrdinalIgnoreCase)) {
                waiter.TrySetResult(new AppScriptExecutionResult(
                    true,
                    $"Accion ejecutada por firmware: {cleanAction}.",
                    null,
                    1));
                return;
            }

            if (cleanLine.StartsWith("ERR|HEAD_ACTION", StringComparison.OrdinalIgnoreCase)) {
                waiter.TrySetResult(new AppScriptExecutionResult(
                    false,
                    cleanLine,
                    null,
                    0));
            }
        }

        _connection.LineReceived += HandleLine;
        try {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(HeadActionResponseTimeout);
            using CancellationTokenRegistration registration = timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<AppScriptExecutionResult>)state!).TrySetCanceled(),
                waiter);

            await _connection.SendLineAsync(commandLine, cancellationToken).ConfigureAwait(false);

            try {
                return await waiter.Task.ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                return new AppScriptExecutionResult(
                    false,
                    $"Timeout esperando OK/ERR de firmware para {commandLine}.",
                    null,
                    0);
            }
        } finally {
            _connection.LineReceived -= HandleLine;
        }
    }

    private static IEnumerable<ScriptLine> EnumerateScriptLines(string text)
    {
        using StringReader reader = new(text ?? string.Empty);
        string? rawLine;
        int lineNumber = 0;

        while ((rawLine = reader.ReadLine()) != null) {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0) {
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) {
                continue;
            }

            yield return new ScriptLine(lineNumber, line);
        }
    }

    private static bool TryParseDelay(string line, out int milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        string cleanLine = line.Trim();
        if (!(cleanLine.StartsWith("WAIT", StringComparison.OrdinalIgnoreCase)
            || cleanLine.StartsWith("DELAY", StringComparison.OrdinalIgnoreCase))) {
            return false;
        }

        int separatorIndex = cleanLine.IndexOf('|');
        string valueText;
        if (separatorIndex >= 0) {
            valueText = cleanLine[(separatorIndex + 1)..].Trim();
        } else {
            string[] parts = cleanLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) {
                return false;
            }

            valueText = parts[1];
        }

        if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds)) {
            return false;
        }

        milliseconds = Math.Max(0, milliseconds);
        return true;
    }

    private static string NormalizeToken(string value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string BuildSystemLabel(HeadSystemKind systemKind)
    {
        return systemKind == HeadSystemKind.Unified
            ? "Sistema Unificado"
            : "Sistema Modular";
    }

    private sealed record ScriptLine(int Number, string Content);
}

