namespace AcuratexControlApp.Services;

/// <summary>
/// Contrato del dashboard de motores del Sistema Unificado. Los dos nombres
/// "Servo CAN" identifican motores; ambos salen fisicamente por U3/CAN2.
/// </summary>
public interface IServoDashboardUnificadoCommandService
{
    Task StartInitAsync(CancellationToken cancellationToken = default);
    Task RequestStatusAsync(CancellationToken cancellationToken = default);
    Task StartRackingSequenceAsync(
        int p1,
        int p2,
        int p3,
        string sequence,
        decimal frequencyHz,
        CancellationToken cancellationToken = default);
    Task StopRackingSequenceAsync(CancellationToken cancellationToken = default);
    Task SetMainMotorDirectionAsync(bool right, CancellationToken cancellationToken = default);
    Task SetMainMotorSpeedAsync(int level, CancellationToken cancellationToken = default);
    Task RunMainMotorAsync(CancellationToken cancellationToken = default);
    Task StopMainMotorAsync(CancellationToken cancellationToken = default);
    Task SetRollerDirectionAsync(int direction, CancellationToken cancellationToken = default);
    Task SetRollerFrequencyAsync(int frequencyHz, CancellationToken cancellationToken = default);
    Task SetRollerRunningAsync(bool running, CancellationToken cancellationToken = default);
}
