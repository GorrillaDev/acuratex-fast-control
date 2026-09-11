namespace AcuratexControlApp.Services;

public sealed class ServoDashboardUnificadoCommandService : IServoDashboardUnificadoCommandService
{
    private readonly IConnectionController _connection;

    public ServoDashboardUnificadoCommandService(IConnectionController connection)
    {
        _connection = connection;
    }

    public Task StartInitAsync(CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.Init(), cancellationToken);

    public Task RequestStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.Status(), cancellationToken);

    public Task StartRackingSequenceAsync(
        int p1,
        int p2,
        int p3,
        string sequence,
        decimal frequencyHz,
        CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.RackingConfig(p1, p2, p3, sequence, frequencyHz), cancellationToken);

    public Task StopRackingSequenceAsync(CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.RackingStop(), cancellationToken);

    public Task SetMainMotorDirectionAsync(bool right, CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.MainDirection(right), cancellationToken);

    public Task SetMainMotorSpeedAsync(int level, CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.MainSpeed(level), cancellationToken);

    public Task RunMainMotorAsync(CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.MainRun(), cancellationToken);

    public Task StopMainMotorAsync(CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.MainStop(), cancellationToken);

    public Task SetRollerDirectionAsync(int direction, CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.RollerDirection(direction), cancellationToken);

    public Task SetRollerFrequencyAsync(int frequencyHz, CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.RollerFrequency(frequencyHz), cancellationToken);

    public Task SetRollerRunningAsync(bool running, CancellationToken cancellationToken = default) =>
        SendAsync(UnifiedMotorCommandProtocol.RollerRun(running), cancellationToken);

    private Task SendAsync(string line, CancellationToken cancellationToken)
    {
        if (!_connection.IsConnected) throw new InvalidOperationException("No hay conexion activa con el tester.");
        return _connection.SendLineAsync(line, cancellationToken);
    }
}
