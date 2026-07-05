using AcuratexControlApp.Data.Sqlite;
using AcuratexControlApp.Data.Sqlite.Importers;
using AcuratexControlApp.Models.Profiles;
using AcuratexControlApp.Repositories.Profiles;
using AcuratexControlApp.Services.Profiles;
using AcuratexControlApp.Services.Profiles.Acx;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AcuratexControlApp.Tests;

public sealed class ProfileAcxExportTests
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    public static TheoryData<InitialProfileSeed> Seeds => new()
    {
        InitialProfileSeeds.Program1,
        InitialProfileSeeds.Program2,
    };

    [Fact]
    public async Task Program1ExportsAreDeterministicAndHaveStablePayloadCrc()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        await AssertDeterministicExportAsync(
            harness,
            original,
            exportService => exportService.ExportProfileVersionAsync(original.Version.Id),
            expectedPackageSizeBytes: 5471,
            expectedPayloadCrc32: 0x5CAE00B7U);
    }

    [Fact]
    public async Task Program2ExportsAreDeterministicAndHaveStablePayloadCrc()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program2.CreateRequest.ProfileKey);
        await AssertDeterministicExportAsync(
            harness,
            original,
            exportService => exportService.ExportLatestProfileAsync(InitialProfileSeeds.Program2.CreateRequest.ProfileKey),
            expectedPackageSizeBytes: 5477,
            expectedPayloadCrc32: 0x2CFB7499U);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void CompilerProducesDeterministicBytes(InitialProfileSeed seed)
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, seed.CreateRequest.ProfileKey);
        AcxProfileCompiler compiler = new(harness.Validator);

        AcxCompilationResult first = compiler.Compile(original);
        AcxCompilationResult second = compiler.Compile(original);

        AssertBytewiseEqual(first.PackageBytes, second.PackageBytes);
        Assert.Equal(first.PackageBytes.Length, first.PackageSizeBytes);
        Assert.Equal(second.PackageBytes.Length, second.PackageSizeBytes);
        Assert.Equal(first.PackageBytes.Length, second.PackageBytes.Length);

        AcxProfilePackage firstPackage = new AcxProfilePackageReader().Read(first.PackageBytes);
        AcxProfilePackage secondPackage = new AcxProfilePackageReader().Read(second.PackageBytes);
        AssertProfileVersionDocumentsEqual(original, firstPackage.Document);
        AssertProfileVersionDocumentsEqual(original, secondPackage.Document);

        uint firstComputedCrc = ComputePayloadCrc32(first.PackageBytes, firstPackage.Header);
        uint secondComputedCrc = ComputePayloadCrc32(second.PackageBytes, secondPackage.Header);

        Assert.Equal(firstComputedCrc, firstPackage.Header.PayloadCrc32);
        Assert.Equal(secondComputedCrc, secondPackage.Header.PayloadCrc32);
        Assert.Equal(firstComputedCrc, first.PayloadCrc32);
        Assert.Equal(secondComputedCrc, second.PayloadCrc32);
    }

    [Fact]
    public void ReaderRejectsCorruptedPayloadByte()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxCompilationResult compiled = new AcxProfileCompiler(harness.Validator).Compile(original);
        byte[] corrupted = (byte[])compiled.PackageBytes.Clone();
        corrupted[(int)compiled.Header.PayloadOffset] ^= 0xFF;

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => new AcxProfilePackageReader().Read(corrupted));
        Assert.Contains("CRC mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsTruncatedPackage()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxCompilationResult compiled = new AcxProfileCompiler(harness.Validator).Compile(original);
        byte[] truncated = compiled.PackageBytes.Take(compiled.PackageBytes.Length - 1).ToArray();

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => new AcxProfilePackageReader().Read(truncated));
        Assert.Contains("size mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsInvalidMagic()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxCompilationResult compiled = new AcxProfileCompiler(harness.Validator).Compile(original);
        byte[] corrupted = (byte[])compiled.PackageBytes.Clone();
        corrupted[0] = (byte)'B';

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => new AcxProfilePackageReader().Read(corrupted));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsUnsupportedFormatVersion()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        AcxCompilationResult compiled = new AcxProfileCompiler(harness.Validator).Compile(original);
        byte[] corrupted = (byte[])compiled.PackageBytes.Clone();
        corrupted[4] = 0x02;
        corrupted[5] = 0x00;

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => new AcxProfilePackageReader().Read(corrupted));
        Assert.Contains("format version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedExportDoesNotRegisterPackageAndCleansTempFile()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        string finalPath = Path.Combine(harness.Options.ExportDirectory, AcxFormatConstants.BuildFileName(original.Profile.ProfileKey, original.Version.VersionNumber));
        string tempPath = finalPath + ".tmp";
        int beforeCount = CountExportedPackages(harness, original.Version.Id);

        AcxProfileExportService exportService = CreateExportService(harness, new CorruptingAcxProfileCompiler(new AcxProfileCompiler(harness.Validator)));
        await Assert.ThrowsAsync<InvalidDataException>(() => exportService.ExportProfileVersionAsync(original.Version.Id));

        Assert.Equal(beforeCount, CountExportedPackages(harness, original.Version.Id));
        Assert.False(File.Exists(finalPath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task SyntheticProfileWithActionsRoundTripsExactly()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = CreateSyntheticProfileWithActions(harness);
        ProfileVersionDocument recovered = harness.Repository.ReadVersion(original.Version.Id);
        AssertProfileVersionDocumentsEqual(original, recovered);
        Assert.Equal(original.Profile.CreatedUtc, recovered.Profile.CreatedUtc);
        Assert.Equal(original.Profile.UpdatedUtc, recovered.Profile.UpdatedUtc);
        Assert.Equal(original.Version.CreatedUtc, recovered.Version.CreatedUtc);
        Assert.Equal(original.Profile.Versions.Count, recovered.Profile.Versions.Count);

        AcxProfileExportService exportService = CreateExportService(harness);
        AcxCompilationResult result = await exportService.ExportLatestProfileAsync(original.Profile.ProfileKey);
        Assert.NotNull(result.FilePath);
        Assert.True(File.Exists(result.FilePath));

        AcxProfilePackage package = new AcxProfilePackageReader().Read(result.FilePath!);

        AssertProfileVersionDocumentsEqual(original, package.Document);
        Assert.Equal(result.PayloadCrc32, package.Header.PayloadCrc32);
        Assert.Equal((uint)result.PackageSizeBytes, package.Header.FileSize);
        AssertPackageLayoutWithinBounds(package);
        Assert.Equal(1, CountExportedPackages(harness, original.Version.Id));
    }

    [Fact]
    public void CompilerRejectsPackagesLargerThanOneMibBeforeBuildingSections()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileVersionDocument original = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey);
        ProfileVersionDocument oversized = original with { Actions = CreateOversizedActions() };

        AcxCompilationException ex = Assert.Throws<AcxCompilationException>(() => new AcxProfileCompiler(harness.Validator).Compile(oversized));
        Assert.Contains("supported size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsPackagesLargerThanOneMibBeforeParsingDirectory()
    {
        byte[] oversizedBytes = new byte[AcxFormatConstants.MaxFileSizeBytes + 1];
        InvalidDataException bytesException = Assert.Throws<InvalidDataException>(() => new AcxProfilePackageReader().Read(oversizedBytes));
        Assert.Contains("supported size", bytesException.Message, StringComparison.OrdinalIgnoreCase);

        string tempPath = Path.Combine(Path.GetTempPath(), $"AcuratexFastControl.AcxSizeTests_{Guid.NewGuid():N}.acx");
        try {
            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.SetLength(AcxFormatConstants.MaxFileSizeBytes + 1L);
            }

            InvalidDataException pathException = Assert.Throws<InvalidDataException>(() => new AcxProfilePackageReader().Read(tempPath));
            Assert.Contains("supported size", pathException.Message, StringComparison.OrdinalIgnoreCase);
        } finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task MissingProfileKeyAndVersionAreRejected()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        AcxProfileExportService exportService = CreateExportService(harness);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exportService.ExportLatestProfileAsync("missing-profile"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => exportService.ExportProfileVersionAsync(long.MaxValue));
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

    private static ProfileVersionDocument CreateSyntheticProfileWithActions(ProfileTestHarness harness)
    {
        ProfileRecord profile = harness.Repository.CreateProfile(new ProfileCreateRequest(
            ProfileKey: "synthetic-acx",
            ProgramNumber: 1,
            DisplayName: "Synthetic ACX",
            Description: "Synthetic profile used to verify ACX actions.",
            Enabled: true));

        HeadCommandProfileModel commands = LoadSeedVersion(harness, InitialProfileSeeds.Program1.CreateRequest.ProfileKey).Commands with
        {
            ProgramName = "Synthetic ACX",
        };

        ProfileVersionDocument version = harness.Repository.SaveNewVersion(profile.Id, new ProfileVersionWriteRequest(
            VersionNumber: 1,
            SchemaVersion: 1,
            Notes: "Synthetic ACX test profile.",
            SourceKind: ProfileSourceKind.Manual,
            Crc32: null,
            IsPublished: false,
            Commands: commands,
            Actions: CreateSyntheticActions()));

        return version;
    }

    private static IReadOnlyList<ProfileActionModel> CreateSyntheticActions()
    {
        return new[]
        {
            new ProfileActionModel(
                Id: 0,
                ActionName: "alpha-action",
                Category: "synthetic",
                Enabled: true,
                Steps: new[]
                {
                    new ProfileActionStepModel(0, HeadInitStepKind.Can, 1, 0x123U, 2, new byte[] { 0x11, 0x22 }, null),
                    new ProfileActionStepModel(1, HeadInitStepKind.Wait, null, null, null, null, 250),
                    new ProfileActionStepModel(2, HeadInitStepKind.Status, null, null, null, null, null),
                    new ProfileActionStepModel(3, HeadInitStepKind.Can, 2, 0x456U, 3, new byte[] { 0x33, 0x44, 0x55 }, null),
                }),
        };
    }

    private static async Task AssertDeterministicExportAsync(
        ProfileTestHarness harness,
        ProfileVersionDocument original,
        Func<AcxProfileExportService, Task<AcxCompilationResult>> exportAsync,
        int expectedPackageSizeBytes,
        uint expectedPayloadCrc32)
    {
        AcxProfileExportService exportService = CreateExportService(harness);

        AcxCompilationResult first = await exportAsync(exportService);
        AcxCompilationResult second = await exportAsync(exportService);

        Assert.NotNull(first.FilePath);
        Assert.NotNull(second.FilePath);
        Assert.Equal(first.FilePath, second.FilePath);
        Assert.True(File.Exists(first.FilePath!));
        Assert.True(File.Exists(second.FilePath!));

        AssertBytewiseEqual(first.PackageBytes, second.PackageBytes);
        Assert.Equal(expectedPackageSizeBytes, first.PackageSizeBytes);
        Assert.Equal(expectedPackageSizeBytes, second.PackageSizeBytes);
        Assert.Equal(expectedPackageSizeBytes, first.PackageBytes.Length);
        Assert.Equal(expectedPackageSizeBytes, second.PackageBytes.Length);

        AcxProfilePackage firstPackage = new AcxProfilePackageReader().Read(first.PackageBytes);
        AcxProfilePackage secondPackage = new AcxProfilePackageReader().Read(second.PackageBytes);
        AssertProfileVersionDocumentsEqual(original, firstPackage.Document);
        AssertProfileVersionDocumentsEqual(original, secondPackage.Document);

        uint firstComputedCrc = ComputePayloadCrc32(first.PackageBytes, firstPackage.Header);
        uint secondComputedCrc = ComputePayloadCrc32(second.PackageBytes, secondPackage.Header);
        Assert.Equal(expectedPayloadCrc32, firstComputedCrc);
        Assert.Equal(expectedPayloadCrc32, secondComputedCrc);
        Assert.Equal(firstComputedCrc, firstPackage.Header.PayloadCrc32);
        Assert.Equal(secondComputedCrc, secondPackage.Header.PayloadCrc32);
        Assert.Equal(expectedPayloadCrc32, first.PayloadCrc32);
        Assert.Equal(expectedPayloadCrc32, second.PayloadCrc32);
        Assert.Equal((uint)expectedPackageSizeBytes, firstPackage.Header.FileSize);
        Assert.Equal((uint)expectedPackageSizeBytes, secondPackage.Header.FileSize);
        AssertPackageLayoutWithinBounds(firstPackage);
        AssertPackageLayoutWithinBounds(secondPackage);
        Assert.Equal(2, CountExportedPackages(harness, original.Version.Id));
        Assert.False(File.Exists(first.FilePath! + ".tmp"));
        Assert.False(File.Exists(second.FilePath! + ".tmp"));
    }

    private static void AssertBytewiseEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++) {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    private static uint ComputePayloadCrc32(byte[] packageBytes, AcxPackageHeader header)
    {
        int payloadOffset = checked((int)header.PayloadOffset);
        int payloadSize = checked((int)header.PayloadSize);
        return ComputeCrc32(packageBytes.AsSpan(payloadOffset, payloadSize));
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFU;
        for (int i = 0; i < data.Length; i++) {
            crc = Crc32Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] BuildCrc32Table()
    {
        const uint Polynomial = 0xEDB88320U;
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++) {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++) {
                crc = (crc & 1U) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    private static IReadOnlyList<ProfileActionModel> CreateOversizedActions()
    {
        IReadOnlyList<ProfileActionStepModel> steps = CreateOversizedActionSteps();
        string oversizedCategory = new('x', 8192);
        ProfileActionModel[] actions = new ProfileActionModel[128];
        for (int i = 0; i < actions.Length; i++) {
            actions[i] = new ProfileActionModel(
                Id: i + 1,
                ActionName: $"bulk-{i:000}",
                Category: oversizedCategory,
                Enabled: true,
                Steps: steps);
        }

        return actions;
    }

    private static IReadOnlyList<ProfileActionStepModel> CreateOversizedActionSteps()
    {
        byte[] payload = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        ProfileActionStepModel[] steps = new ProfileActionStepModel[6];
        for (int i = 0; i < steps.Length; i++) {
            steps[i] = new ProfileActionStepModel(
                StepOrder: i,
                StepKind: HeadInitStepKind.Can,
                Bus: 1,
                CanId: 0x123U + (uint)i,
                Dlc: 8,
                Data: payload,
                WaitMs: null);
        }

        return steps;
    }

    private static int CountExportedPackages(ProfileTestHarness harness, long profileVersionId)
    {
        using SqliteConnection connection = harness.ConnectionFactory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM exported_packages WHERE profile_version_id = $profile_version_id;";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void AssertProfileVersionDocumentsEqual(ProfileVersionDocument expected, ProfileVersionDocument actual)
    {
        Assert.Equal(expected.Profile.Id, actual.Profile.Id);
        Assert.Equal(expected.Profile.ProfileKey, actual.Profile.ProfileKey);
        Assert.Equal(expected.Profile.ProgramNumber, actual.Profile.ProgramNumber);
        Assert.Equal(expected.Profile.DisplayName, actual.Profile.DisplayName);
        Assert.Equal(expected.Profile.Description, actual.Profile.Description);
        Assert.Equal(expected.Profile.Enabled, actual.Profile.Enabled);
        Assert.Equal(expected.Profile.Versions.Count, actual.Profile.Versions.Count);

        Assert.Equal(expected.Version.Id, actual.Version.Id);
        Assert.Equal(expected.Version.ProfileId, actual.Version.ProfileId);
        Assert.Equal(expected.Version.VersionNumber, actual.Version.VersionNumber);
        Assert.Equal(expected.Version.SchemaVersion, actual.Version.SchemaVersion);
        Assert.Equal(expected.Version.Notes, actual.Version.Notes);
        Assert.Equal(expected.Version.SourceKind, actual.Version.SourceKind);
        Assert.Equal(expected.Version.Crc32, actual.Version.Crc32);
        Assert.Equal(expected.Version.IsPublished, actual.Version.IsPublished);

        Assert.Equal(expected.Commands.ProgramNumber, actual.Commands.ProgramNumber);
        Assert.Equal(expected.Commands.ProgramName, actual.Commands.ProgramName);
        AssertInitSequenceEqual(expected.Commands.InitSequence, actual.Commands.InitSequence);
        AssertCanEqual(expected.Commands.Testeo.Ping, actual.Commands.Testeo.Ping);
        Assert.Equal(expected.Commands.Testeo.ResponseCanId, actual.Commands.Testeo.ResponseCanId);
        Assert.Equal(expected.Commands.Testeo.ResetCanId, actual.Commands.Testeo.ResetCanId);
        AssertCanEqual(expected.Commands.Testeo.Reset, actual.Commands.Testeo.Reset);
        Assert.Equal(expected.Commands.Testeo.SuccessCode, actual.Commands.Testeo.SuccessCode);
        Assert.Equal(expected.Commands.Testeo.MissingExpansionCode, actual.Commands.Testeo.MissingExpansionCode);
        Assert.Equal(expected.Commands.Testeo.MissingForceCode, actual.Commands.Testeo.MissingForceCode);
        Assert.Equal(expected.Commands.Testeo.ForceBoard1Code, actual.Commands.Testeo.ForceBoard1Code);
        Assert.Equal(expected.Commands.Testeo.ForceBoard2Code, actual.Commands.Testeo.ForceBoard2Code);
        Assert.Equal(expected.Commands.Testeo.MaxTries, actual.Commands.Testeo.MaxTries);
        Assert.Equal(expected.Commands.Testeo.ResponseTimeoutMs, actual.Commands.Testeo.ResponseTimeoutMs);
        Assert.Equal(expected.Commands.Testeo.RetryDelayMs, actual.Commands.Testeo.RetryDelayMs);
        Assert.Equal(expected.Commands.Testeo.ResetDebounceMs, actual.Commands.Testeo.ResetDebounceMs);
        AssertMotionEqual(expected.Commands.Den, actual.Commands.Den);
        AssertMotionEqual(expected.Commands.Sic, actual.Commands.Sic);
        AssertMotionEqual(expected.Commands.Feet, actual.Commands.Feet);
        Assert.Equal(expected.Commands.J, actual.Commands.J);
        AssertCascadeEqual(expected.Commands.Yarn, actual.Commands.Yarn);
        AssertCascadeEqual(expected.Commands.Stitch, actual.Commands.Stitch);
        AssertStopEqual(expected.Commands.Stop, actual.Commands.Stop);

        Assert.Equal(expected.Actions.Count, actual.Actions.Count);
        for (int i = 0; i < expected.Actions.Count; i++) {
            AssertActionEqual(expected.Actions[i], actual.Actions[i]);
        }
    }

    private static void AssertInitSequenceEqual(HeadInitCommandSequenceModel expected, HeadInitCommandSequenceModel actual)
    {
        Assert.Equal(expected.Phase1StepDelayMs, actual.Phase1StepDelayMs);
        Assert.Equal(expected.PhaseGapMs, actual.PhaseGapMs);
        Assert.Equal(expected.Phase2StepDelayMs, actual.Phase2StepDelayMs);
        Assert.Equal(expected.Phase1Steps.Count, actual.Phase1Steps.Count);
        Assert.Equal(expected.Phase2Steps.Count, actual.Phase2Steps.Count);

        for (int i = 0; i < expected.Phase1Steps.Count; i++) {
            AssertInitStepEqual(expected.Phase1Steps[i], actual.Phase1Steps[i]);
        }

        for (int i = 0; i < expected.Phase2Steps.Count; i++) {
            AssertInitStepEqual(expected.Phase2Steps[i], actual.Phase2Steps[i]);
        }
    }

    private static void AssertInitStepEqual(HeadInitStepModel expected, HeadInitStepModel actual)
    {
        Assert.Equal(expected.Phase, actual.Phase);
        Assert.Equal(expected.StepOrder, actual.StepOrder);
        Assert.Equal(expected.RawText, actual.RawText);
        Assert.Equal(expected.StepKind, actual.StepKind);
        Assert.Equal(expected.Bus, actual.Bus);
        Assert.Equal(expected.CanId, actual.CanId);
        Assert.Equal(expected.Dlc, actual.Dlc);
        Assert.Equal(expected.Data ?? Array.Empty<byte>(), actual.Data ?? Array.Empty<byte>());
        Assert.Equal(expected.WaitMs, actual.WaitMs);
    }

    private static void AssertCanEqual(HeadCanCommandModel expected, HeadCanCommandModel actual)
    {
        Assert.Equal(expected.CanId, actual.CanId);
        Assert.Equal(expected.Dlc, actual.Dlc);
        Assert.Equal(expected.Data, actual.Data);
    }

    private static void AssertMotionEqual(HeadMotionCommandProfileModel expected, HeadMotionCommandProfileModel actual)
    {
        Assert.Equal(expected.ModuleKind, actual.ModuleKind);
        Assert.Equal(expected.CanId, actual.CanId);
        Assert.Equal(expected.Opcode, actual.Opcode);
        Assert.Equal(expected.MotorIndexBase, actual.MotorIndexBase);
        Assert.Equal(expected.InstanceCount, actual.InstanceCount);
        Assert.Equal(expected.RunSequence, actual.RunSequence);
        Assert.Equal(expected.AlternateRunSequence, actual.AlternateRunSequence);
        Assert.Equal(expected.Positions, actual.Positions);
        Assert.Equal(expected.RunPeriodMs, actual.RunPeriodMs);
        Assert.Equal(expected.AlternateRunPeriodMs, actual.AlternateRunPeriodMs);
    }

    private static void AssertCascadeEqual(HeadCascadeCommandProfileModel expected, HeadCascadeCommandProfileModel actual)
    {
        Assert.Equal(expected.ModuleKind, actual.ModuleKind);
        Assert.Equal(expected.CanId, actual.CanId);
        Assert.Equal(expected.Opcode, actual.Opcode);
        Assert.Equal(expected.AddressesPerInstance, actual.AddressesPerInstance);
        Assert.Equal(expected.InstanceCount, actual.InstanceCount);
        Assert.Equal(expected.Addresses, actual.Addresses);
        Assert.Equal(expected.OnValue, actual.OnValue);
        Assert.Equal(expected.OffValue, actual.OffValue);
        Assert.Equal(expected.RunPeriodMs, actual.RunPeriodMs);
    }

    private static void AssertStopEqual(HeadStopCommandProfileModel expected, HeadStopCommandProfileModel actual)
    {
        Assert.Equal(expected.SendsCanFrame, actual.SendsCanFrame);
        if (expected.Frame is null || actual.Frame is null) {
            Assert.Equal(expected.Frame, actual.Frame);
            return;
        }

        AssertCanEqual(expected.Frame, actual.Frame);
    }

    private static void AssertActionEqual(ProfileActionModel expected, ProfileActionModel actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.ActionName, actual.ActionName);
        Assert.Equal(expected.Category, actual.Category);
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.Steps.Count, actual.Steps.Count);
        for (int i = 0; i < expected.Steps.Count; i++) {
            AssertActionStepEqual(expected.Steps[i], actual.Steps[i]);
        }
    }

    private static void AssertActionStepEqual(ProfileActionStepModel expected, ProfileActionStepModel actual)
    {
        Assert.Equal(expected.StepOrder, actual.StepOrder);
        Assert.Equal(expected.StepKind, actual.StepKind);
        Assert.Equal(expected.Bus, actual.Bus);
        Assert.Equal(expected.CanId, actual.CanId);
        Assert.Equal(expected.Dlc, actual.Dlc);
        Assert.Equal(expected.Data ?? Array.Empty<byte>(), actual.Data ?? Array.Empty<byte>());
        Assert.Equal(expected.WaitMs, actual.WaitMs);
    }

    private static void AssertPackageLayoutWithinBounds(AcxProfilePackage package)
    {
        Assert.Equal(package.Header.SectionCount, (ushort)package.Sections.Count);

        ulong previousEnd = package.Header.HeaderSize;
        foreach (AcxSectionDirectoryEntry entry in package.Sections.OrderBy(section => section.Offset)) {
            Assert.True(entry.Offset >= package.Header.HeaderSize, $"Section {entry.SectionId} starts inside the header.");
            ulong end = (ulong)entry.Offset + entry.Size;
            Assert.True(end <= package.Header.FileSize, $"Section {entry.SectionId} exceeds the file length.");
            Assert.True(entry.Offset >= previousEnd, $"Section {entry.SectionId} overlaps a previous section.");
            previousEnd = end;
        }
    }

    private sealed class CorruptingAcxProfileCompiler : IAcxProfileCompiler
    {
        private readonly IAcxProfileCompiler _inner;

        public CorruptingAcxProfileCompiler(IAcxProfileCompiler inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public AcxCompilationResult Compile(ProfileVersionDocument document)
        {
            AcxCompilationResult result = _inner.Compile(document);
            byte[] corrupted = (byte[])result.PackageBytes.Clone();
            corrupted[(int)result.Header.PayloadOffset] ^= 0xFF;
            return result with { PackageBytes = corrupted, PackageSizeBytes = corrupted.Length };
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