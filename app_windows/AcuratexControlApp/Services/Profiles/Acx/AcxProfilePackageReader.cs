
using System.Buffers.Binary;
using System.Text;
using AcuratexControlApp.Models.Profiles;

namespace AcuratexControlApp.Services.Profiles.Acx;

public sealed class AcxProfilePackageReader
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public AcxProfilePackage Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Package path cannot be empty.", nameof(path));
        }

        FileInfo fileInfo = new(path);
        if (fileInfo.Exists) {
            EnsurePackageSizeWithinLimit(fileInfo.Length);
        }

        return Read(File.ReadAllBytes(path));
    }

    public AcxProfilePackage Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Read(bytes.AsSpan());
    }

    public AcxProfilePackage Read(ReadOnlySpan<byte> data)
    {
        EnsurePackageSizeWithinLimit(data.Length);

        if (data.Length < AcxFormatConstants.HeaderSize) {
            throw new InvalidDataException("ACX package is truncated: header is incomplete.");
        }

        int offset = 0;
        EnsureMagic(data, ref offset);
        ushort formatVersion = ReadUInt16(data, ref offset);
        ushort headerSize = ReadUInt16(data, ref offset);
        uint fileSize = ReadUInt32(data, ref offset);
        uint sectionDirectoryOffset = ReadUInt32(data, ref offset);
        uint sectionDirectorySize = ReadUInt32(data, ref offset);
        uint payloadOffset = ReadUInt32(data, ref offset);
        uint payloadSize = ReadUInt32(data, ref offset);
        uint payloadCrc32 = ReadUInt32(data, ref offset);
        uint profileId = ReadUInt32(data, ref offset);
        uint profileVersionId = ReadUInt32(data, ref offset);
        int programNumber = ReadInt32(data, ref offset);
        int versionNumber = ReadInt32(data, ref offset);
        uint schemaVersion = ReadUInt32(data, ref offset);
        ushort sectionCount = ReadUInt16(data, ref offset);
        ushort reserved = ReadUInt16(data, ref offset);
        uint reserved2 = ReadUInt32(data, ref offset);
        uint reserved3 = ReadUInt32(data, ref offset);

        if (formatVersion != AcxFormatConstants.FormatVersion) {
            throw new InvalidDataException($"Unsupported ACX format version '{formatVersion}'.");
        }

        if (headerSize != AcxFormatConstants.HeaderSize) {
            throw new InvalidDataException($"Unsupported ACX header size '{headerSize}'.");
        }

        if (reserved != 0 || reserved2 != 0 || reserved3 != 0) {
            throw new InvalidDataException("ACX header reserved bytes must be zero.");
        }

        if (fileSize != data.Length) {
            throw new InvalidDataException($"ACX file size mismatch: header says {fileSize} bytes but file contains {data.Length} bytes.");
        }

        if (sectionDirectoryOffset != AcxFormatConstants.HeaderSize || payloadOffset != AcxFormatConstants.HeaderSize) {
            throw new InvalidDataException("ACX payload must start immediately after the fixed header.");
        }

        if (sectionDirectorySize != (uint)sectionCount * AcxFormatConstants.SectionDirectoryEntrySize) {
            throw new InvalidDataException("ACX section directory size is inconsistent with the directory entry count.");
        }

        if (payloadSize != fileSize - payloadOffset) {
            throw new InvalidDataException("ACX payload size is inconsistent with the file size.");
        }

        uint computedPayloadCrc = AcxCrc32.Compute(data.Slice((int)payloadOffset, checked((int)payloadSize)));
        if (computedPayloadCrc != payloadCrc32) {
            throw new InvalidDataException($"ACX payload CRC mismatch. Expected 0x{payloadCrc32:X8}, computed 0x{computedPayloadCrc:X8}.");
        }

        List<AcxSectionDirectoryEntry> entries = ReadSectionDirectory(data, sectionCount);
        ValidateDirectoryLayout(entries, data.Length);

        AcxProfileMetadata metadata = default!;
        HeadInitCommandSequenceModel init = default!;
        HeadTesteoCommandProfileModel testeo = default!;
        HeadMotionCommandProfileModel den = default!;
        HeadMotionCommandProfileModel sic = default!;
        HeadMotionCommandProfileModel feet = default!;
        HeadJCommandProfileModel jProfile = default!;
        HeadCascadeCommandProfileModel yarn = default!;
        HeadCascadeCommandProfileModel stitch = default!;
        HeadStopCommandProfileModel stop = default!;
        List<ProfileActionModel> actions = new();

        bool sawMetadata = false;
        bool sawInit = false;
        bool sawTesteo = false;
        bool sawMotion = false;
        bool sawJ = false;
        bool sawCascade = false;
        bool sawStop = false;
        bool sawActions = false;

        foreach (AcxSectionDirectoryEntry entry in entries) {
            ReadOnlySpan<byte> sectionData = data.Slice(checked((int)entry.Offset), checked((int)entry.Size));
            if (AcxCrc32.Compute(sectionData) != entry.Crc32) {
                throw new InvalidDataException($"ACX section '{entry.SectionId}' CRC mismatch.");
            }

            switch ((AcxSectionId)entry.SectionId) {
                case AcxSectionId.Metadata:
                    metadata = ReadMetadataSection(sectionData, entry.RecordCount);
                    sawMetadata = true;
                    break;
                case AcxSectionId.Init:
                    init = ReadInitSection(sectionData, entry.RecordCount);
                    sawInit = true;
                    break;
                case AcxSectionId.Testeo:
                    testeo = ReadTesteoSection(sectionData, entry.RecordCount);
                    sawTesteo = true;
                    break;
                case AcxSectionId.Motion:
                    (den, sic, feet) = ReadMotionSection(sectionData, entry.RecordCount);
                    sawMotion = true;
                    break;
                case AcxSectionId.J:
                    jProfile = ReadJSection(sectionData, entry.RecordCount);
                    sawJ = true;
                    break;
                case AcxSectionId.Cascade:
                    (yarn, stitch) = ReadCascadeSection(sectionData, entry.RecordCount);
                    sawCascade = true;
                    break;
                case AcxSectionId.Stop:
                    stop = ReadStopSection(sectionData, entry.RecordCount);
                    sawStop = true;
                    break;
                case AcxSectionId.Actions:
                    actions = ReadActionsSection(sectionData, entry.RecordCount);
                    sawActions = true;
                    break;
                default:
                    break;
            }
        }

        if (!sawMetadata || !sawInit || !sawTesteo || !sawMotion || !sawJ || !sawCascade || !sawStop || !sawActions) {
            throw new InvalidDataException("ACX package is missing one or more required sections.");
        }

        ProfileVersionDocument document = BuildDocument(metadata, init, testeo, den, sic, feet, jProfile, yarn, stitch, stop, actions);
        AcxPackageHeader header = new(
            FormatVersion: formatVersion,
            HeaderSize: headerSize,
            FileSize: fileSize,
            SectionDirectoryOffset: sectionDirectoryOffset,
            SectionDirectorySize: sectionDirectorySize,
            PayloadOffset: payloadOffset,
            PayloadSize: payloadSize,
            PayloadCrc32: payloadCrc32,
            ProfileId: profileId,
            ProfileVersionId: profileVersionId,
            ProgramNumber: programNumber,
            VersionNumber: versionNumber,
            SchemaVersion: schemaVersion,
            SectionCount: sectionCount);

        return new AcxProfilePackage(header, metadata, document, entries);
    }

    private static List<AcxSectionDirectoryEntry> ReadSectionDirectory(ReadOnlySpan<byte> data, ushort sectionCount)
    {
        int offset = AcxFormatConstants.HeaderSize;
        List<AcxSectionDirectoryEntry> entries = new(sectionCount);
        for (int i = 0; i < sectionCount; i++) {
            ushort sectionId = ReadUInt16(data, ref offset);
            ushort sectionVersion = ReadUInt16(data, ref offset);
            uint sectionOffset = ReadUInt32(data, ref offset);
            uint sectionSize = ReadUInt32(data, ref offset);
            uint recordCount = ReadUInt32(data, ref offset);
            uint crc32 = ReadUInt32(data, ref offset);
            uint reserved = ReadUInt32(data, ref offset);
            if (reserved != 0) {
                throw new InvalidDataException("ACX section directory reserved bytes must be zero.");
            }

            entries.Add(new AcxSectionDirectoryEntry(sectionId, sectionVersion, sectionOffset, sectionSize, recordCount, crc32));
        }

        return entries;
    }

    private static void ValidateDirectoryLayout(IReadOnlyList<AcxSectionDirectoryEntry> entries, int fileLength)
    {
        uint previousEnd = AcxFormatConstants.HeaderSize;
        HashSet<ushort> seen = new();
        foreach (AcxSectionDirectoryEntry entry in entries.OrderBy(entry => entry.Offset)) {
            if (!seen.Add(entry.SectionId)) {
                throw new InvalidDataException($"ACX section '{entry.SectionId}' is duplicated in the directory.");
            }

            if (entry.Offset < AcxFormatConstants.HeaderSize) {
                throw new InvalidDataException($"ACX section '{entry.SectionId}' has an offset inside the header.");
            }

            if (entry.Size == 0) {
                throw new InvalidDataException($"ACX section '{entry.SectionId}' has zero size.");
            }

            ulong end = (ulong)entry.Offset + entry.Size;
            if (end > (ulong)fileLength) {
                throw new InvalidDataException($"ACX section '{entry.SectionId}' exceeds the file length.");
            }

            if (entry.Offset < previousEnd) {
                throw new InvalidDataException($"ACX section '{entry.SectionId}' overlaps a previous section.");
            }

            previousEnd = (uint)end;
        }
    }
    private static AcxProfileMetadata ReadMetadataSection(ReadOnlySpan<byte> data, uint recordCount)
    {
        if (recordCount != 1) {
            throw new InvalidDataException("ACX metadata section must contain exactly one record.");
        }

        int offset = 0;
        long profileId = ReadUInt32(data, ref offset);
        long profileVersionId = ReadUInt32(data, ref offset);
        bool enabled = ReadByte(data, ref offset) != 0;
        bool isPublished = ReadByte(data, ref offset) != 0;
        ProfileSourceKind sourceKind = ParseSourceKind(ReadByte(data, ref offset));
        bool hasSourceCrc32 = ReadByte(data, ref offset) != 0;
        int programNumber = ReadInt32(data, ref offset);
        int versionNumber = ReadInt32(data, ref offset);
        uint schemaVersion = ReadUInt32(data, ref offset);
        ushort profileKeyLength = ReadUInt16(data, ref offset);
        ushort displayNameLength = ReadUInt16(data, ref offset);
        ushort descriptionLength = ReadUInt16(data, ref offset);
        ushort notesLength = ReadUInt16(data, ref offset);
        uint sourceCrc32 = ReadUInt32(data, ref offset);

        string profileKey = ReadUtf8String(data, ref offset, profileKeyLength);
        string displayName = ReadUtf8String(data, ref offset, displayNameLength);
        string description = ReadUtf8String(data, ref offset, descriptionLength);
        string notes = ReadUtf8String(data, ref offset, notesLength);

        EnsureConsumed(data, offset, nameof(ReadMetadataSection));

        return new AcxProfileMetadata(
            ProfileId: profileId,
            ProfileVersionId: profileVersionId,
            ProfileKey: profileKey,
            DisplayName: displayName,
            Description: description,
            Notes: notes,
            ProgramNumber: programNumber,
            VersionNumber: versionNumber,
            SchemaVersion: schemaVersion,
            Enabled: enabled,
            IsPublished: isPublished,
            SourceKind: sourceKind,
            SourceCrc32: hasSourceCrc32 ? sourceCrc32 : null);
    }

    private static HeadInitCommandSequenceModel ReadInitSection(ReadOnlySpan<byte> data, uint recordCount)
    {
        int offset = 0;
        uint phase1Delay = ReadUInt32(data, ref offset);
        uint phaseGap = ReadUInt32(data, ref offset);
        uint phase2Delay = ReadUInt32(data, ref offset);
        ushort phase1Count = ReadUInt16(data, ref offset);
        ushort phase2Count = ReadUInt16(data, ref offset);

        List<HeadInitStepModel> phase1 = new(phase1Count);
        List<HeadInitStepModel> phase2 = new(phase2Count);

        for (int i = 0; i < phase1Count + phase2Count; i++) {
            ushort stepOrder = ReadUInt16(data, ref offset);
            byte phase = ReadByte(data, ref offset);
            byte stepKindValue = ReadByte(data, ref offset);
            byte busValue = ReadByte(data, ref offset);
            byte dlc = ReadByte(data, ref offset);
            ushort rawTextLength = ReadUInt16(data, ref offset);
            uint canId = ReadUInt32(data, ref offset);
            uint waitMs = ReadUInt32(data, ref offset);
            string rawText = ReadUtf8String(data, ref offset, rawTextLength);
            byte[] payload = ReadBytes(data, ref offset, dlc);

            HeadInitStepKind stepKind = ParseStepKind(stepKindValue);
            HeadInitStepModel step = stepKind switch
            {
                HeadInitStepKind.Can => new HeadInitStepModel(
                    Phase: phase,
                    StepOrder: stepOrder,
                    RawText: rawText,
                    StepKind: stepKind,
                    Bus: busValue,
                    CanId: canId,
                    Dlc: dlc,
                    Data: payload,
                    WaitMs: null),
                HeadInitStepKind.Wait => new HeadInitStepModel(
                    Phase: phase,
                    StepOrder: stepOrder,
                    RawText: rawText,
                    StepKind: stepKind,
                    Bus: null,
                    CanId: null,
                    Dlc: null,
                    Data: null,
                    WaitMs: checked((int)waitMs)),
                HeadInitStepKind.Status => new HeadInitStepModel(
                    Phase: phase,
                    StepOrder: stepOrder,
                    RawText: rawText,
                    StepKind: stepKind,
                    Bus: null,
                    CanId: null,
                    Dlc: null,
                    Data: null,
                    WaitMs: null),
                _ => throw new InvalidDataException("Unsupported INIT step kind in ACX package."),
            };

            if (phase == 1) {
                phase1.Add(step);
            } else if (phase == 2) {
                phase2.Add(step);
            } else {
                throw new InvalidDataException($"Invalid INIT phase '{phase}' in ACX package.");
            }
        }

        EnsureConsumed(data, offset, nameof(ReadInitSection));
        if (recordCount != (uint)(phase1.Count + phase2.Count)) {
            throw new InvalidDataException("ACX INIT record count does not match the number of steps.");
        }

        return new HeadInitCommandSequenceModel(phase1Delay, phaseGap, phase2Delay, phase1, phase2);
    }
    private static HeadTesteoCommandProfileModel ReadTesteoSection(ReadOnlySpan<byte> data, uint recordCount)
    {
        if (recordCount != 1) {
            throw new InvalidDataException("ACX TESTEO section must contain exactly one record.");
        }

        int offset = 0;
        uint pingCanId = ReadUInt32(data, ref offset);
        byte pingDlc = ReadByte(data, ref offset);
        uint responseCanId = ReadUInt32(data, ref offset);
        uint resetCanId = ReadUInt32(data, ref offset);
        byte resetDlc = ReadByte(data, ref offset);
        byte successCode = ReadByte(data, ref offset);
        byte missingExpansionCode = ReadByte(data, ref offset);
        byte missingForceCode = ReadByte(data, ref offset);
        byte forceBoard1Code = ReadByte(data, ref offset);
        byte forceBoard2Code = ReadByte(data, ref offset);
        ushort maxTries = ReadUInt16(data, ref offset);
        uint responseTimeoutMs = ReadUInt32(data, ref offset);
        uint retryDelayMs = ReadUInt32(data, ref offset);
        uint resetDebounceMs = ReadUInt32(data, ref offset);
        byte[] pingData = ReadBytes(data, ref offset, pingDlc);
        byte[] resetData = ReadBytes(data, ref offset, resetDlc);
        EnsureConsumed(data, offset, nameof(ReadTesteoSection));

        return new HeadTesteoCommandProfileModel(
            Ping: new HeadCanCommandModel(pingCanId, pingDlc, pingData),
            ResponseCanId: responseCanId,
            ResetCanId: resetCanId,
            Reset: new HeadCanCommandModel(resetCanId, resetDlc, resetData),
            SuccessCode: successCode,
            MissingExpansionCode: missingExpansionCode,
            MissingForceCode: missingForceCode,
            ForceBoard1Code: forceBoard1Code,
            ForceBoard2Code: forceBoard2Code,
            MaxTries: maxTries,
            ResponseTimeoutMs: responseTimeoutMs,
            RetryDelayMs: retryDelayMs,
            ResetDebounceMs: resetDebounceMs);
    }

    private static (HeadMotionCommandProfileModel Den, HeadMotionCommandProfileModel Sic, HeadMotionCommandProfileModel Feet) ReadMotionSection(ReadOnlySpan<byte> data, uint recordCount)
    {
        int offset = 0;
        ushort moduleCount = ReadUInt16(data, ref offset);
        if (moduleCount != 3) {
            throw new InvalidDataException("ACX motion section must contain three modules.");
        }

        HeadMotionCommandProfileModel? den = null;
        HeadMotionCommandProfileModel? sic = null;
        HeadMotionCommandProfileModel? feet = null;

        for (int i = 0; i < moduleCount; i++) {
            byte moduleKindValue = ReadByte(data, ref offset);
            byte opcode = ReadByte(data, ref offset);
            byte motorIndexBase = ReadByte(data, ref offset);
            ReadByte(data, ref offset);
            ushort instanceCount = ReadUInt16(data, ref offset);
            ushort positionCount = ReadUInt16(data, ref offset);
            ushort runSequenceCount = ReadUInt16(data, ref offset);
            ushort alternateRunSequenceCount = ReadUInt16(data, ref offset);
            uint canId = ReadUInt32(data, ref offset);
            uint runPeriodMs = ReadUInt32(data, ref offset);
            uint alternateRunPeriodMs = ReadUInt32(data, ref offset);

            List<ushort> positions = new(positionCount);
            for (int positionIndex = 0; positionIndex < positionCount; positionIndex++) {
                positions.Add(ReadUInt16(data, ref offset));
            }

            List<byte> runSequence = new(runSequenceCount);
            for (int positionIndex = 0; positionIndex < runSequenceCount; positionIndex++) {
                runSequence.Add(ReadByte(data, ref offset));
            }

            List<byte> alternateRunSequence = new(alternateRunSequenceCount);
            for (int positionIndex = 0; positionIndex < alternateRunSequenceCount; positionIndex++) {
                alternateRunSequence.Add(ReadByte(data, ref offset));
            }

            HeadMotionModuleKind moduleKind = ParseMotionModuleKind(moduleKindValue);
            HeadMotionCommandProfileModel module = new(
                ModuleKind: moduleKind,
                CanId: canId,
                Opcode: opcode,
                MotorIndexBase: motorIndexBase,
                InstanceCount: instanceCount,
                RunSequence: runSequence,
                AlternateRunSequence: alternateRunSequence,
                Positions: positions,
                RunPeriodMs: runPeriodMs,
                AlternateRunPeriodMs: alternateRunPeriodMs);

            switch (moduleKind) {
                case HeadMotionModuleKind.Den:
                    den = module;
                    break;
                case HeadMotionModuleKind.Sic:
                    sic = module;
                    break;
                case HeadMotionModuleKind.Feet:
                    feet = module;
                    break;
            }
        }

        EnsureConsumed(data, offset, nameof(ReadMotionSection));
        if (recordCount != 3) {
            throw new InvalidDataException("ACX motion record count does not match the number of modules.");
        }

        return (den ?? throw new InvalidDataException("ACX motion section is missing DEN."),
            sic ?? throw new InvalidDataException("ACX motion section is missing SIC."),
            feet ?? throw new InvalidDataException("ACX motion section is missing FEET."));
    }
    private static HeadJCommandProfileModel ReadJSection(ReadOnlySpan<byte> data, uint recordCount)
    {
        if (recordCount != 1) {
            throw new InvalidDataException("ACX J section must contain exactly one record.");
        }

        int offset = 0;
        uint canId = ReadUInt32(data, ref offset);
        byte opcode = ReadByte(data, ref offset);
        byte instanceIndexBase = ReadByte(data, ref offset);
        ushort instanceCount = ReadUInt16(data, ref offset);
        byte channelCount = ReadByte(data, ref offset);
        byte initialRegister = ReadByte(data, ref offset);
        byte onAllRegister = ReadByte(data, ref offset);
        byte offAllRegister = ReadByte(data, ref offset);
        uint runPeriodMs = ReadUInt32(data, ref offset);
        EnsureConsumed(data, offset, nameof(ReadJSection));

        return new HeadJCommandProfileModel(
            CanId: canId,
            Opcode: opcode,
            InstanceIndexBase: instanceIndexBase,
            InstanceCount: instanceCount,
            ChannelCount: channelCount,
            InitialRegister: initialRegister,
            OnAllRegister: onAllRegister,
            OffAllRegister: offAllRegister,
            RunPeriodMs: runPeriodMs);
    }

    private static (HeadCascadeCommandProfileModel Yarn, HeadCascadeCommandProfileModel Stitch) ReadCascadeSection(ReadOnlySpan<byte> data, uint recordCount)
    {
        int offset = 0;
        ushort moduleCount = ReadUInt16(data, ref offset);
        if (moduleCount != 2) {
            throw new InvalidDataException("ACX cascade section must contain two modules.");
        }

        HeadCascadeCommandProfileModel? yarn = null;
        HeadCascadeCommandProfileModel? stitch = null;

        for (int i = 0; i < moduleCount; i++) {
            byte moduleKindValue = ReadByte(data, ref offset);
            byte opcode = ReadByte(data, ref offset);
            ushort addressesPerInstance = ReadUInt16(data, ref offset);
            ushort instanceCount = ReadUInt16(data, ref offset);
            ushort addressCount = ReadUInt16(data, ref offset);
            uint canId = ReadUInt32(data, ref offset);
            uint runPeriodMs = ReadUInt32(data, ref offset);
            byte onValue = ReadByte(data, ref offset);
            byte offValue = ReadByte(data, ref offset);
            ReadUInt16(data, ref offset);

            List<byte> addresses = new(addressCount);
            for (int addressIndex = 0; addressIndex < addressCount; addressIndex++) {
                addresses.Add(ReadByte(data, ref offset));
            }

            HeadCascadeModuleKind moduleKind = ParseCascadeModuleKind(moduleKindValue);
            HeadCascadeCommandProfileModel module = new(
                ModuleKind: moduleKind,
                CanId: canId,
                Opcode: opcode,
                AddressesPerInstance: checked((byte)addressesPerInstance),
                InstanceCount: instanceCount,
                Addresses: addresses,
                OnValue: onValue,
                OffValue: offValue,
                RunPeriodMs: runPeriodMs);

            switch (moduleKind) {
                case HeadCascadeModuleKind.Yarn:
                    yarn = module;
                    break;
                case HeadCascadeModuleKind.Stitch:
                    stitch = module;
                    break;
            }
        }

        EnsureConsumed(data, offset, nameof(ReadCascadeSection));
        if (recordCount != 2) {
            throw new InvalidDataException("ACX cascade record count does not match the number of modules.");
        }

        return (yarn ?? throw new InvalidDataException("ACX cascade section is missing YARN."),
            stitch ?? throw new InvalidDataException("ACX cascade section is missing STITCH."));
    }
    private static HeadStopCommandProfileModel ReadStopSection(ReadOnlySpan<byte> data, uint recordCount)
    {
        if (recordCount != 1) {
            throw new InvalidDataException("ACX STOP section must contain exactly one record.");
        }

        int offset = 0;
        bool sendsCanFrame = ReadByte(data, ref offset) != 0;
        bool framePresent = ReadByte(data, ref offset) != 0;
        ReadUInt16(data, ref offset);
        uint canId = ReadUInt32(data, ref offset);
        byte dlc = ReadByte(data, ref offset);
        ReadByte(data, ref offset);
        byte[] payload = ReadBytes(data, ref offset, dlc);
        EnsureConsumed(data, offset, nameof(ReadStopSection));

        if (!sendsCanFrame) {
            if (framePresent || dlc != 0 || canId != 0 || payload.Length != 0) {
                throw new InvalidDataException("ACX STOP section is inconsistent with sends_can_frame = 0.");
            }

            return new HeadStopCommandProfileModel(false, null);
        }

        if (!framePresent) {
            throw new InvalidDataException("ACX STOP section is missing the CAN frame payload.");
        }

        return new HeadStopCommandProfileModel(true, new HeadCanCommandModel(canId, dlc, payload));
    }

    private static List<ProfileActionModel> ReadActionsSection(ReadOnlySpan<byte> data, uint recordCount)
    {
        int offset = 0;
        ushort actionCount = ReadUInt16(data, ref offset);
        List<ProfileActionModel> actions = new(actionCount);
        for (int actionIndex = 0; actionIndex < actionCount; actionIndex++) {
            uint actionId = ReadUInt32(data, ref offset);
            ushort stepCount = ReadUInt16(data, ref offset);
            bool enabled = ReadByte(data, ref offset) != 0;
            ReadByte(data, ref offset);
            ushort nameLength = ReadUInt16(data, ref offset);
            ushort categoryLength = ReadUInt16(data, ref offset);
            ReadUInt32(data, ref offset);
            string actionName = ReadUtf8String(data, ref offset, nameLength);
            string category = ReadUtf8String(data, ref offset, categoryLength);
            List<ProfileActionStepModel> steps = new(stepCount);
            for (int stepIndex = 0; stepIndex < stepCount; stepIndex++) {
                ushort stepOrder = ReadUInt16(data, ref offset);
                byte stepKindValue = ReadByte(data, ref offset);
                byte busValue = ReadByte(data, ref offset);
                byte dlc = ReadByte(data, ref offset);
                ReadByte(data, ref offset);
                uint canId = ReadUInt32(data, ref offset);
                uint waitMs = ReadUInt32(data, ref offset);
                ReadUInt16(data, ref offset);
                byte[] payload = ReadBytes(data, ref offset, dlc);

                HeadInitStepKind stepKind = ParseStepKind(stepKindValue);
                ProfileActionStepModel step = stepKind switch
                {
                    HeadInitStepKind.Can => new ProfileActionStepModel(
                        StepOrder: stepOrder,
                        StepKind: stepKind,
                        Bus: busValue,
                        CanId: canId,
                        Dlc: dlc,
                        Data: payload,
                        WaitMs: null),
                    HeadInitStepKind.Wait => new ProfileActionStepModel(
                        StepOrder: stepOrder,
                        StepKind: stepKind,
                        Bus: null,
                        CanId: null,
                        Dlc: null,
                        Data: null,
                        WaitMs: checked((int)waitMs)),
                    HeadInitStepKind.Status => new ProfileActionStepModel(
                        StepOrder: stepOrder,
                        StepKind: stepKind,
                        Bus: null,
                        CanId: null,
                        Dlc: null,
                        Data: null,
                        WaitMs: null),
                    _ => throw new InvalidDataException("Unsupported action step kind in ACX package."),
                };
                steps.Add(step);
            }

            actions.Add(new ProfileActionModel(actionId, actionName, category, enabled, steps));
        }

        EnsureConsumed(data, offset, nameof(ReadActionsSection));
        if (recordCount != actionCount) {
            throw new InvalidDataException("ACX actions record count does not match the number of actions.");
        }

        return actions;
    }

    private static ProfileVersionDocument BuildDocument(
        AcxProfileMetadata metadata,
        HeadInitCommandSequenceModel init,
        HeadTesteoCommandProfileModel testeo,
        HeadMotionCommandProfileModel den,
        HeadMotionCommandProfileModel sic,
        HeadMotionCommandProfileModel feet,
        HeadJCommandProfileModel jProfile,
        HeadCascadeCommandProfileModel yarn,
        HeadCascadeCommandProfileModel stitch,
        HeadStopCommandProfileModel stop,
        IReadOnlyList<ProfileActionModel> actions)
    {

        ProfileVersionSummary version = new(
            Id: metadata.ProfileVersionId,
            ProfileId: metadata.ProfileId,
            VersionNumber: metadata.VersionNumber,
            SchemaVersion: checked((int)metadata.SchemaVersion),
            Notes: metadata.Notes,
            SourceKind: metadata.SourceKind,
            Crc32: metadata.SourceCrc32,
            IsPublished: metadata.IsPublished,
            CreatedUtc: default);

        ProfileRecord profile = new(
            Id: metadata.ProfileId,
            ProfileKey: metadata.ProfileKey,
            ProgramNumber: metadata.ProgramNumber,
            DisplayName: metadata.DisplayName,
            Description: metadata.Description,
            Enabled: metadata.Enabled,
            CreatedUtc: default,
            UpdatedUtc: default,
            Versions: new[] { version });


        HeadCommandProfileModel commands = new(
            ProgramNumber: metadata.ProgramNumber,
            ProgramName: metadata.DisplayName,
            InitSequence: init,
            Testeo: testeo,
            Den: den,
            Sic: sic,
            Feet: feet,
            J: jProfile,
            Yarn: yarn,
            Stitch: stitch,
            Stop: stop);

        return new ProfileVersionDocument(profile, version, commands, actions);
    }
    private static void EnsurePackageSizeWithinLimit(long fileSize)
    {
        if (fileSize > AcxFormatConstants.MaxFileSizeBytes) {
            throw new InvalidDataException($"ACX package exceeds the supported size of {AcxFormatConstants.MaxFileSizeBytes} bytes.");
        }
    }
    private static void EnsureMagic(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length < AcxFormatConstants.MagicBytes.Length) {
            throw new InvalidDataException("ACX package is truncated: magic is incomplete.");
        }

        if (!data.Slice(0, AcxFormatConstants.MagicBytes.Length).SequenceEqual(AcxFormatConstants.MagicBytes)) {
            throw new InvalidDataException("Invalid ACX magic. Expected ACX1.");
        }

        offset += AcxFormatConstants.MagicBytes.Length;
    }

    private static void EnsureConsumed(ReadOnlySpan<byte> data, int offset, string sectionName)
    {
        if (offset != data.Length) {
            throw new InvalidDataException($"ACX section '{sectionName}' has trailing bytes.");
        }
    }

    private static byte[] ReadBytes(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (count < 0 || offset + count > data.Length) {
            throw new InvalidDataException("ACX package is truncated while reading a section payload.");
        }

        byte[] result = data.Slice(offset, count).ToArray();
        offset += count;
        return result;
    }

    private static string ReadUtf8String(ReadOnlySpan<byte> data, ref int offset, int byteCount)
    {
        byte[] bytes = ReadBytes(data, ref offset, byteCount);
        return Utf8.GetString(bytes);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 2 > data.Length) {
            throw new InvalidDataException("ACX package is truncated while reading a uint16.");
        }

        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 4 > data.Length) {
            throw new InvalidDataException("ACX package is truncated while reading a uint32.");
        }

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        return unchecked((int)ReadUInt32(data, ref offset));
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length) {
            throw new InvalidDataException("ACX package is truncated while reading a byte.");
        }

        return data[offset++];
    }

    private static ProfileSourceKind ParseSourceKind(byte value)
    {
        return value switch
        {
            1 => ProfileSourceKind.Sqlite,
            2 => ProfileSourceKind.CppImport,
            3 => ProfileSourceKind.Manual,
            _ => throw new InvalidDataException($"Unsupported ACX source kind '{value}'."),
        };
    }

    private static HeadInitStepKind ParseStepKind(byte value)
    {
        return value switch
        {
            1 => HeadInitStepKind.Can,
            2 => HeadInitStepKind.Wait,
            3 => HeadInitStepKind.Status,
            _ => throw new InvalidDataException($"Unsupported ACX step kind '{value}'."),
        };
    }

    private static HeadMotionModuleKind ParseMotionModuleKind(byte value)
    {
        return value switch
        {
            1 => HeadMotionModuleKind.Den,
            2 => HeadMotionModuleKind.Sic,
            3 => HeadMotionModuleKind.Feet,
            _ => throw new InvalidDataException($"Unsupported ACX motion module kind '{value}'."),
        };
    }

    private static HeadCascadeModuleKind ParseCascadeModuleKind(byte value)
    {
        return value switch
        {
            1 => HeadCascadeModuleKind.Yarn,
            2 => HeadCascadeModuleKind.Stitch,
            _ => throw new InvalidDataException($"Unsupported ACX cascade module kind '{value}'."),
        };
    }
}
