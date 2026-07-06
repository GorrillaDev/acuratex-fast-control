using AcuratexControlApp.Models.Profiles;
using AcuratexControlApp.Services;

namespace AcuratexControlApp.Services.Profiles.Acx;

public sealed record AcxPackageHeader(
    ushort FormatVersion,
    ushort HeaderSize,
    uint FileSize,
    uint SectionDirectoryOffset,
    uint SectionDirectorySize,
    uint PayloadOffset,
    uint PayloadSize,
    uint PayloadCrc32,
    uint ProfileId,
    uint ProfileVersionId,
    int ProgramNumber,
    int VersionNumber,
    uint SchemaVersion,
    ushort SectionCount);

public sealed record AcxSectionDirectoryEntry(
    ushort SectionId,
    ushort SectionVersion,
    uint Offset,
    uint Size,
    uint RecordCount,
    uint Crc32);

public sealed record AcxProfileMetadata(
    long ProfileId,
    long ProfileVersionId,
    string ProfileKey,
    string DisplayName,
    string Description,
    string Notes,
    int ProgramNumber,
    int VersionNumber,
    uint SchemaVersion,
    bool Enabled,
    bool IsPublished,
    ProfileSourceKind SourceKind,
    uint? SourceCrc32);

public sealed record AcxProfilePackage(
    AcxPackageHeader Header,
    AcxProfileMetadata Metadata,
    ProfileVersionDocument Document,
    IReadOnlyList<AcxSectionDirectoryEntry> Sections);

public interface IAcxProfileCompiler
{
    AcxCompilationResult Compile(ProfileVersionDocument document);
}

public sealed record AcxCompilationResult(
    long ProfileVersionId,
    string ProfileKey,
    int VersionNumber,
    string? FilePath,
    byte[] PackageBytes,
    int PackageSizeBytes,
    uint PayloadCrc32,
    AcxPackageHeader Header,
    IReadOnlyList<AcxSectionDirectoryEntry> Sections,
    ProfileVersionDocument Document);
public sealed record AcxPublishedProfileSelection(
    ProfileRecord Profile,
    ProfileVersionSummary Version)
{
    public string FileName => AcxFormatConstants.BuildFileName(Profile.ProfileKey, Version.VersionNumber);
}

public sealed record AcxProfileTransferResult(
    bool Success,
    string Message,
    string? ErrorMessage,
    long ProfileVersionId,
    string ProfileKey,
    int VersionNumber,
    string FileName,
    int FileSizeBytes,
    int TotalBlocks,
    uint PayloadCrc32,
    string? FirmwareAckLine);


public sealed class AcxCompilationException : InvalidOperationException
{
    public AcxCompilationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public interface IAcxProfileExportService
{
    Task<AcxCompilationResult> ExportProfileVersionAsync(long profileVersionId, CancellationToken cancellationToken = default);

    Task<AcxCompilationResult> ExportLatestProfileAsync(string profileKey, CancellationToken cancellationToken = default);
}
public interface IAcxProfileTransferService
{
    Task<AcxProfileTransferResult> TransferPublishedProfileAsync(
        AcxPublishedProfileSelection selection,
        IProgress<CommandFileUploadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
