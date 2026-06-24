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
        _ = selectedPositionNumber;
        string line = $"den_pos_{motorIndex + 1}|{position}";
        return SendLineAsync(line, cancellationToken);
    }

    public Task SendSicPositionAsync(
        int sicIndex,
        int position,
        int selectedPositionNumber = 0,
        CancellationToken cancellationToken = default)
    {
        _ = selectedPositionNumber;
        string line = $"sic_pos_{sicIndex + 1}|{position}";
        return SendLineAsync(line, cancellationToken);
    }

    public Task SendJRegisterAsync(int jIndex, byte value, CancellationToken cancellationToken = default)
    {
        string line = CabezalDashboardTarjetasProtocol.FormatJRegisterLine(jIndex, value);
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
        string line = CabezalDashboardTarjetasProtocol.FormatBlockPinLine(blockKey, pinIndex, on);
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
