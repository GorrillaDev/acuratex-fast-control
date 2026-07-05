using AcuratexControlApp.Models.Profiles;

namespace AcuratexControlApp.Data.Sqlite.Importers;

public sealed record InitialProfileSeed(ProfileCreateRequest CreateRequest, ProfileVersionWriteRequest VersionRequest);

public static class InitialProfileSeeds
{
    private const string SharedInitPhase1 = """
    320 07
    WAIT 2000
    320 30
    WAIT 2000
    370 fd 06 10 00
    WAIT 2000
    370 fd 06 11 00
    WAIT 2000
    320 2d 00 bf ff
    WAIT 2000
    320 00
    320 07
    320 07
    320 07
    320 07
    320 02
    320 07
    320 25 07
    320 05
    320 19 04
    320 1a 19
    320 4d 18 0d 00
    320 4d 19 0d 00
    320 2b 00 03
    320 2c 00 03
    320 43 00
    320 2d 00 bf ff
    320 4c 02 32 00
    320 5a 08 b0 04
    320 5a 09 b0 04
    320 48 00 01 00
    320 00
    320 53 00 ff 03
    320 38 00 5a 01
    320 54 00
    320 58 00 00
    320 54 01
    320 58 01 00
    320 54 02
    320 58 02 00
    320 54 03
    320 58 03 00
    320 54 04
    320 58 04 00
    320 54 05
    320 58 05 00
    320 54 06
    320 58 06 00
    320 54 07
    320 58 07 00
    320 54 08
    320 58 08 00
    320 54 09
    320 58 09 00
    320 07
    320 1e 18 01
    320 1e 19 01
    320 1e 1a 01
    320 1e 1b 01
    320 1e 1c 01
    320 1e 1d 01
    320 1e 1e 01
    320 1e 1f 01
    320 1e 20 01
    320 1e 21 01
    320 1e 22 01
    320 1e 23 01
    320 1e 24 01
    320 1e 25 01
    320 1e 26 01
    320 1e 27 01
    """;

    private const string SharedInitPhase2 = """
    320 30
    WAIT 2000
    320 30
    WAIT 2000
    320 0d 00
    320 0e 00
    320 0c 00
    320 0e 01
    320 0d 01
    320 0d 02
    320 0e 02
    320 0c 01
    320 0e 03
    320 0d 03
    320 0d 04
    320 0e 04
    320 0c 02
    320 0e 05
    320 0d 05
    320 0d 06
    320 0e 06
    320 0c 03
    320 0e 07
    320 0d 07
    320 09
    320 26 01
    320 26 00
    320 09
    320 26 02
    320 26 03
    320 0b
    320 54 00
    320 54 01
    320 54 02
    320 54 03
    320 54 04
    320 54 05
    320 54 06
    320 54 07
    320 54 08
    320 54 09
    320 1c 00 08 00
    WAIT 200
    320 1c 00 00 00
    WAIT 200
    320 07
    320 0e 00
    320 1c 01 08 00
    WAIT 200
    320 1c 01 00 00
    WAIT 200
    320 07
    320 0e 01
    320 1c 02 08 00
    WAIT 200
    320 1c 02 00 00
    WAIT 200
    320 07
    320 0e 02
    320 1c 03 08 00
    WAIT 200
    320 1c 03 00 00
    WAIT 200
    320 07
    320 0e 03
    320 1c 04 08 00
    WAIT 200
    320 1c 04 00 00
    WAIT 200
    320 07
    320 0e 04
    320 1c 05 08 00
    WAIT 200
    320 1c 05 00 00
    WAIT 200
    320 07
    320 0e 05
    320 1c 06 08 00
    WAIT 200
    320 1c 06 00 00
    WAIT 200
    320 07
    320 0e 06
    320 1c 07 08 00
    WAIT 200
    320 1c 07 00 00
    WAIT 200
    320 07
    320 0e 07
    320 1c 08 08 00
    WAIT 200
    320 1c 08 00 00
    WAIT 200
    320 07
    320 0e 08
    320 1c 09 08 00
    WAIT 200
    320 1c 09 00 00
    WAIT 200
    320 07
    320 0e 09
    """;

    public static IReadOnlyList<InitialProfileSeed> All { get; } = new[]
    {
        CreateProgram1(),
        CreateProgram2(),
    };

    public static InitialProfileSeed Program1 => CreateProgram1();

    public static InitialProfileSeed Program2 => CreateProgram2();

    private static InitialProfileSeed CreateProgram1()
    {
        HeadCommandProfileModel commands = new(
            ProgramNumber: 1,
            ProgramName: "Programa 1",
            InitSequence: HeadInitCommandSequenceModel.FromRawText(SharedInitPhase1, 80, 5000, SharedInitPhase2, 200),
            Testeo: new HeadTesteoCommandProfileModel(
                Ping: HeadCanCommandModel.Create(0x320, 0x07),
                ResponseCanId: 0x700,
                ResetCanId: 0x702,
                Reset: HeadCanCommandModel.Create(0x702, 0x3F, 0x00),
                SuccessCode: 0xCB,
                MissingExpansionCode: 0xBC,
                MissingForceCode: 0xBF,
                ForceBoard1Code: 0xA1,
                ForceBoard2Code: 0xA2,
                MaxTries: 25,
                ResponseTimeoutMs: 300,
                RetryDelayMs: 60,
                ResetDebounceMs: 250),
            Den: CreateMotionProfile(
                HeadMotionModuleKind.Den,
                canId: 0x320,
                opcode: 0x1C,
                motorIndexBase: 0x00,
                instanceCount: 8,
                runSequence: new byte[] { 1, 3, 5, 2, 4 },
                alternateRunSequence: new byte[] { 1, 3, 5 },
                positions: new ushort[] { 0x0000, 0x00A2, 0x0145, 0x01E7, 0x028A },
                runPeriodMs: 80,
                alternateRunPeriodMs: 300),
            Sic: CreateMotionProfile(
                HeadMotionModuleKind.Sic,
                canId: 0x320,
                opcode: 0x1C,
                motorIndexBase: 0x08,
                instanceCount: 2,
                runSequence: new byte[] { 1, 2, 3 },
                alternateRunSequence: Array.Empty<byte>(),
                positions: new ushort[] { 0x0000, 0x0176, 0x02EE },
                runPeriodMs: 300,
                alternateRunPeriodMs: 0),
            Feet: CreateEmptyMotionProfile(HeadMotionModuleKind.Feet, 0x320, 0x1C, 0x08),
            J: new HeadJCommandProfileModel(
                CanId: 0x320,
                Opcode: 0x1D,
                InstanceIndexBase: 0x00,
                InstanceCount: 8,
                ChannelCount: 8,
                InitialRegister: 0xFF,
                OnAllRegister: 0x00,
                OffAllRegister: 0xFF,
                RunPeriodMs: 80),
            Yarn: CreateCascadeProfile(
                HeadCascadeModuleKind.Yarn,
                canId: 0x320,
                opcode: 0x1E,
                addressesPerInstance: 8,
                instanceCount: 2,
                addresses: new byte[]
                {
                    0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
                    0x24, 0x25, 0x26, 0x27, 0x20, 0x21, 0x22, 0x23,
                },
                onValue: 0x01,
                offValue: 0x00,
                runPeriodMs: 80),
            Stitch: CreateCascadeProfile(
                HeadCascadeModuleKind.Stitch,
                canId: 0x320,
                opcode: 0x1E,
                addressesPerInstance: 4,
                instanceCount: 4,
                addresses: new byte[]
                {
                    0x00, 0x01, 0x02, 0x05,
                    0x06, 0x07, 0x08, 0x0B,
                    0x0C, 0x0D, 0x0E, 0x11,
                    0x12, 0x13, 0x14, 0x17,
                },
                onValue: 0x01,
                offValue: 0x00,
                runPeriodMs: 80),
            Stop: new HeadStopCommandProfileModel(false, null));

        return new InitialProfileSeed(
            new ProfileCreateRequest("programa-1", 1, "Programa 1", "Importado desde kProgram1Commands.", true),
            new ProfileVersionWriteRequest(1, 1, "CPP_IMPORT inicial de kProgram1Commands.", ProfileSourceKind.CppImport, null, true, commands, Array.Empty<ProfileActionModel>()));
    }

    private static InitialProfileSeed CreateProgram2()
    {
        HeadCommandProfileModel commands = new(
            ProgramNumber: 2,
            ProgramName: "Programa 2",
            InitSequence: HeadInitCommandSequenceModel.FromRawText(SharedInitPhase1, 80, 5000, SharedInitPhase2, 200),
            Testeo: new HeadTesteoCommandProfileModel(
                Ping: HeadCanCommandModel.Create(0x320, 0x07),
                ResponseCanId: 0x700,
                ResetCanId: 0x702,
                Reset: HeadCanCommandModel.Create(0x702, 0x3F, 0x00),
                SuccessCode: 0xCB,
                MissingExpansionCode: 0xBC,
                MissingForceCode: 0xBF,
                ForceBoard1Code: 0xA1,
                ForceBoard2Code: 0xA2,
                MaxTries: 25,
                ResponseTimeoutMs: 300,
                RetryDelayMs: 60,
                ResetDebounceMs: 250),
            Den: CreateMotionProfile(
                HeadMotionModuleKind.Den,
                canId: 0x320,
                opcode: 0x1C,
                motorIndexBase: 0x00,
                instanceCount: 8,
                runSequence: new byte[] { 1, 3, 5, 2, 4 },
                alternateRunSequence: new byte[] { 1, 3, 5 },
                positions: new ushort[] { 0x0000, 0x00A2, 0x0145, 0x01E7, 0x028A },
                runPeriodMs: 80,
                alternateRunPeriodMs: 300),
            Sic: CreateMotionProfile(
                HeadMotionModuleKind.Sic,
                canId: 0x320,
                opcode: 0x1C,
                motorIndexBase: 0x08,
                instanceCount: 2,
                runSequence: new byte[] { 1, 2, 3 },
                alternateRunSequence: Array.Empty<byte>(),
                positions: new ushort[] { 0x0000, 0x0176, 0x02EE },
                runPeriodMs: 300,
                alternateRunPeriodMs: 0),
            Feet: CreateMotionProfile(
                HeadMotionModuleKind.Feet,
                canId: 0x320,
                opcode: 0x1C,
                motorIndexBase: 0x08,
                instanceCount: 2,
                runSequence: new byte[] { 1, 2 },
                alternateRunSequence: Array.Empty<byte>(),
                positions: new ushort[] { 0x0000, 0x0176 },
                runPeriodMs: 300,
                alternateRunPeriodMs: 0),
            J: new HeadJCommandProfileModel(
                CanId: 0x320,
                Opcode: 0x1D,
                InstanceIndexBase: 0x00,
                InstanceCount: 8,
                ChannelCount: 8,
                InitialRegister: 0xFF,
                OnAllRegister: 0x00,
                OffAllRegister: 0xFF,
                RunPeriodMs: 80),
            Yarn: CreateCascadeProfile(
                HeadCascadeModuleKind.Yarn,
                canId: 0x320,
                opcode: 0x1E,
                addressesPerInstance: 8,
                instanceCount: 2,
                addresses: new byte[]
                {
                    0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
                    0x24, 0x25, 0x26, 0x27, 0x20, 0x21, 0x22, 0x23,
                },
                onValue: 0x01,
                offValue: 0x00,
                runPeriodMs: 80),
            Stitch: CreateCascadeProfile(
                HeadCascadeModuleKind.Stitch,
                canId: 0x320,
                opcode: 0x1E,
                addressesPerInstance: 4,
                instanceCount: 4,
                addresses: new byte[]
                {
                    0x00, 0x01, 0x02, 0x05,
                    0x06, 0x07, 0x08, 0x0B,
                    0x0C, 0x0D, 0x0E, 0x11,
                    0x12, 0x13, 0x14, 0x17,
                },
                onValue: 0x01,
                offValue: 0x00,
                runPeriodMs: 80),
            Stop: new HeadStopCommandProfileModel(false, null));

        return new InitialProfileSeed(
            new ProfileCreateRequest("programa-2", 2, "Programa 2", "Importado desde kProgram2Commands.", true),
            new ProfileVersionWriteRequest(1, 1, "CPP_IMPORT inicial de kProgram2Commands.", ProfileSourceKind.CppImport, null, true, commands, Array.Empty<ProfileActionModel>()));
    }

    private static HeadMotionCommandProfileModel CreateMotionProfile(
        HeadMotionModuleKind moduleKind,
        uint canId,
        byte opcode,
        byte motorIndexBase,
        int instanceCount,
        IReadOnlyList<byte> runSequence,
        IReadOnlyList<byte> alternateRunSequence,
        IReadOnlyList<ushort> positions,
        uint runPeriodMs,
        uint alternateRunPeriodMs)
    {
        return new HeadMotionCommandProfileModel(
            moduleKind,
            canId,
            opcode,
            motorIndexBase,
            instanceCount,
            runSequence.ToArray(),
            alternateRunSequence.ToArray(),
            positions.ToArray(),
            runPeriodMs,
            alternateRunPeriodMs);
    }

    private static HeadMotionCommandProfileModel CreateEmptyMotionProfile(
        HeadMotionModuleKind moduleKind,
        uint canId,
        byte opcode,
        byte motorIndexBase)
    {
        return new HeadMotionCommandProfileModel(
            moduleKind,
            canId,
            opcode,
            motorIndexBase,
            0,
            Array.Empty<byte>(),
            Array.Empty<byte>(),
            Array.Empty<ushort>(),
            0,
            0);
    }

    private static HeadCascadeCommandProfileModel CreateCascadeProfile(
        HeadCascadeModuleKind moduleKind,
        uint canId,
        byte opcode,
        byte addressesPerInstance,
        int instanceCount,
        IReadOnlyList<byte> addresses,
        byte onValue,
        byte offValue,
        uint runPeriodMs)
    {
        return new HeadCascadeCommandProfileModel(
            moduleKind,
            canId,
            opcode,
            addressesPerInstance,
            instanceCount,
            addresses.ToArray(),
            onValue,
            offValue,
            runPeriodMs);
    }
}
