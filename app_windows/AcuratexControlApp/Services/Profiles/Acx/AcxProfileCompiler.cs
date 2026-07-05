using System.Text;
using AcuratexControlApp.Models.Profiles;

namespace AcuratexControlApp.Services.Profiles.Acx;

public sealed class AcxProfileCompiler : IAcxProfileCompiler
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly ProfileValidator _validator;

    public AcxProfileCompiler(ProfileValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public AcxCompilationResult Compile(ProfileVersionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _validator.ValidateVersionDocumentOrThrow(document);
        EnsurePackageSizeWithinLimit(document);

        AcxProfileMetadata metadata = CreateMetadata(document);
        List<SectionPayload> sections = BuildSections(document);
        byte[] payload = BuildPayload(sections, out IReadOnlyList<AcxSectionDirectoryEntry> directoryEntries);
        uint payloadCrc32 = AcxCrc32.Compute(payload);

        int fileSize = AcxFormatConstants.HeaderSize + payload.Length;
        if (fileSize > AcxFormatConstants.MaxFileSizeBytes) {
            throw new AcxCompilationException(new[] { $"ACX package exceeds the supported size of {AcxFormatConstants.MaxFileSizeBytes} bytes." });
        }

        byte[] package = new byte[fileSize];
        using (MemoryStream packageStream = new(package, writable: true))
        using (AcxBinaryWriter writer = new(packageStream)) {
            WriteHeader(writer, metadata, payload.Length, payloadCrc32, sections.Count);
            writer.WriteBytes(payload);
        }

        AcxPackageHeader header = new(
            FormatVersion: AcxFormatConstants.FormatVersion,
            HeaderSize: AcxFormatConstants.HeaderSize,
            FileSize: (uint)fileSize,
            SectionDirectoryOffset: AcxFormatConstants.HeaderSize,
            SectionDirectorySize: (uint)(sections.Count * AcxFormatConstants.SectionDirectoryEntrySize),
            PayloadOffset: AcxFormatConstants.HeaderSize,
            PayloadSize: (uint)payload.Length,
            PayloadCrc32: payloadCrc32,
            ProfileId: ToUInt32Id(metadata.ProfileId, nameof(metadata.ProfileId)),
            ProfileVersionId: ToUInt32Id(metadata.ProfileVersionId, nameof(metadata.ProfileVersionId)),
            ProgramNumber: metadata.ProgramNumber,
            VersionNumber: metadata.VersionNumber,
            SchemaVersion: metadata.SchemaVersion,
            SectionCount: (ushort)sections.Count);

        ProfileRecord profile = new(
            Id: metadata.ProfileId,
            ProfileKey: metadata.ProfileKey,
            ProgramNumber: metadata.ProgramNumber,
            DisplayName: metadata.DisplayName,
            Description: metadata.Description,
            Enabled: metadata.Enabled,
            CreatedUtc: default,
            UpdatedUtc: default,
            Versions: new[]
            {
                new ProfileVersionSummary(
                    Id: metadata.ProfileVersionId,
                    ProfileId: metadata.ProfileId,
                    VersionNumber: metadata.VersionNumber,
                    SchemaVersion: (int)metadata.SchemaVersion,
                    Notes: metadata.Notes,
                    SourceKind: metadata.SourceKind,
                    Crc32: metadata.SourceCrc32,
                    IsPublished: metadata.IsPublished,
                    CreatedUtc: default),
            });

        ProfileVersionSummary version = profile.Versions[0];
        HeadCommandProfileModel commands = document.Commands with
        {
            ProgramNumber = metadata.ProgramNumber,
            ProgramName = metadata.DisplayName,
        };

        ProfileVersionDocument normalizedDocument = new(
            Profile: profile,
            Version: version,
            Commands: commands,
            Actions: document.Actions);

        return new AcxCompilationResult(
            ProfileVersionId: metadata.ProfileVersionId,
            ProfileKey: metadata.ProfileKey,
            VersionNumber: metadata.VersionNumber,
            FilePath: null,
            PackageBytes: package,
            PackageSizeBytes: fileSize,
            PayloadCrc32: payloadCrc32,
            Header: header,
            Sections: directoryEntries,
            Document: normalizedDocument);
    }

    private static AcxProfileMetadata CreateMetadata(ProfileVersionDocument document)
    {
        ProfileRecord profile = document.Profile;
        ProfileVersionSummary version = document.Version;

        return new AcxProfileMetadata(
            ProfileId: profile.Id,
            ProfileVersionId: version.Id,
            ProfileKey: profile.ProfileKey,
            DisplayName: profile.DisplayName,
            Description: profile.Description,
            Notes: version.Notes,
            ProgramNumber: document.Commands.ProgramNumber,
            VersionNumber: version.VersionNumber,
            SchemaVersion: (uint)version.SchemaVersion,
            Enabled: profile.Enabled,
            IsPublished: version.IsPublished,
            SourceKind: version.SourceKind,
            SourceCrc32: version.Crc32);
    }

    private static List<SectionPayload> BuildSections(ProfileVersionDocument document)
    {
        return new List<SectionPayload>
        {
            BuildMetadataSection(CreateMetadata(document)),
            BuildInitSection(document.Commands.InitSequence),
            BuildTesteoSection(document.Commands.Testeo),
            BuildMotionSection(document.Commands.Den, document.Commands.Sic, document.Commands.Feet),
            BuildJSection(document.Commands.J),
            BuildCascadeSection(document.Commands.Yarn, document.Commands.Stitch),
            BuildStopSection(document.Commands.Stop),
            BuildActionsSection(document.Actions),
        };
    }

    private static void EnsurePackageSizeWithinLimit(ProfileVersionDocument document)
    {
        long estimatedFileSize = EstimatePackageSize(document);
        if (estimatedFileSize > AcxFormatConstants.MaxFileSizeBytes) {
            throw new AcxCompilationException(new[] { $"ACX package exceeds the supported size of {AcxFormatConstants.MaxFileSizeBytes} bytes." });
        }
    }

    private static long EstimatePackageSize(ProfileVersionDocument document)
    {
        const int SectionCount = 8;
        long payloadSize = SectionCount * (long)AcxFormatConstants.SectionDirectoryEntrySize;
        payloadSize += EstimateMetadataSectionSize(document);
        payloadSize += EstimateInitSectionSize(document.Commands.InitSequence);
        payloadSize += EstimateTesteoSectionSize(document.Commands.Testeo);
        payloadSize += EstimateMotionSectionSize(document.Commands.Den, document.Commands.Sic, document.Commands.Feet);
        payloadSize += EstimateJSectionSize(document.Commands.J);
        payloadSize += EstimateCascadeSectionSize(document.Commands.Yarn, document.Commands.Stitch);
        payloadSize += EstimateStopSectionSize(document.Commands.Stop);
        payloadSize += EstimateActionsSectionSize(document.Actions);
        return AcxFormatConstants.HeaderSize + payloadSize;
    }

    private static long EstimateMetadataSectionSize(ProfileVersionDocument document)
    {
        ProfileRecord profile = document.Profile;
        ProfileVersionSummary version = document.Version;

        return 36L
            + Utf8.GetByteCount(profile.ProfileKey)
            + Utf8.GetByteCount(profile.DisplayName)
            + Utf8.GetByteCount(profile.Description)
            + Utf8.GetByteCount(version.Notes);
    }

    private static long EstimateInitSectionSize(HeadInitCommandSequenceModel sequence)
    {
        long size = 16L;
        size += EstimateInitStepsSize(sequence.Phase1Steps);
        size += EstimateInitStepsSize(sequence.Phase2Steps);
        return size;
    }

    private static long EstimateInitStepsSize(IReadOnlyList<HeadInitStepModel> steps)
    {
        long size = 0;
        for (int i = 0; i < steps.Count; i++) {
            HeadInitStepModel step = steps[i];
            size += 16L + Utf8.GetByteCount(step.RawText) + (step.Data?.Length ?? 0);
        }

        return size;
    }

    private static long EstimateTesteoSectionSize(HeadTesteoCommandProfileModel testeo)
    {
        return 32L
            + testeo.Ping.Data.Length
            + testeo.Reset.Data.Length;
    }

    private static long EstimateMotionSectionSize(
        HeadMotionCommandProfileModel den,
        HeadMotionCommandProfileModel sic,
        HeadMotionCommandProfileModel feet)
    {
        long size = 2L;
        size += EstimateMotionModuleSize(den);
        size += EstimateMotionModuleSize(sic);
        size += EstimateMotionModuleSize(feet);
        return size;
    }

    private static long EstimateMotionModuleSize(HeadMotionCommandProfileModel module)
    {
        long size = 24L;
        size += module.Positions.Count * 2L;
        size += module.RunSequence.Count;
        size += module.AlternateRunSequence.Count;
        return size;
    }

    private static long EstimateJSectionSize(HeadJCommandProfileModel jProfile)
    {
        return 16L;
    }

    private static long EstimateCascadeSectionSize(HeadCascadeCommandProfileModel yarn, HeadCascadeCommandProfileModel stitch)
    {
        long size = 2L;
        size += EstimateCascadeModuleSize(yarn);
        size += EstimateCascadeModuleSize(stitch);
        return size;
    }

    private static long EstimateCascadeModuleSize(HeadCascadeCommandProfileModel module)
    {
        long size = 20L;
        size += module.Addresses.Count;
        return size;
    }

    private static long EstimateStopSectionSize(HeadStopCommandProfileModel stop)
    {
        long size = 10L;
        size += stop.Frame?.Data.Length ?? 0;
        return size;
    }

    private static long EstimateActionsSectionSize(IReadOnlyList<ProfileActionModel> actions)
    {
        long size = 2L;
        for (int i = 0; i < actions.Count; i++) {
            ProfileActionModel action = actions[i];
            size += 16L;
            size += Utf8.GetByteCount(action.ActionName);
            size += Utf8.GetByteCount(action.Category);
            size += EstimateActionStepsSize(action.Steps);
        }

        return size;
    }

    private static long EstimateActionStepsSize(IReadOnlyList<ProfileActionStepModel> steps)
    {
        long size = 0;
        for (int i = 0; i < steps.Count; i++) {
            ProfileActionStepModel step = steps[i];
            size += 16L + (step.Data?.Length ?? 0);
        }

        return size;
    }
    private static SectionPayload BuildMetadataSection(AcxProfileMetadata metadata)
    {
        using MemoryStream stream = new();
        using AcxBinaryWriter writer = new(stream);

        byte[] profileKeyBytes = Utf8.GetBytes(metadata.ProfileKey);
        byte[] displayNameBytes = Utf8.GetBytes(metadata.DisplayName);
        byte[] descriptionBytes = Utf8.GetBytes(metadata.Description);
        byte[] notesBytes = Utf8.GetBytes(metadata.Notes);

        writer.WriteUInt32(ToUInt32Id(metadata.ProfileId, nameof(metadata.ProfileId)));
        writer.WriteUInt32(ToUInt32Id(metadata.ProfileVersionId, nameof(metadata.ProfileVersionId)));
        writer.WriteByte(metadata.Enabled ? (byte)1 : (byte)0);
        writer.WriteByte(metadata.IsPublished ? (byte)1 : (byte)0);
        writer.WriteByte((byte)metadata.SourceKind);
        writer.WriteByte(metadata.SourceCrc32.HasValue ? (byte)1 : (byte)0);
        writer.WriteInt32(metadata.ProgramNumber);
        writer.WriteInt32(metadata.VersionNumber);
        writer.WriteUInt32(metadata.SchemaVersion);
        writer.WriteUInt16(checked((ushort)profileKeyBytes.Length));
        writer.WriteUInt16(checked((ushort)displayNameBytes.Length));
        writer.WriteUInt16(checked((ushort)descriptionBytes.Length));
        writer.WriteUInt16(checked((ushort)notesBytes.Length));
        writer.WriteUInt32(metadata.SourceCrc32 ?? 0U);
        writer.WriteBytes(profileKeyBytes);
        writer.WriteBytes(displayNameBytes);
        writer.WriteBytes(descriptionBytes);
        writer.WriteBytes(notesBytes);
        return new SectionPayload((ushort)AcxSectionId.Metadata, 1, stream.ToArray(), 1U);
    }

    private static SectionPayload BuildInitSection(HeadInitCommandSequenceModel sequence)
    {
        using MemoryStream stream = new();
        using AcxBinaryWriter writer = new(stream);

        writer.WriteUInt32(sequence.Phase1StepDelayMs);
        writer.WriteUInt32(sequence.PhaseGapMs);
        writer.WriteUInt32(sequence.Phase2StepDelayMs);
        writer.WriteUInt16(checked((ushort)sequence.Phase1StepCount));
        writer.WriteUInt16(checked((ushort)sequence.Phase2StepCount));

        WriteInitSteps(writer, sequence.Phase1Steps);
        WriteInitSteps(writer, sequence.Phase2Steps);
        return new SectionPayload((ushort)AcxSectionId.Init, 1, stream.ToArray(), (uint)(sequence.Phase1StepCount + sequence.Phase2StepCount));
    }

    private static void WriteInitSteps(AcxBinaryWriter writer, IReadOnlyList<HeadInitStepModel> steps)
    {
        for (int i = 0; i < steps.Count; i++) {
            HeadInitStepModel step = steps[i];
            writer.WriteUInt16(checked((ushort)step.StepOrder));
            writer.WriteByte((byte)step.Phase);
            writer.WriteByte((byte)step.StepKind);
            writer.WriteByte(step.Bus.HasValue ? checked((byte)step.Bus.Value) : (byte)0);
            writer.WriteByte(step.Dlc ?? 0);
            writer.WriteUInt16(checked((ushort)Utf8.GetByteCount(step.RawText)));
            writer.WriteUInt32(step.CanId ?? 0U);
            writer.WriteUInt32((uint)(step.WaitMs ?? 0));
            writer.WriteBytes(Utf8.GetBytes(step.RawText));
            if (step.Data is not null && step.Data.Length > 0) {
                writer.WriteBytes(step.Data);
            }
        }
    }

    private static SectionPayload BuildTesteoSection(HeadTesteoCommandProfileModel testeo)
    {
        using MemoryStream stream = new();
        using AcxBinaryWriter writer = new(stream);

        writer.WriteUInt32(testeo.Ping.CanId);
        writer.WriteByte(testeo.Ping.Dlc);
        writer.WriteUInt32(testeo.ResponseCanId);
        writer.WriteUInt32(testeo.ResetCanId);
        writer.WriteByte(testeo.Reset.Dlc);
        writer.WriteByte(testeo.SuccessCode);
        writer.WriteByte(testeo.MissingExpansionCode);
        writer.WriteByte(testeo.MissingForceCode);
        writer.WriteByte(testeo.ForceBoard1Code);
        writer.WriteByte(testeo.ForceBoard2Code);
        writer.WriteUInt16(testeo.MaxTries);
        writer.WriteUInt32(testeo.ResponseTimeoutMs);
        writer.WriteUInt32(testeo.RetryDelayMs);
        writer.WriteUInt32(testeo.ResetDebounceMs);
        writer.WriteBytes(testeo.Ping.Data);
        writer.WriteBytes(testeo.Reset.Data);
        return new SectionPayload((ushort)AcxSectionId.Testeo, 1, stream.ToArray(), 1U);
    }

    private static SectionPayload BuildMotionSection(HeadMotionCommandProfileModel den, HeadMotionCommandProfileModel sic, HeadMotionCommandProfileModel feet)
    {
        using MemoryStream stream = new();
        using AcxBinaryWriter writer = new(stream);

        IReadOnlyList<HeadMotionCommandProfileModel> modules = new[] { den, sic, feet };
        writer.WriteUInt16((ushort)modules.Count);
        foreach (HeadMotionCommandProfileModel module in modules) {
            writer.WriteByte((byte)module.ModuleKind);
            writer.WriteByte(module.Opcode);
            writer.WriteByte(module.MotorIndexBase);
            writer.WriteByte(0);
            writer.WriteUInt16(checked((ushort)module.InstanceCount));
            writer.WriteUInt16(checked((ushort)module.PositionCount));
            writer.WriteUInt16(checked((ushort)module.RunSequenceCount));
            writer.WriteUInt16(checked((ushort)module.AlternateRunSequenceCount));
            writer.WriteUInt32(module.CanId);
            writer.WriteUInt32(module.RunPeriodMs);
            writer.WriteUInt32(module.AlternateRunPeriodMs);

            foreach (ushort position in module.Positions) {
                writer.WriteUInt16(position);
            }

            foreach (byte positionIndex in module.RunSequence) {
                writer.WriteByte(positionIndex);
            }

            foreach (byte positionIndex in module.AlternateRunSequence) {
                writer.WriteByte(positionIndex);
            }
        }

        return new SectionPayload((ushort)AcxSectionId.Motion, 1, stream.ToArray(), 3U);
    }

    private static SectionPayload BuildJSection(HeadJCommandProfileModel jProfile)
    {
        using MemoryStream stream = new();
        using AcxBinaryWriter writer = new(stream);

        writer.WriteUInt32(jProfile.CanId);
        writer.WriteByte(jProfile.Opcode);
        writer.WriteByte(jProfile.InstanceIndexBase);
        writer.WriteUInt16(checked((ushort)jProfile.InstanceCount));
        writer.WriteByte(jProfile.ChannelCount);
        writer.WriteByte(jProfile.InitialRegister);
        writer.WriteByte(jProfile.OnAllRegister);
        writer.WriteByte(jProfile.OffAllRegister);
        writer.WriteUInt32(jProfile.RunPeriodMs);
        return new SectionPayload((ushort)AcxSectionId.J, 1, stream.ToArray(), 1U);
    }

    private static SectionPayload BuildCascadeSection(HeadCascadeCommandProfileModel yarn, HeadCascadeCommandProfileModel stitch)
    {
        using MemoryStream stream = new();
        using AcxBinaryWriter writer = new(stream);

        IReadOnlyList<HeadCascadeCommandProfileModel> modules = new[] { yarn, stitch };
        writer.WriteUInt16((ushort)modules.Count);
        foreach (HeadCascadeCommandProfileModel module in modules) {
            writer.WriteByte((byte)module.ModuleKind);
            writer.WriteByte(module.Opcode);
            writer.WriteUInt16(module.AddressesPerInstance);
            writer.WriteUInt16(checked((ushort)module.InstanceCount));
            writer.WriteUInt16(checked((ushort)module.AddressCount));
            writer.WriteUInt32(module.CanId);
            writer.WriteUInt32(module.RunPeriodMs);
            writer.WriteByte(module.OnValue);
            writer.WriteByte(module.OffValue);
            writer.WriteUInt16(0);

            foreach (byte address in module.Addresses) {
                writer.WriteByte(address);
            }
        }

        return new SectionPayload((ushort)AcxSectionId.Cascade, 1, stream.ToArray(), 2U);
    }

    private static SectionPayload BuildStopSection(HeadStopCommandProfileModel stop)
    {
        using MemoryStream stream = new();
        using AcxBinaryWriter writer = new(stream);

        writer.WriteByte(stop.SendsCanFrame ? (byte)1 : (byte)0);
        writer.WriteByte(stop.Frame is null ? (byte)0 : (byte)1);
        writer.WriteUInt16(0);
        writer.WriteUInt32(stop.Frame?.CanId ?? 0U);
        writer.WriteByte(stop.Frame?.Dlc ?? 0);
        writer.WriteByte(0);
        if (stop.Frame is not null) {
            writer.WriteBytes(stop.Frame.Data);
        }

        return new SectionPayload((ushort)AcxSectionId.Stop, 1, stream.ToArray(), 1U);
    }

    private static SectionPayload BuildActionsSection(IReadOnlyList<ProfileActionModel> actions)
    {
        using MemoryStream stream = new();
        using AcxBinaryWriter writer = new(stream);

        writer.WriteUInt16(checked((ushort)actions.Count));
        foreach (ProfileActionModel action in actions) {
            writer.WriteUInt32(ToUInt32Id(action.Id, nameof(action.Id)));
            writer.WriteUInt16(checked((ushort)action.StepCount));
            writer.WriteByte(action.Enabled ? (byte)1 : (byte)0);
            writer.WriteByte(0);
            writer.WriteUInt16(checked((ushort)Utf8.GetByteCount(action.ActionName)));
            writer.WriteUInt16(checked((ushort)Utf8.GetByteCount(action.Category)));
            writer.WriteUInt32(0);
            writer.WriteBytes(Utf8.GetBytes(action.ActionName));
            writer.WriteBytes(Utf8.GetBytes(action.Category));
            WriteActionSteps(writer, action.Steps);
        }

        return new SectionPayload((ushort)AcxSectionId.Actions, 1, stream.ToArray(), (uint)actions.Count);
    }

    private static void WriteActionSteps(AcxBinaryWriter writer, IReadOnlyList<ProfileActionStepModel> steps)
    {
        for (int i = 0; i < steps.Count; i++) {
            ProfileActionStepModel step = steps[i];
            writer.WriteUInt16(checked((ushort)step.StepOrder));
            writer.WriteByte((byte)step.StepKind);
            writer.WriteByte(step.Bus.HasValue ? checked((byte)step.Bus.Value) : (byte)0);
            writer.WriteByte(step.Dlc ?? 0);
            writer.WriteByte(0);
            writer.WriteUInt32(step.CanId ?? 0U);
            writer.WriteUInt32((uint)(step.WaitMs ?? 0));
            writer.WriteUInt16(0);
            if (step.Data is not null && step.Data.Length > 0) {
                writer.WriteBytes(step.Data);
            }
        }
    }

    private static byte[] BuildPayload(List<SectionPayload> sections, out IReadOnlyList<AcxSectionDirectoryEntry> directoryEntries)
    {
        int directorySize = sections.Count * AcxFormatConstants.SectionDirectoryEntrySize;
        int nextSectionOffset = AcxFormatConstants.HeaderSize + directorySize;
        List<AcxSectionDirectoryEntry> entries = new(sections.Count);

        using MemoryStream payloadStream = new();
        using (AcxBinaryWriter writer = new(payloadStream)) {
            foreach (SectionPayload section in sections) {
                uint offset = (uint)nextSectionOffset;
                uint size = (uint)section.Data.Length;
                uint crc32 = AcxCrc32.Compute(section.Data);
                entries.Add(new AcxSectionDirectoryEntry(section.SectionId, section.SectionVersion, offset, size, section.RecordCount, crc32));
                writer.WriteUInt16(section.SectionId);
                writer.WriteUInt16(section.SectionVersion);
                writer.WriteUInt32(offset);
                writer.WriteUInt32(size);
                writer.WriteUInt32(section.RecordCount);
                writer.WriteUInt32(crc32);
                writer.WriteUInt32(0);
                nextSectionOffset += section.Data.Length;
            }

            foreach (SectionPayload section in sections) {
                writer.WriteBytes(section.Data);
            }
        }

        directoryEntries = entries;
        return payloadStream.ToArray();
    }

    private static void WriteHeader(
        AcxBinaryWriter writer,
        AcxProfileMetadata metadata,
        int payloadSize,
        uint payloadCrc32,
        int sectionCount)
    {
        writer.WriteBytes(AcxFormatConstants.MagicBytes);
        writer.WriteUInt16(AcxFormatConstants.FormatVersion);
        writer.WriteUInt16(AcxFormatConstants.HeaderSize);
        writer.WriteUInt32((uint)(AcxFormatConstants.HeaderSize + payloadSize));
        writer.WriteUInt32(AcxFormatConstants.HeaderSize);
        writer.WriteUInt32((uint)(sectionCount * AcxFormatConstants.SectionDirectoryEntrySize));
        writer.WriteUInt32(AcxFormatConstants.HeaderSize);
        writer.WriteUInt32((uint)payloadSize);
        writer.WriteUInt32(payloadCrc32);
        writer.WriteUInt32(ToUInt32Id(metadata.ProfileId, nameof(metadata.ProfileId)));
        writer.WriteUInt32(ToUInt32Id(metadata.ProfileVersionId, nameof(metadata.ProfileVersionId)));
        writer.WriteInt32(metadata.ProgramNumber);
        writer.WriteInt32(metadata.VersionNumber);
        writer.WriteUInt32(metadata.SchemaVersion);
        writer.WriteUInt16((ushort)sectionCount);
        writer.WriteUInt16(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
    }

    private static uint ToUInt32Id(long value, string parameterName)
    {
        if (value < 0 || value > uint.MaxValue) {
            throw new AcxCompilationException(new[] { $"{parameterName} '{value}' is outside the ACX v1 uint32 range." });
        }

        return (uint)value;
    }

    private sealed record SectionPayload(ushort SectionId, ushort SectionVersion, byte[] Data, uint RecordCount);
}
