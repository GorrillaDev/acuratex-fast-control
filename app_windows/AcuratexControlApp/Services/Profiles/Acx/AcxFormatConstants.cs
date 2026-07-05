using AcuratexControlApp.Models.Profiles;

namespace AcuratexControlApp.Services.Profiles.Acx;

public static class AcxFormatConstants
{
    public const string FileExtension = ".acx";
    public const string PackageFormatTag = "ACX1";
    public static readonly byte[] MagicBytes = { (byte)'A', (byte)'C', (byte)'X', (byte)'1' };

    public const ushort FormatVersion = 1;
    public const ushort HeaderSize = 64;
    public const ushort SectionDirectoryEntrySize = 24;

    public const ushort MetadataSectionId = 1;
    public const ushort InitSectionId = 2;
    public const ushort TesteoSectionId = 3;
    public const ushort MotionSectionId = 4;
    public const ushort JSectionId = 5;
    public const ushort CascadeSectionId = 6;
    public const ushort StopSectionId = 7;
    public const ushort ActionsSectionId = 8;

    public const int MaxProfileKeyUtf8Bytes = 48;
    public const int MaxProfileDisplayNameUtf8Bytes = 64;
    public const int MaxProfileSystemUtf8Bytes = 8;
    public const int MaxInitScriptUtf8Bytes = 48;
    public const int MaxActionNameUtf8Bytes = 48;
    public const int MaxLineUtf8Bytes = 224;
    public const int MaxActions = 128;
    public const int MaxCommands = 768;
    public const int MaxInitStepsPerPhase = 192;
    public const int MaxMotionSequenceLength = 16;
    public const int MaxPositions = 16;
    public const int MaxYarnAddresses = 16;
    public const int MaxStitchAddresses = 32;
    public const uint MaxWaitMilliseconds = 10_000;
    public const byte MaxCanDlc = 8;
    public const uint MaxCanId = 0x1FFFFFFF;

    public const int MaxDenInstanceCount = 8;
    public const int MaxSicInstanceCount = 2;
    public const int MaxFeetInstanceCount = 2;
    public const int MaxJInstanceCount = 8;
    public const int MaxYarnInstanceCount = 2;
    public const int MaxStitchInstanceCount = 4;

    public const int MaxMetadataStringBytes = ushort.MaxValue;
    public const int MaxFileSizeBytes = 1_048_576;

    public static string BuildFileName(string profileKey, int versionNumber)
    {
        return $"{profileKey}_v{versionNumber}{FileExtension}";
    }
}

public enum AcxSectionId : ushort
{
    Metadata = AcxFormatConstants.MetadataSectionId,
    Init = AcxFormatConstants.InitSectionId,
    Testeo = AcxFormatConstants.TesteoSectionId,
    Motion = AcxFormatConstants.MotionSectionId,
    J = AcxFormatConstants.JSectionId,
    Cascade = AcxFormatConstants.CascadeSectionId,
    Stop = AcxFormatConstants.StopSectionId,
    Actions = AcxFormatConstants.ActionsSectionId,
}

public enum AcxStepKind : byte
{
    Can = 1,
    Wait = 2,
    Status = 3,
}
