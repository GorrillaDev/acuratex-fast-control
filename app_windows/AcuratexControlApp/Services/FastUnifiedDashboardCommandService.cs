using AcuratexControlApp;

namespace AcuratexControlApp.Services;

/// <summary>
/// Route dedicated to the unified head dashboard. It writes the fast firmware
/// protocol directly to the active connection and intentionally has no profile,
/// script, database, file-transfer or storage dependencies.
/// </summary>
public sealed class FastUnifiedDashboardCommandService : ICabezalDashboardUnificadoCommandService
{
    private readonly IConnectionController _connection;

    public FastUnifiedDashboardCommandService(IConnectionController connection)
    {
        _connection = connection;
    }

    public Task SendCanLineAsync(string line, CancellationToken cancellationToken = default) =>
        SendRawLineAsync(line, cancellationToken);

    public Task SendDoCommandAsync(string command, CancellationToken cancellationToken = default) =>
        SendUnifiedLineAsync(command, cancellationToken);

    public Task SendDenPositionAsync(
        int motorIndex,
        int position,
        int selectedPositionNumber = 0,
        CancellationToken cancellationToken = default)
    {
        string line = selectedPositionNumber > 0
            ? $"den_select_{motorIndex + 1}|{selectedPositionNumber}"
            : $"den_pos_{motorIndex + 1}|{position}";
        return SendUnifiedLineAsync(line, cancellationToken);
    }

    public Task SendSicPositionAsync(
        int sicIndex,
        int position,
        int selectedPositionNumber = 0,
        CancellationToken cancellationToken = default)
    {
        string line = selectedPositionNumber > 0
            ? $"sic_select_{sicIndex + 1}|{selectedPositionNumber}"
            : $"sic_pos_{sicIndex + 1}|{position}";
        return SendUnifiedLineAsync(line, cancellationToken);
    }

    public Task SendJRegisterAsync(int jIndex, byte value, CancellationToken cancellationToken = default) =>
        SendUnifiedLineAsync($"j_set_{jIndex}|{value}", cancellationToken);

    public Task SendJAllAsync(int jIndex, bool on, CancellationToken cancellationToken = default) =>
        SendJRegisterAsync(jIndex, on ? (byte)0x00 : (byte)0xFF, cancellationToken);

    public Task SendJChannelAsync(int jIndex, int channelIndex, CancellationToken cancellationToken = default) =>
        SendUnifiedLineAsync($"j_ch_{jIndex}_{channelIndex}", cancellationToken);

    public Task SendBlockPinAsync(string blockKey, int pinIndex, bool on, CancellationToken cancellationToken = default)
    {
        string cleanKey = (blockKey ?? string.Empty).Trim().ToLowerInvariant();
        string module;
        string suffix;

        if (cleanKey.StartsWith("yarn", StringComparison.Ordinal))
        {
            module = "yarn";
            suffix = cleanKey[4..];
        }
        else if (cleanKey.StartsWith("stitch", StringComparison.Ordinal))
        {
            module = "stitch";
            suffix = cleanKey[6..];
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(blockKey));
        }

        if (!int.TryParse(suffix, out int instance) || instance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockKey));
        }

        return SendUnifiedLineAsync($"{module}_pin_{instance}|{pinIndex}|{(on ? 1 : 0)}", cancellationToken);
    }

    private Task SendUnifiedLineAsync(string line, CancellationToken cancellationToken) =>
        SendRawLineAsync(ToUnifiedCommand(line), cancellationToken);

    private static string ToUnifiedCommand(string command)
    {
        string cleanCommand = (command ?? string.Empty).Trim();
        return cleanCommand.StartsWith("uni_", StringComparison.OrdinalIgnoreCase)
            ? cleanCommand
            : $"uni_{cleanCommand}";
    }

    private Task SendRawLineAsync(string line, CancellationToken cancellationToken)
    {
        string cleanLine = (line ?? string.Empty).Trim();
        if (cleanLine.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (!_connection.IsConnected)
        {
            throw new InvalidOperationException("No hay conexion activa con el tester.");
        }

        return _connection.SendLineAsync(cleanLine, cancellationToken);
    }
}
