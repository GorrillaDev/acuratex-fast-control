using AcuratexControlApp;
using AcuratexControlApp.Components;

namespace AcuratexControlApp.Services;

public sealed class FastDashboardCommandService : ICabezalDashboardTarjetasCommandService
{
    private readonly IConnectionController _connection;

    public FastDashboardCommandService(IConnectionController connection)
    {
        _connection = connection;
    }

    public Task SendCanLineAsync(string line, CancellationToken cancellationToken = default)
    {
        return SendLineAsync(line, cancellationToken);
    }

    public Task SendDoCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        return SendLineAsync(command, cancellationToken);
    }

    public Task SendDenPositionAsync(
        int motorIndex,
        int position,
        int selectedPositionNumber = 0,
        CancellationToken cancellationToken = default)
    {
        string line = selectedPositionNumber > 0
            ? $"den_select_{motorIndex + 1}|{selectedPositionNumber}"
            : $"den_pos_{motorIndex + 1}|{position}";
        return SendLineAsync(line, cancellationToken);
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
        return SendLineAsync(line, cancellationToken);
    }

    public Task SendJRegisterAsync(int jIndex, byte value, CancellationToken cancellationToken = default)
    {
        string line = $"j_set_{jIndex}|{value}";
        return SendLineAsync(line, cancellationToken);
    }

    public Task SendJAllAsync(int jIndex, bool on, CancellationToken cancellationToken = default)
    {
        return SendJRegisterAsync(jIndex, on ? (byte)0x00 : (byte)0xFF, cancellationToken);
    }

    public Task SendJChannelAsync(int jIndex, int channelIndex, CancellationToken cancellationToken = default)
    {
        string line = $"j_ch_{jIndex}_{channelIndex}";
        return SendLineAsync(line, cancellationToken);
    }

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

        string line = $"{module}_pin_{instance}|{pinIndex}|{(on ? 1 : 0)}";
        return SendLineAsync(line, cancellationToken);
    }

    private Task SendLineAsync(string line, CancellationToken cancellationToken)
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
