using System.Globalization;

namespace AcuratexControlApp.Services;

/// <summary>Protocol adapter used only by Sistema Modular / Dashboard Servo.</summary>
public sealed class ServoDashboardTarjetasCommandService : IServoDashboardTarjetasCommandService
{
    private readonly IConnectionController _connection;
    private bool _lineDriveSelected = true;

    public ServoDashboardTarjetasCommandService(IConnectionController connection) => _connection = connection;

    public async Task SetModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        bool nextLineDrive = string.Equals(mode, "LINE DRIVE", StringComparison.OrdinalIgnoreCase);
        if (_lineDriveSelected && !nextLineDrive) {
            await SendAsync("LD_SAFE_STOP", cancellationToken).ConfigureAwait(false);
        }
        _lineDriveSelected = nextLineDrive;
    }

    public Task SetInitAsync(bool enabled, CancellationToken cancellationToken = default) =>
        _lineDriveSelected || !enabled ? Task.CompletedTask : SendAsync("init", cancellationToken);

    public Task SetOutputAsync(string key, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!_lineDriveSelected) return Task.CompletedTask;
        string line = key switch {
            "s1-son" => $"LD_S2_SON|{OnOff(enabled)}",
            "s1-run" => $"LD_S2_RUN|{OnOff(enabled)}",
            "s1-dir" => $"LD_S2_DIR|{Bit(enabled)}",
            "step-run" => $"LD_STEP_RUN|{OnOff(enabled)}",
            "step-dir" => $"LD_STEP_DIR|{Bit(enabled)}",
            "step-rev" when enabled => "LD_STEP_REV",
            "step-rev" => string.Empty,
            "s2-son" => $"LD_S1_SON|{OnOff(enabled)}",
            // The large Servo 1 card keeps its existing tiles; motion is position controlled.
            "s2-run" when !enabled => "LD_S1_ROUTINE|OFF",
            "s2-run" => string.Empty,
            "s2-dir" => string.Empty,
            _ => string.Empty
        };
        return SendAsync(line, cancellationToken);
    }

    public Task SetFrequencyAsync(string key, int frequencyHz, CancellationToken cancellationToken = default)
    {
        if (!_lineDriveSelected) return Task.CompletedTask;
        string line = key switch {
            "servo2" => $"LD_S2_FREQ|HZ={Math.Clamp(frequencyHz, 1, 160000)}",
            "stepper" => $"LD_STEP_FREQ|HZ={Math.Clamp(frequencyHz, 1, 50000)}",
            _ => string.Empty
        };
        return SendAsync(line, cancellationToken);
    }

    public Task ConfigurePositionAsync(int positionNumber, decimal target, int turns, CancellationToken cancellationToken = default) =>
        !_lineDriveSelected ? Task.CompletedTask : SendAsync($"LD_S1_POS_CONFIG|POS={positionNumber}|TARGET={Format(target)}|TURNS={turns}", cancellationToken);

    public Task GoToPositionAsync(int positionNumber, decimal target, int turns, CancellationToken cancellationToken = default) =>
        !_lineDriveSelected ? Task.CompletedTask : SendAsync($"LD_S1_GOTO|POS={positionNumber}", cancellationToken);

    public Task SetTurnsAsync(int positionNumber, int turns, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetRoutineAsync(bool enabled, IReadOnlyList<int> orderedPositions, int speedLevel, CancellationToken cancellationToken = default)
    {
        if (!_lineDriveSelected) return Task.CompletedTask;
        if (!enabled) return SendAsync("LD_S1_ROUTINE|OFF", cancellationToken);
        int dwellMs = speedLevel switch { 1 => 1000, 2 => 750, 3 => 500, 4 => 250, _ => 1000 };
        string order = string.Join(',', orderedPositions.Where(static p => p is >= 1 and <= 3));
        return SendAsync($"LD_S1_ROUTINE|ON|ORDER={order}|DWELL_MS={dwellMs}", cancellationToken);
    }

    private Task SendAsync(string line, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(line) || !_connection.IsConnected ? Task.CompletedTask : _connection.SendLineAsync(line, cancellationToken);
    private static string OnOff(bool value) => value ? "ON" : "OFF";
    private static int Bit(bool value) => value ? 1 : 0;
    private static string Format(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
