namespace AcuratexControlApp.Services;

public sealed record TesterWifiConfig(
    string Ssid,
    int Port,
    string Status,
    string Ip,
    string Reason);

public interface ITesterWifiConfigService
{
    Task<TesterWifiConfig> ReadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string ssid, string password, int port, CancellationToken cancellationToken = default);

    Task ConnectAsync(CancellationToken cancellationToken = default);
}