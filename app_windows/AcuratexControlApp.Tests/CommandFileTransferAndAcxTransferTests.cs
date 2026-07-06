using System.Text;
using AcuratexControlApp;
using AcuratexControlApp.Data.Sqlite;
using AcuratexControlApp.Data.Sqlite.Importers;
using AcuratexControlApp.Models.Profiles;
using AcuratexControlApp.Repositories.Profiles;
using AcuratexControlApp.Services;
using AcuratexControlApp.Services.Profiles;
using AcuratexControlApp.Services.Profiles.Acx;
using Xunit;

namespace AcuratexControlApp.Tests;

public sealed class CommandFileTransferAndAcxTransferTests
{
    private const int FileDataChunkSize = 32;
    private const int TransportFileSizeLimitBytes = 65536;

    [Fact]
    public async Task BinaryUploadUsesExpectedSequenceAndReconstructsBytes()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult export = await exportService.ExportProfileVersionAsync(original.Version.Id).ConfigureAwait(false);

        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => CreateBinaryUploadResponse(line, Path.GetFileName(export.FilePath!)),
        };

        using CommandFileTransferService transfer = new(controller);
        List<CommandFileUploadProgress> progressReports = new();

        CommandFileUploadResult result = await transfer.UploadBinaryFileAsync(
            Path.GetFileName(export.FilePath!),
            export.PackageBytes,
            new ActionProgress<CommandFileUploadProgress>(progressReports.Add),
            CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.Success);
        Assert.Equal(Path.GetFileName(export.FilePath!), result.FileName);
        Assert.Equal(export.PackageBytes.Length, result.FileSizeBytes);
        Assert.Equal((export.PackageBytes.Length + FileDataChunkSize - 1) / FileDataChunkSize, result.TotalChunks);
        Assert.Equal($"ACK FILE_END {Path.GetFileName(export.FilePath!)}", result.InfoLine);

        IReadOnlyList<string> sentLines = controller.SentLines;
        Assert.NotEmpty(sentLines);
        Assert.DoesNotContain(sentLines, line => line.StartsWith("FILE_SELECT|", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sentLines, line => line.StartsWith("FILE_INFO|", StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"FILE_BEGIN|{Path.GetFileName(export.FilePath!)}|{export.PackageBytes.Length}", sentLines[0]);
        Assert.Equal($"FILE_END|{Path.GetFileName(export.FilePath!)}", sentLines[^1]);

        int expectedBlocks = (export.PackageBytes.Length + FileDataChunkSize - 1) / FileDataChunkSize;
        Assert.Equal(expectedBlocks + 2, progressReports.Count);
        Assert.Equal(0, progressReports[0].SentBytes);
        Assert.Equal(export.PackageBytes.Length, progressReports[^1].SentBytes);
        Assert.Equal(export.PackageBytes.Length, progressReports[^1].TotalBytes);
        Assert.Equal(100, progressReports[^1].ProgressPercent);
        Assert.Equal(expectedBlocks, progressReports[^1].TotalBlocks);
        Assert.Equal(expectedBlocks, progressReports[^1].CurrentBlockIndex);

        byte[] reconstructed = ReconstructBinaryPayload(sentLines);
        Assert.Equal(export.PackageBytes, reconstructed);
        Assert.All(EnumerateFileDataLines(sentLines), line => AssertFileDataLineHasChunkSize(line, FileDataChunkSize));
        Assert.Equal(expectedBlocks, EnumerateFileDataLines(sentLines).Count);
    }

    [Fact]
    public async Task TextUploadRetainsLegacySelectFlow()
    {
        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => CreateTextUploadResponse(line),
        };

        using CommandFileTransferService transfer = new(controller);
        byte[] bytes = Encoding.UTF8.GetBytes("hola");

        CommandFileUploadResult result = await transfer.UploadTextFileAsync("test.txt", bytes, null, CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.Success);
        Assert.Equal("test.txt", result.FileName);
        Assert.Equal(bytes.Length, result.FileSizeBytes);
        Assert.Equal("FILE_INFO|test.txt|4|selected=1", result.InfoLine);
        Assert.Contains("FILE_SELECT|test.txt", controller.SentLines);
        Assert.Contains("FILE_INFO|test.txt", controller.SentLines);
        Assert.True(controller.SentLines.IndexOf("FILE_END|test.txt") < controller.SentLines.IndexOf("FILE_SELECT|test.txt"));
        Assert.True(controller.SentLines.IndexOf("FILE_SELECT|test.txt") < controller.SentLines.IndexOf("FILE_INFO|test.txt"));
    }

    [Theory]
    [InlineData("ERR FILE_BUSY")]
    [InlineData("ERR FILE_PROTECTED")]
    public async Task BinaryUploadFailsImmediatelyOnBeginErrors(string firmwareError)
    {
        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => line.StartsWith("FILE_BEGIN|", StringComparison.OrdinalIgnoreCase)
                ? firmwareError
                : throw new InvalidOperationException($"Unexpected line sent during error test: {line}"),
        };

        using CommandFileTransferService transfer = new(controller);
        byte[] bytes = Encoding.UTF8.GetBytes("acx-data");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transfer.UploadBinaryFileAsync("test.acx", bytes, null, CancellationToken.None)).ConfigureAwait(false);

        Assert.Contains(firmwareError, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(controller.SentLines);
        Assert.StartsWith("FILE_BEGIN|test.acx|", controller.SentLines[0], StringComparison.OrdinalIgnoreCase);
    }

    private static AcxProfileExportService CreateExportService(ProfileTestHarness harness, IAcxProfileCompiler? compiler = null, AcxProfilePackageReader? reader = null)
    {
        return new AcxProfileExportService(
            harness.Repository,
            compiler ?? new AcxProfileCompiler(harness.Validator),
            reader ?? new AcxProfilePackageReader(),
            harness.Validator,
            harness.ConnectionFactory,
            harness.Options);
    }

    private static ProfileVersionDocument LoadSeedVersion(ProfileTestHarness harness, string profileKey)
    {
        ProfileRecord profile = harness.Repository.GetProfileByKey(profileKey)
            ?? throw new InvalidOperationException($"Profile '{profileKey}' was not imported.");

        Assert.NotEmpty(profile.Versions);
        return harness.Repository.ReadVersion(profile.Versions[0].Id);
    }

    private static AcxCompilationResult CreateMutatedExportResult(AcxCompilationResult template, byte[] bytes, string fileName)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "AcuratexFastControl.AcxTransferTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        string path = Path.Combine(tempDirectory, fileName);
        File.WriteAllBytes(path, bytes);

        return template with
        {
            FilePath = path,
            PackageBytes = bytes,
            PackageSizeBytes = bytes.Length,
            PayloadCrc32 = 0,
        };
    }

    private static byte[] ReconstructBinaryPayload(IReadOnlyList<string> sentLines)
    {
        List<byte> bytes = new();
        foreach (string line in EnumerateFileDataLines(sentLines)) {
            string[] parts = line.Split('|', 3, StringSplitOptions.None);
            bytes.AddRange(Convert.FromBase64String(parts[2]));
        }

        return bytes.ToArray();
    }

    private static IReadOnlyList<string> EnumerateFileDataLines(IReadOnlyList<string> sentLines)
    {
        return sentLines.Where(line => line.StartsWith("FILE_DATA|", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static void AssertFileDataLineHasChunkSize(string line, int expectedChunkSize)
    {
        string[] parts = line.Split('|', 3, StringSplitOptions.None);
        Assert.Equal(3, parts.Length);
        Assert.Equal("FILE_DATA", parts[0]);
        Assert.True(int.TryParse(parts[1], out int index));
        Assert.True(index >= 0);
        byte[] chunk = Convert.FromBase64String(parts[2]);
        Assert.True(chunk.Length <= expectedChunkSize);
        Assert.True(chunk.Length > 0);
    }

    private static string? CreateBinaryUploadResponse(string line, string fileName)
    {
        if (line.StartsWith($"FILE_BEGIN|{fileName}|", StringComparison.OrdinalIgnoreCase)) {
            return $"ACK FILE_BEGIN {fileName}";
        }

        if (line.StartsWith("FILE_DATA|", StringComparison.OrdinalIgnoreCase)) {
            string[] parts = line.Split('|', 3, StringSplitOptions.None);
            return $"ACK FILE_DATA {parts[1]}";
        }

        if (line.Equals($"FILE_END|{fileName}", StringComparison.OrdinalIgnoreCase)) {
            return $"ACK FILE_END {fileName}";
        }

        return null;
    }

    private static string? CreateTextUploadResponse(string line)
    {
        if (line.StartsWith("FILE_BEGIN|test.txt|", StringComparison.OrdinalIgnoreCase)) {
            return "ACK FILE_BEGIN test.txt";
        }

        if (line.StartsWith("FILE_DATA|", StringComparison.OrdinalIgnoreCase)) {
            string[] parts = line.Split('|', 3, StringSplitOptions.None);
            return $"ACK FILE_DATA {parts[1]}";
        }

        if (line.Equals("FILE_END|test.txt", StringComparison.OrdinalIgnoreCase)) {
            return "ACK FILE_END test.txt";
        }

        if (line.Equals("FILE_SELECT|test.txt", StringComparison.OrdinalIgnoreCase)) {
            return "ACK FILE_SELECT test.txt";
        }

        if (line.Equals("FILE_INFO|test.txt", StringComparison.OrdinalIgnoreCase)) {
            return "FILE_INFO|test.txt|4|selected=1";
        }

        return null;
    }

    private static string? CreateBinaryUploadFailureResponse(string line, string fileName)
    {
        if (line.StartsWith($"FILE_BEGIN|{fileName}|", StringComparison.OrdinalIgnoreCase)) {
            return $"ACK FILE_BEGIN {fileName}";
        }

        if (line.StartsWith("FILE_DATA|0|", StringComparison.OrdinalIgnoreCase)) {
            string[] parts = line.Split('|', 3, StringSplitOptions.None);
            return $"ACK FILE_DATA {parts[1]}";
        }

        if (line.StartsWith("FILE_DATA|1|", StringComparison.OrdinalIgnoreCase)) {
            return "ERR FILE_B64|1|invalid";
        }

        throw new InvalidOperationException($"Unexpected line sent during failure test: {line}");
    }

    private sealed class ScriptedConnectionController : IConnectionController
    {
        public List<string> SentLines { get; } = new();

        public Func<string, CancellationToken, Task>? BeforeResponseAsync { get; set; }

        public Func<string, string?>? ResponseFactory { get; set; }

        public bool IsConnected => true;

        public event Action<string>? LineReceived;
        public event Action<string>? LineSent;
        public event Action? ConnectionLost;

        public Task ConnectAsync(ConnectionMode mode, UsbVendorDeviceInfo? device, string host, int tcpPort, string serialPort, int baudRate, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            return Task.CompletedTask;
        }

        public async Task SendLineAsync(string line, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SentLines.Add(line);
            LineSent?.Invoke(line);

            if (BeforeResponseAsync is not null) {
                await BeforeResponseAsync(line, cancellationToken).ConfigureAwait(false);
            }

            if (ResponseFactory is null) {
                return;
            }

            string? response = ResponseFactory(line);
            if (!string.IsNullOrWhiteSpace(response)) {
                LineReceived?.Invoke(response);
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixedAcxProfileExportService : IAcxProfileExportService
    {
        private readonly AcxCompilationResult _result;

        public FixedAcxProfileExportService(AcxCompilationResult result)
        {
            _result = result;
        }

        public Task<AcxCompilationResult> ExportProfileVersionAsync(long profileVersionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }

        public Task<AcxCompilationResult> ExportLatestProfileAsync(string profileKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class ActionProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public ActionProgress(Action<T> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }

    private sealed class ProfileTestHarness : IDisposable
    {
        public ProfileTestHarness()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "AcuratexFastControl.AcxTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);

            Options = ProfileDatabaseOptions.ForRoot(RootDirectory);
            ConnectionFactory = new SqliteConnectionFactory(Options);
            Validator = new ProfileValidator();
            Repository = new SqliteProfileRepository(ConnectionFactory, Validator);
            ImportService = new HeadProfileImportService(Repository, Validator);
            Initializer = new ProfileDatabaseInitializer(ConnectionFactory, ImportService);
        }

        public string RootDirectory { get; }
        public ProfileDatabaseOptions Options { get; }
        public SqliteConnectionFactory ConnectionFactory { get; }
        public ProfileValidator Validator { get; }
        public IProfileRepository Repository { get; }
        public HeadProfileImportService ImportService { get; }
        public ProfileDatabaseInitializer Initializer { get; }

        public void Dispose()
        {
            try {
                if (Directory.Exists(RootDirectory)) {
                    Directory.Delete(RootDirectory, recursive: true);
                }
            } catch {
                // Transient file locks can appear briefly after SQLite closes.
            }
        }
    }
}


