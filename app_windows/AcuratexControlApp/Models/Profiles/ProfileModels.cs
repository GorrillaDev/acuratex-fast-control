using System.Globalization;

namespace AcuratexControlApp.Models.Profiles;

public enum ProfileSourceKind
{
    Sqlite = 1,
    CppImport = 2,
    Manual = 3,
}

public enum HeadInitStepKind
{
    Can = 1,
    Wait = 2,
    Status = 3,
}

public enum HeadMotionModuleKind
{
    Den = 1,
    Sic = 2,
    Feet = 3,
}

public enum HeadCascadeModuleKind
{
    Yarn = 1,
    Stitch = 2,
}

public sealed record HeadCanCommandModel(uint CanId, byte Dlc, byte[] Data)
{
    public static HeadCanCommandModel Create(uint canId, params byte[] data)
    {
        byte[] copy = data?.ToArray() ?? throw new ArgumentNullException(nameof(data));
        return new HeadCanCommandModel(canId, checked((byte)copy.Length), copy);
    }
}

public sealed record HeadInitStepModel(
    int Phase,
    int StepOrder,
    string RawText,
    HeadInitStepKind StepKind,
    int? Bus,
    uint? CanId,
    byte? Dlc,
    byte[]? Data,
    int? WaitMs)
{
    public static HeadInitStepModel Parse(string rawText, int phase, int stepOrder, int defaultBus = 1)
    {
        if (string.IsNullOrWhiteSpace(rawText)) {
            throw new ArgumentException("Raw INIT step cannot be empty.", nameof(rawText));
        }

        string cleanText = rawText.Trim();
        if (string.Equals(cleanText, "status", StringComparison.OrdinalIgnoreCase)) {
            return new HeadInitStepModel(phase, stepOrder, cleanText, HeadInitStepKind.Status, null, null, null, null, null);
        }

        string[] parts = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) {
            throw new FormatException($"Invalid INIT step: '{rawText}'.");
        }

        if (string.Equals(parts[0], "WAIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parts[0], "DELAY", StringComparison.OrdinalIgnoreCase)) {
            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int waitMs) || waitMs < 0) {
                throw new FormatException($"Invalid WAIT step: '{rawText}'.");
            }

            return new HeadInitStepModel(phase, stepOrder, cleanText, HeadInitStepKind.Wait, null, null, null, null, waitMs);
        }

        if (!TryParseHexUInt32(parts[0], out uint canId)) {
            throw new FormatException($"Invalid CAN id in INIT step: '{rawText}'.");
        }

        if (parts.Length < 2) {
            throw new FormatException($"CAN step without payload: '{rawText}'.");
        }

        byte[] data = new byte[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++) {
            if (!TryParseHexByte(parts[i], out data[i - 1])) {
                throw new FormatException($"Invalid CAN byte in INIT step: '{rawText}'.");
            }
        }

        return new HeadInitStepModel(
            phase,
            stepOrder,
            cleanText,
            HeadInitStepKind.Can,
            defaultBus,
            canId,
            checked((byte)data.Length),
            data,
            null);
    }

    private static bool TryParseHexUInt32(string value, out uint result)
    {
        string cleanValue = NormalizeHexToken(value);
        return uint.TryParse(cleanValue, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseHexByte(string value, out byte result)
    {
        string cleanValue = NormalizeHexToken(value);
        return byte.TryParse(cleanValue, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result);
    }

    private static string NormalizeHexToken(string value)
    {
        string cleanValue = value.Trim();
        if (cleanValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            cleanValue = cleanValue[2..];
        }

        return cleanValue;
    }
}

public sealed record HeadInitCommandSequenceModel(
    uint Phase1StepDelayMs,
    uint PhaseGapMs,
    uint Phase2StepDelayMs,
    IReadOnlyList<HeadInitStepModel> Phase1Steps,
    IReadOnlyList<HeadInitStepModel> Phase2Steps)
{
    public int Phase1StepCount => Phase1Steps.Count;
    public int Phase2StepCount => Phase2Steps.Count;

    public static HeadInitCommandSequenceModel FromRawText(
        string phase1Text,
        uint phase1StepDelayMs,
        uint phaseGapMs,
        string phase2Text,
        uint phase2StepDelayMs,
        int defaultBus = 1)
    {
        IReadOnlyList<HeadInitStepModel> phase1Steps = ParsePhaseText(phase1Text, 1, defaultBus);
        IReadOnlyList<HeadInitStepModel> phase2Steps = ParsePhaseText(phase2Text, 2, defaultBus);
        return new HeadInitCommandSequenceModel(phase1StepDelayMs, phaseGapMs, phase2StepDelayMs, phase1Steps, phase2Steps);
    }

    private static IReadOnlyList<HeadInitStepModel> ParsePhaseText(string phaseText, int phase, int defaultBus)
    {
        if (string.IsNullOrWhiteSpace(phaseText)) {
            return Array.Empty<HeadInitStepModel>();
        }

        string[] lines = phaseText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<HeadInitStepModel> steps = new(lines.Length);
        for (int i = 0; i < lines.Length; i++) {
            steps.Add(HeadInitStepModel.Parse(lines[i], phase, i, defaultBus));
        }

        return steps;
    }
}

public sealed record HeadTesteoCommandProfileModel(
    HeadCanCommandModel Ping,
    uint ResponseCanId,
    uint ResetCanId,
    HeadCanCommandModel Reset,
    byte SuccessCode,
    byte MissingExpansionCode,
    byte MissingForceCode,
    byte ForceBoard1Code,
    byte ForceBoard2Code,
    ushort MaxTries,
    uint ResponseTimeoutMs,
    uint RetryDelayMs,
    uint ResetDebounceMs);

public sealed record HeadMotionCommandProfileModel(
    HeadMotionModuleKind ModuleKind,
    uint CanId,
    byte Opcode,
    byte MotorIndexBase,
    int InstanceCount,
    IReadOnlyList<byte> RunSequence,
    IReadOnlyList<byte> AlternateRunSequence,
    IReadOnlyList<ushort> Positions,
    uint RunPeriodMs,
    uint AlternateRunPeriodMs)
{
    public int RunSequenceCount => RunSequence.Count;
    public int AlternateRunSequenceCount => AlternateRunSequence.Count;
    public int PositionCount => Positions.Count;
}

public sealed record HeadJCommandProfileModel(
    uint CanId,
    byte Opcode,
    byte InstanceIndexBase,
    int InstanceCount,
    byte ChannelCount,
    byte InitialRegister,
    byte OnAllRegister,
    byte OffAllRegister,
    uint RunPeriodMs);

public sealed record HeadCascadeCommandProfileModel(
    HeadCascadeModuleKind ModuleKind,
    uint CanId,
    byte Opcode,
    byte AddressesPerInstance,
    int InstanceCount,
    IReadOnlyList<byte> Addresses,
    byte OnValue,
    byte OffValue,
    uint RunPeriodMs)
{
    public int AddressCount => Addresses.Count;
}

public sealed record HeadStopCommandProfileModel(bool SendsCanFrame, HeadCanCommandModel? Frame);

public sealed record HeadCommandProfileModel(
    int ProgramNumber,
    string ProgramName,
    HeadInitCommandSequenceModel InitSequence,
    HeadTesteoCommandProfileModel Testeo,
    HeadMotionCommandProfileModel Den,
    HeadMotionCommandProfileModel Sic,
    HeadMotionCommandProfileModel Feet,
    HeadJCommandProfileModel J,
    HeadCascadeCommandProfileModel Yarn,
    HeadCascadeCommandProfileModel Stitch,
    HeadStopCommandProfileModel Stop);

public sealed record ProfileCreateRequest(
    string ProfileKey,
    int? ProgramNumber,
    string DisplayName,
    string Description,
    bool Enabled);

public sealed record ProfileVersionWriteRequest(
    int? VersionNumber,
    int SchemaVersion,
    string Notes,
    ProfileSourceKind SourceKind,
    uint? Crc32,
    bool IsPublished,
    HeadCommandProfileModel Commands);

public sealed record ProfileVersionSummary(
    long Id,
    long ProfileId,
    int VersionNumber,
    int SchemaVersion,
    string Notes,
    ProfileSourceKind SourceKind,
    uint? Crc32,
    bool IsPublished,
    DateTimeOffset CreatedUtc);

public sealed record ProfileRecord(
    long Id,
    string ProfileKey,
    int? ProgramNumber,
    string DisplayName,
    string Description,
    bool Enabled,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<ProfileVersionSummary> Versions);

public sealed record ProfileListItem(
    long Id,
    string ProfileKey,
    int? ProgramNumber,
    string DisplayName,
    bool Enabled,
    int VersionCount,
    int? LatestVersionNumber,
    ProfileSourceKind? LatestSourceKind,
    bool LatestPublished,
    DateTimeOffset UpdatedUtc);

public sealed record ProfileVersionDocument(
    ProfileRecord Profile,
    ProfileVersionSummary Version,
    HeadCommandProfileModel Commands);

internal static class ProfileSqliteMappings
{
    public static string ToDatabaseValue(this ProfileSourceKind kind) => kind switch
    {
        ProfileSourceKind.Sqlite => "SQLITE",
        ProfileSourceKind.CppImport => "CPP_IMPORT",
        ProfileSourceKind.Manual => "MANUAL",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ProfileSourceKind ParseProfileSourceKind(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "SQLITE" => ProfileSourceKind.Sqlite,
            "CPP_IMPORT" => ProfileSourceKind.CppImport,
            "MANUAL" => ProfileSourceKind.Manual,
            _ => throw new FormatException($"Unsupported profile source kind '{value}'."),
        };
    }

    public static string ToDatabaseValue(this HeadMotionModuleKind kind) => kind switch
    {
        HeadMotionModuleKind.Den => "DEN",
        HeadMotionModuleKind.Sic => "SIC",
        HeadMotionModuleKind.Feet => "FEET",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static HeadMotionModuleKind ParseHeadMotionModuleKind(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "DEN" => HeadMotionModuleKind.Den,
            "SIC" => HeadMotionModuleKind.Sic,
            "FEET" => HeadMotionModuleKind.Feet,
            _ => throw new FormatException($"Unsupported motion module kind '{value}'."),
        };
    }

    public static string ToDatabaseValue(this HeadCascadeModuleKind kind) => kind switch
    {
        HeadCascadeModuleKind.Yarn => "YARN",
        HeadCascadeModuleKind.Stitch => "STITCH",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static HeadCascadeModuleKind ParseHeadCascadeModuleKind(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "YARN" => HeadCascadeModuleKind.Yarn,
            "STITCH" => HeadCascadeModuleKind.Stitch,
            _ => throw new FormatException($"Unsupported cascade module kind '{value}'."),
        };
    }

    public static string ToDatabaseValue(this HeadInitStepKind kind) => kind switch
    {
        HeadInitStepKind.Can => "CAN",
        HeadInitStepKind.Wait => "WAIT",
        HeadInitStepKind.Status => "STATUS",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static HeadInitStepKind ParseHeadInitStepKind(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "CAN" => HeadInitStepKind.Can,
            "WAIT" => HeadInitStepKind.Wait,
            "STATUS" => HeadInitStepKind.Status,
            _ => throw new FormatException($"Unsupported INIT step kind '{value}'."),
        };
    }
}
