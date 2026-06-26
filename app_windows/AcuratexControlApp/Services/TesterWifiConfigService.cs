namespace AcuratexControlApp.Services;

public sealed class TesterWifiConfigService : ITesterWifiConfigService, IDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    private readonly IConnectionController _connection;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public TesterWifiConfigService(IConnectionController connection)
    {
        _connection = connection;
    }

    public async Task<TesterWifiConfig> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            string line = await SendCommandAndWaitAsync(
                "WIFI_CONFIG_GET",
                static clean => clean.StartsWith("WIFI_CONFIG|", StringComparison.OrdinalIgnoreCase),
                static clean => clean.StartsWith("ERR WIFI", StringComparison.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);

            if (line.StartsWith("ERR WIFI", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Tester respondio error: {line}");
            }

            return ParseConfig(line);
        } finally {
            _operationGate.Release();
        }
    }

    public async Task SaveAsync(string ssid, string password, int port, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        string cleanSsid = ValidateTextField(ssid, "SSID", 32);
        string cleanPassword = ValidateOptionalTextField(password, "password", 63);
        if (port < 1 || port > 65535) {
            throw new InvalidOperationException("Puerto TCP invalido.");
        }

        string command = $"WIFI_CONFIG_SET|SSID={cleanSsid}|PORT={port}";
        if (!string.IsNullOrWhiteSpace(cleanPassword)) {
            command += $"|PASS={cleanPassword}";
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            string line = await SendCommandAndWaitAsync(
                command,
                static clean => string.Equals(clean, "ACK WIFI_CONFIG_SAVE", StringComparison.OrdinalIgnoreCase),
                static clean => clean.StartsWith("ERR WIFI", StringComparison.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);

            if (line.StartsWith("ERR WIFI", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Tester respondio error: {line}");
            }
        } finally {
            _operationGate.Release();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            string line = await SendCommandAndWaitAsync(
                "WIFI_CONNECT",
                static clean => string.Equals(clean, "ACK WIFI_CONNECT", StringComparison.OrdinalIgnoreCase),
                static clean => clean.StartsWith("ERR WIFI", StringComparison.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);

            if (line.StartsWith("ERR WIFI", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Tester respondio error: {line}");
            }
        } finally {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _operationGate.Dispose();
    }

    private async Task<string> SendCommandAndWaitAsync(
        string commandLine,
        Func<string, bool> successMatcher,
        Func<string, bool> errorMatcher,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<string> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleLine(string line)
        {
            string clean = line?.Trim() ?? string.Empty;
            if (clean.Length == 0 || string.Equals(clean, "QUEUED", StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            if (successMatcher(clean) || errorMatcher(clean)) {
                waiter.TrySetResult(clean);
            }
        }

        _connection.LineReceived += HandleLine;
        try {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResponseTimeout);
            using CancellationTokenRegistration registration = timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<string>)state!).TrySetCanceled(),
                waiter);

            await _connection.SendLineAsync(commandLine, cancellationToken).ConfigureAwait(false);

            try {
                return await waiter.Task.ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException($"Timeout esperando respuesta de: {RedactCommandForError(commandLine)}");
            }
        } finally {
            _connection.LineReceived -= HandleLine;
        }
    }

    private static string RedactCommandForError(string commandLine)
    {
        return commandLine.StartsWith("WIFI_CONFIG_SET", StringComparison.OrdinalIgnoreCase)
            ? "WIFI_CONFIG_SET|PASS=<redacted>"
            : commandLine;
    }

    private static TesterWifiConfig ParseConfig(string line)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string part in line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1)) {
            int eq = part.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            values[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }

        int port = values.TryGetValue("PORT", out string? portText) && int.TryParse(portText, out int parsedPort)
            ? parsedPort
            : 3333;

        return new TesterWifiConfig(
            values.TryGetValue("SSID", out string? ssid) ? ssid : string.Empty,
            port,
            values.TryGetValue("STATUS", out string? status) ? status : "desconocido",
            values.TryGetValue("IP", out string? ip) ? ip : "0.0.0.0",
            values.TryGetValue("REASON", out string? reason) ? reason : string.Empty);
    }

    private static string ValidateTextField(string value, string fieldName, int maxLength)
    {
        string clean = (value ?? string.Empty).Trim();
        if (clean.Length == 0) {
            throw new InvalidOperationException($"{fieldName} requerido.");
        }

        return ValidateOptionalTextField(clean, fieldName, maxLength);
    }

    private static string ValidateOptionalTextField(string value, string fieldName, int maxLength)
    {
        string clean = (value ?? string.Empty).Trim();
        if (clean.Contains('|') || clean.Contains('\r') || clean.Contains('\n')) {
            throw new InvalidOperationException($"{fieldName} contiene caracteres no permitidos.");
        }

        if (clean.Length > maxLength) {
            throw new InvalidOperationException($"{fieldName} demasiado largo.");
        }

        return clean;
    }

    private void EnsureConnected()
    {
        if (!_connection.IsConnected) {
            throw new InvalidOperationException("No hay conexion activa.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(TesterWifiConfigService));
        }
    }
}
