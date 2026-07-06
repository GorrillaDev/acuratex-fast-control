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

public sealed class AcxProfileTransferWorkflowTests
{
    private const int FileDataChunkSize = 32;
    private const int TransportFileSizeLimitBytes = 65536;

    [Fact]
    public async Task BinaryUploadUsesExact32ByteBlocksAndSequentialIndices()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult export = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);

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
        int expectedBlocks = (export.PackageBytes.Length + FileDataChunkSize - 1) / FileDataChunkSize;
        Assert.Equal(expectedBlocks, result.TotalChunks);
        Assert.Equal($"ACK FILE_END {Path.GetFileName(export.FilePath!)}", result.InfoLine);

        IReadOnlyList<string> sentLines = controller.SentLines;
        Assert.Equal($"FILE_BEGIN|{Path.GetFileName(export.FilePath!)}|{export.PackageBytes.Length}", sentLines[0]);
        Assert.Equal($"FILE_END|{Path.GetFileName(export.FilePath!)}", sentLines[^1]);
        Assert.DoesNotContain(sentLines, line => line.StartsWith("FILE_SELECT|", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sentLines, line => line.StartsWith("FILE_INFO|", StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<string> fileDataLines = EnumerateFileDataLines(sentLines);
        Assert.Equal(expectedBlocks, fileDataLines.Count);
        for (int index = 0; index < fileDataLines.Count; index++) {
            string[] parts = fileDataLines[index].Split('|', 3, StringSplitOptions.None);
            Assert.Equal("FILE_DATA", parts[0]);
            Assert.Equal(index.ToString(), parts[1]);
            byte[] chunk = Convert.FromBase64String(parts[2]);
            int expectedChunkLength = index == fileDataLines.Count - 1
                ? export.PackageBytes.Length - (index * FileDataChunkSize)
                : FileDataChunkSize;
            Assert.Equal(expectedChunkLength, chunk.Length);
        }

        int remainder = export.PackageBytes.Length % FileDataChunkSize;
        if (remainder == 0) {
            remainder = FileDataChunkSize;
        }

        Assert.Equal(remainder, Convert.FromBase64String(fileDataLines[^1].Split('|', 3, StringSplitOptions.None)[2]).Length);
        Assert.Equal(expectedBlocks + 2, progressReports.Count);
        Assert.Equal(0, progressReports[0].SentBytes);
        Assert.Equal(export.PackageBytes.Length, progressReports[^1].SentBytes);
        Assert.Equal(export.PackageBytes.Length, progressReports[^1].TotalBytes);
        Assert.Equal(100, progressReports[^1].ProgressPercent);
        Assert.Equal(expectedBlocks, progressReports[^1].TotalBlocks);
        Assert.Equal(expectedBlocks, progressReports[^1].CurrentBlockIndex);
        Assert.Equal(export.PackageBytes, ReconstructBinaryPayload(sentLines));
    }

    [Fact]
    public async Task AcxTransferPublishesBinaryFileAndSkipsFileSelect()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult export = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);

        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => CreateBinaryUploadResponse(line, Path.GetFileName(export.FilePath!)),
        };

        using CommandFileTransferService commandTransfer = new(controller);
        AcxProfileTransferService transfer = new(
            new FixedAcxProfileExportService(export),
            new AcxProfilePackageReader(),
            commandTransfer);

        List<CommandFileUploadProgress> progressReports = new();
        AcxProfileTransferResult result = await transfer.TransferPublishedProfileAsync(
            selection,
            new ActionProgress<CommandFileUploadProgress>(progressReports.Add),
            CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.Success);
        Assert.Equal("ACX almacenado en el dispositivo. Todavía no está activado como perfil.", result.Message);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(Path.GetFileName(export.FilePath!), result.FileName);
        Assert.Equal(export.PackageBytes.Length, result.FileSizeBytes);
        Assert.Equal((export.PackageBytes.Length + FileDataChunkSize - 1) / FileDataChunkSize, result.TotalBlocks);
        Assert.Equal($"ACK FILE_END {Path.GetFileName(export.FilePath!)}", result.FirmwareAckLine);
        Assert.Equal($"FILE_BEGIN|{Path.GetFileName(export.FilePath!)}|{export.PackageBytes.Length}", controller.SentLines[0]);
        Assert.Equal($"FILE_END|{Path.GetFileName(export.FilePath!)}", controller.SentLines[^1]);
        Assert.DoesNotContain(controller.SentLines, line => line.StartsWith("FILE_SELECT|", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(controller.SentLines, line => line.StartsWith("FILE_INFO|", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(progressReports);
        Assert.Equal(100, progressReports[^1].ProgressPercent);
        Assert.Equal(result.FileSizeBytes, progressReports[^1].SentBytes);
        Assert.Equal(result.TotalBlocks, progressReports[^1].TotalBlocks);
    }

    [Fact]
    public async Task AcxTransferRejectsEmptyPackageBeforeBegin()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult template = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);
        AcxCompilationResult emptyExport = CreateMutatedExportResult(template, Array.Empty<byte>(), selection.FileName);

        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => throw new InvalidOperationException($"Unexpected line during empty export test: {line}"),
        };

        using CommandFileTransferService commandTransfer = new(controller);
        AcxProfileTransferService transfer = new(
            new FixedAcxProfileExportService(emptyExport),
            new AcxProfilePackageReader(),
            commandTransfer);

        AcxProfileTransferResult result = await transfer.TransferPublishedProfileAsync(selection).ConfigureAwait(false);

        Assert.False(result.Success);
        Assert.Equal("El archivo ACX generado esta vacio.", result.Message);
        Assert.Equal(result.Message, result.ErrorMessage);
        Assert.Equal(0, result.FileSizeBytes);
        Assert.Equal(0, result.TotalBlocks);
        Assert.Empty(controller.SentLines);
    }

    [Fact]
    public async Task AcxTransferRejectsOversizedPackageBeforeBegin()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult template = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);
        byte[] oversizedBytes = new byte[TransportFileSizeLimitBytes + 1];
        AcxCompilationResult oversizedExport = CreateMutatedExportResult(template, oversizedBytes, selection.FileName);

        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => throw new InvalidOperationException($"Unexpected line during oversized export test: {line}"),
        };

        using CommandFileTransferService commandTransfer = new(controller);
        AcxProfileTransferService transfer = new(
            new FixedAcxProfileExportService(oversizedExport),
            new AcxProfilePackageReader(),
            commandTransfer);

        AcxProfileTransferResult result = await transfer.TransferPublishedProfileAsync(selection).ConfigureAwait(false);

        Assert.False(result.Success);
        Assert.Contains($"{TransportFileSizeLimitBytes} bytes", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Message, result.ErrorMessage);
        Assert.Equal(oversizedBytes.Length, result.FileSizeBytes);
        Assert.Equal((oversizedBytes.Length + FileDataChunkSize - 1) / FileDataChunkSize, result.TotalBlocks);
        Assert.Empty(controller.SentLines);
    }

    [Fact]
    public async Task AcxTransferRejectsInvalidPackageBeforeBegin()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult template = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);
        byte[] corruptedBytes = (byte[])template.PackageBytes.Clone();
        corruptedBytes[0] = (byte)'B';
        AcxCompilationResult invalidExport = CreateMutatedExportResult(template, corruptedBytes, selection.FileName);

        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => throw new InvalidOperationException($"Unexpected line during invalid export test: {line}"),
        };

        using CommandFileTransferService commandTransfer = new(controller);
        AcxProfileTransferService transfer = new(
            new FixedAcxProfileExportService(invalidExport),
            new AcxProfilePackageReader(),
            commandTransfer);

        AcxProfileTransferResult result = await transfer.TransferPublishedProfileAsync(selection).ConfigureAwait(false);

        Assert.False(result.Success);
        Assert.Contains("Invalid ACX magic", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Message, result.ErrorMessage);
        Assert.Equal(0, result.FileSizeBytes);
        Assert.Equal(0, result.TotalBlocks);
        Assert.Empty(controller.SentLines);
    }

    [Fact]
    public async Task AcxTransferRejectsWhenFilePathAndPackageBytesDifferBeforeBegin()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult template = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);
        byte[] mismatchedBytes = (byte[])template.PackageBytes.Clone();
        mismatchedBytes[0] ^= 0xFF;

        AcxCompilationResult mismatchedExport = template with
        {
            PackageBytes = mismatchedBytes,
            PackageSizeBytes = mismatchedBytes.Length,
            PayloadCrc32 = 0,
        };

        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => throw new InvalidOperationException($"Unexpected line during mismatch test: {line}"),
        };

        using CommandFileTransferService commandTransfer = new(controller);
        AcxProfileTransferService transfer = new(
            new FixedAcxProfileExportService(mismatchedExport),
            new AcxProfilePackageReader(),
            commandTransfer);

        AcxProfileTransferResult result = await transfer.TransferPublishedProfileAsync(selection).ConfigureAwait(false);

        Assert.False(result.Success);
        Assert.Contains("no coinciden", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Message, result.ErrorMessage);
        Assert.Empty(controller.SentLines);
    }

    [Fact]
    public async Task AcxTransferCancelledBeforeBeginDoesNotSendCommands()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult export = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);

        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => throw new InvalidOperationException($"Unexpected line during cancellation test: {line}"),
        };

        using CommandFileTransferService commandTransfer = new(controller);
        AcxProfileTransferService transfer = new(
            new FixedAcxProfileExportService(export),
            new AcxProfilePackageReader(),
            commandTransfer);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        AcxProfileTransferResult result = await transfer.TransferPublishedProfileAsync(selection, null, cts.Token).ConfigureAwait(false);

        Assert.False(result.Success);
        Assert.Equal("Transferencia cancelada por el usuario.", result.Message);
        Assert.Equal(result.Message, result.ErrorMessage);
        Assert.Empty(controller.SentLines);
    }

    [Fact]
    public async Task AcxTransferCancelledBetweenBlocksSkipsFileEnd()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult export = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);

        ScriptedConnectionController controller = new();
        using CommandFileTransferService commandTransfer = new(controller);
        AcxProfileTransferService transfer = new(
            new FixedAcxProfileExportService(export),
            new AcxProfilePackageReader(),
            commandTransfer);

        using CancellationTokenSource cts = new();
        int fileDataResponses = 0;
        controller.BeforeResponseAsync = (line, token) =>
        {
            if (line.StartsWith("FILE_DATA|", StringComparison.OrdinalIgnoreCase)) {
                fileDataResponses++;
                if (fileDataResponses == 1) {
                    cts.Cancel();
                }
            }

            return Task.CompletedTask;
        };
        controller.ResponseFactory = line => CreateBinaryUploadResponse(line, Path.GetFileName(export.FilePath!));

        AcxProfileTransferResult result = await transfer.TransferPublishedProfileAsync(selection, null, cts.Token).ConfigureAwait(false);

        Assert.False(result.Success);
        Assert.Equal("Transferencia cancelada por el usuario.", result.Message);
        Assert.Equal(result.Message, result.ErrorMessage);
        Assert.Contains(controller.SentLines, line => line.StartsWith("FILE_BEGIN|", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controller.SentLines, line => line.StartsWith("FILE_DATA|0|", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(controller.SentLines, line => line.StartsWith("FILE_END|", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(controller.SentLines, line => line.StartsWith("FILE_DATA|1|", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, controller.SentLines.Count);
    }

    [Fact]
    public async Task AcxTransferIntermediateBlockFailureSkipsFileEnd()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxPublishedProfileSelection selection = LoadPublishedSelection(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult export = await exportService.ExportProfileVersionAsync(selection.Version.Id).ConfigureAwait(false);

        ScriptedConnectionController controller = new()
        {
            ResponseFactory = line => CreateBinaryUploadFailureResponse(line, Path.GetFileName(export.FilePath!)),
        };

        using CommandFileTransferService commandTransfer = new(controller);
        AcxProfileTransferService transfer = new(
            new FixedAcxProfileExportService(export),
            new AcxProfilePackageReader(),
            commandTransfer);

        AcxProfileTransferResult result = await transfer.TransferPublishedProfileAsync(selection).ConfigureAwait(false);

        Assert.False(result.Success);
        Assert.Equal(result.Message, result.ErrorMessage);
        Assert.Contains("FILE_B64", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(controller.SentLines, line => line.StartsWith("FILE_BEGIN|", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controller.SentLines, line => line.StartsWith("FILE_DATA|0|", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controller.SentLines, line => line.StartsWith("FILE_DATA|1|", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(controller.SentLines, line => line.StartsWith("FILE_END|", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, controller.SentLines.Count);
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

    private static AcxPublishedProfileSelection LoadPublishedSelection(ProfileTestHarness harness, string profileKey)
    {
        ProfileRecord profile = harness.Repository.GetProfileByKey(profileKey)
            ?? throw new InvalidOperationException($"Profile '{profileKey}' was not imported.");

        ProfileVersionSummary version = profile.Versions.FirstOrDefault(version => version.IsPublished)
            ?? throw new InvalidOperationException($"Profile '{profileKey}' does not have a published version.");

        return new AcxPublishedProfileSelection(profile, version);
    }

    private static AcxProfileTransferService CreateTransferService(AcxCompilationResult exportResult, ScriptedConnectionController controller)
    {
        return new AcxProfileTransferService(
            new FixedAcxProfileExportService(exportResult),
            new AcxProfilePackageReader(),
            new CommandFileTransferService(controller));
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

        throw new InvalidOperationException($"Unexpected line during failure test: {line}");
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