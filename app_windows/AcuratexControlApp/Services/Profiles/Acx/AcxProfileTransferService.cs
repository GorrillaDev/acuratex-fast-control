using System;
using System.IO;
using AcuratexControlApp.Services;

namespace AcuratexControlApp.Services.Profiles.Acx;

public sealed class AcxProfileTransferService : IAcxProfileTransferService
{
    private const int TransportFileSizeLimitBytes = 65536;
    private const int TransportChunkSizeBytes = 32;

    private readonly IAcxProfileExportService _exportService;
    private readonly AcxProfilePackageReader _reader;
    private readonly ICommandFileTransferService _commandFileTransferService;

    public AcxProfileTransferService(
        IAcxProfileExportService exportService,
        AcxProfilePackageReader reader,
        ICommandFileTransferService commandFileTransferService)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _commandFileTransferService = commandFileTransferService ?? throw new ArgumentNullException(nameof(commandFileTransferService));
    }

    public async Task<AcxProfileTransferResult> TransferPublishedProfileAsync(
        AcxPublishedProfileSelection selection,
        IProgress<CommandFileUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);

        try {
            ValidateSelectionOrThrow(selection);
            ReportProgress(progress, $"Validando perfil publicado {selection.Profile.ProfileKey} v{selection.Version.VersionNumber}...", 0, 0, 0, 0);

            AcxCompilationResult exportResult = await _exportService
                .ExportProfileVersionAsync(selection.Version.Id, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            string filePath = exportResult.FilePath ?? string.Empty;
            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName)) {
                return CreateFailure(selection, selection.FileName, exportResult.PackageBytes.Length, 0, exportResult.PayloadCrc32, "El exportador ACX no devolvio una ruta de archivo valida.");
            }

            if (!string.Equals(fileName, selection.FileName, StringComparison.OrdinalIgnoreCase)) {
                return CreateFailure(selection, fileName, exportResult.PackageBytes.Length, CalculateBlockCount(exportResult.PackageBytes.Length), exportResult.PayloadCrc32, $"El nombre exportado '{fileName}' no coincide con el nombre esperado '{selection.FileName}'.");
            }

            byte[] fileBytes;
            try {
                fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) {
                return CreateFailure(selection, fileName, 0, 0, exportResult.PayloadCrc32, BuildErrorMessage(ex, "No se pudo leer el archivo ACX exportado."));
            }

            int fileSizeBytes = fileBytes.Length;
            int totalBlocks = CalculateBlockCount(fileSizeBytes);

            if (fileSizeBytes == 0) {
                return CreateFailure(selection, fileName, fileSizeBytes, totalBlocks, exportResult.PayloadCrc32, "El archivo ACX generado esta vacio.");
            }

            if (fileSizeBytes > TransportFileSizeLimitBytes) {
                return CreateFailure(selection, fileName, fileSizeBytes, totalBlocks, exportResult.PayloadCrc32, $"El archivo ACX supera {TransportFileSizeLimitBytes} bytes y no puede enviarse con el FILE_* actual.");
            }

            if (!fileBytes.SequenceEqual(exportResult.PackageBytes)) {
                return CreateFailure(selection, fileName, fileSizeBytes, totalBlocks, exportResult.PayloadCrc32, "El archivo exportado y el paquete en memoria no coinciden.");
            }

            AcxProfilePackage validatedPackage = _reader.Read(filePath);
            if (validatedPackage.Header.FileSize != (uint)fileSizeBytes) {
                return CreateFailure(selection, fileName, fileSizeBytes, totalBlocks, exportResult.PayloadCrc32, "El archivo ACX validado no coincide con el tamano compilado.");
            }

            ReportProgress(progress, $"ACX listo para transferir: {fileName}", 0, fileSizeBytes, 0, totalBlocks);

            CommandFileUploadResult uploadResult;
            try {
                uploadResult = await _commandFileTransferService.UploadBinaryFileAsync(
                    fileName,
                    fileBytes,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                return CreateFailure(selection, fileName, fileSizeBytes, totalBlocks, exportResult.PayloadCrc32, "Transferencia cancelada por el usuario.");
            } catch (Exception ex) {
                return CreateFailure(selection, fileName, fileSizeBytes, totalBlocks, exportResult.PayloadCrc32, BuildErrorMessage(ex, "No se pudo transferir el archivo ACX al tester."));
            }

            if (!uploadResult.Success) {
                return CreateFailure(selection, fileName, uploadResult.FileSizeBytes, uploadResult.TotalChunks, exportResult.PayloadCrc32, uploadResult.Message, uploadResult.InfoLine);
            }

            string successMessage = "ACX almacenado en el dispositivo. Todavía no está activado como perfil.";
            ReportProgress(progress, successMessage, fileSizeBytes, fileSizeBytes, totalBlocks, totalBlocks);

            return new AcxProfileTransferResult(
                true,
                successMessage,
                null,
                selection.Version.Id,
                selection.Profile.ProfileKey,
                selection.Version.VersionNumber,
                fileName,
                fileSizeBytes,
                totalBlocks,
                exportResult.PayloadCrc32,
                uploadResult.InfoLine);
        } catch (OperationCanceledException) {
            return CreateFailure(selection, selection.FileName, 0, 0, 0, "Transferencia cancelada por el usuario.");
        } catch (Exception ex) {
            return CreateFailure(selection, selection.FileName, 0, 0, 0, BuildErrorMessage(ex, "No se pudo transferir el archivo ACX."));
        }
    }

    private static void ValidateSelectionOrThrow(AcxPublishedProfileSelection selection)
    {
        if (selection.Profile is null) {
            throw new InvalidOperationException("No se selecciono un perfil ACX valido.");
        }

        if (selection.Version is null) {
            throw new InvalidOperationException("No se selecciono una version ACX valida.");
        }

        if (selection.Profile.Id != selection.Version.ProfileId) {
            throw new InvalidOperationException("La version ACX seleccionada no pertenece al perfil elegido.");
        }

        if (!selection.Version.IsPublished) {
            throw new InvalidOperationException("La version ACX seleccionada no esta publicada.");
        }

        string expectedFileName = AcxFormatConstants.BuildFileName(selection.Profile.ProfileKey, selection.Version.VersionNumber);
        if (!string.Equals(selection.FileName, expectedFileName, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("El nombre exportado para ACX no coincide con la seleccion publicada.");
        }
    }

    private static int CalculateBlockCount(int fileSizeBytes)
    {
        if (fileSizeBytes <= 0) {
            return 0;
        }

        return (fileSizeBytes + TransportChunkSizeBytes - 1) / TransportChunkSizeBytes;
    }

    private static void ReportProgress(
        IProgress<CommandFileUploadProgress>? progress,
        string stage,
        int sentBytes,
        int totalBytes,
        int currentBlockIndex,
        int totalBlocks)
    {
        int safeTotalBytes = Math.Max(0, totalBytes);
        int safeSentBytes = Math.Max(0, sentBytes);
        if (safeTotalBytes > 0 && safeSentBytes > safeTotalBytes) {
            safeSentBytes = safeTotalBytes;
        }

        int progressPercent = safeTotalBytes <= 0
            ? 0
            : (int)Math.Round(safeSentBytes * 100d / safeTotalBytes);

        progress?.Report(new CommandFileUploadProgress(
            currentBlockIndex,
            totalBlocks,
            progressPercent,
            stage,
            safeSentBytes,
            safeTotalBytes,
            currentBlockIndex,
            totalBlocks));
    }

    private static AcxProfileTransferResult CreateFailure(
        AcxPublishedProfileSelection selection,
        string fileName,
        int fileSizeBytes,
        int totalBlocks,
        uint payloadCrc32,
        string message,
        string? firmwareAckLine = null)
    {
        return new AcxProfileTransferResult(
            false,
            message,
            message,
            selection.Version.Id,
            selection.Profile.ProfileKey,
            selection.Version.VersionNumber,
            fileName,
            fileSizeBytes,
            totalBlocks,
            payloadCrc32,
            firmwareAckLine);
    }

    private static string BuildErrorMessage(Exception ex, string fallback)
    {
        return string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message;
    }
}
