using AcuratexControlApp.Data.Sqlite;
using AcuratexControlApp.Data.Sqlite.Importers;
using AcuratexControlApp.Models.Profiles;
using AcuratexControlApp.Repositories.Profiles;
using AcuratexControlApp.Services.Profiles;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AcuratexControlApp.Tests;

public sealed class ProfileSqliteInfrastructureTests
{
    public static TheoryData<InitialProfileSeed> Seeds => new()
    {
        InitialProfileSeeds.Program1,
        InitialProfileSeeds.Program2,
    };

    [Fact]
    public void DatabaseIsCreatedFromScratch()
    {
        using ProfileTestHarness harness = new();

        Assert.False(File.Exists(harness.Options.DatabasePath));

        harness.Initializer.EnsureInitialized();

        Assert.True(File.Exists(harness.Options.DatabasePath));
        Assert.True(Directory.Exists(harness.Options.DatabaseDirectory));
    }

    [Fact]
    public void SchemaCanBeInitializedTwiceWithoutFailing()
    {
        using ProfileTestHarness harness = new();

        harness.Initializer.EnsureInitialized();
        harness.Initializer.EnsureInitialized();

        IReadOnlyList<ProfileListItem> profiles = harness.Repository.ListProfiles();
        Assert.Equal(2, profiles.Count);
        Assert.All(profiles, profile => Assert.Equal(1, profile.VersionCount));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ImportedProgramsRoundTripWithoutLoss(InitialProfileSeed seed)
    {
        using ProfileTestHarness harness = new();

        harness.Initializer.EnsureInitialized();

        ProfileRecord? profile = harness.Repository.GetProfileByKey(seed.CreateRequest.ProfileKey);
        Assert.NotNull(profile);
        AssertProfileMatchesSeed(seed, profile!);

        Assert.NotEmpty(profile!.Versions);
        ProfileVersionDocument version = harness.Repository.ReadVersion(profile.Versions[0].Id);
        AssertProfileVersionMatchesSeed(seed, version);
    }

    [Fact]
    public void Program1KeepsFeetEmpty()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileRecord program1 = harness.Repository.GetProfileByKey(InitialProfileSeeds.Program1.CreateRequest.ProfileKey)
            ?? throw new InvalidOperationException("Program 1 was not imported.");
        ProfileVersionDocument version = harness.Repository.ReadVersion(program1.Versions[0].Id);

        Assert.Equal(0, version.Commands.Feet.InstanceCount);
        Assert.Empty(version.Commands.Feet.RunSequence);
        Assert.Empty(version.Commands.Feet.AlternateRunSequence);
        Assert.Empty(version.Commands.Feet.Positions);
        Assert.Equal(0u, version.Commands.Feet.RunPeriodMs);
        Assert.Equal(0u, version.Commands.Feet.AlternateRunPeriodMs);
    }

    [Fact]
    public void Program2KeepsFeetConfigured()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileRecord program2 = harness.Repository.GetProfileByKey(InitialProfileSeeds.Program2.CreateRequest.ProfileKey)
            ?? throw new InvalidOperationException("Program 2 was not imported.");
        ProfileVersionDocument version = harness.Repository.ReadVersion(program2.Versions[0].Id);

        Assert.Equal(2, version.Commands.Feet.InstanceCount);
        Assert.NotEmpty(version.Commands.Feet.RunSequence);
        Assert.NotEmpty(version.Commands.Feet.Positions);
        Assert.Equal(300u, version.Commands.Feet.RunPeriodMs);
        Assert.Equal(0u, version.Commands.Feet.AlternateRunPeriodMs);
    }

    [Fact]
    public void InvalidVersionDoesNotLeavePartialData()
    {
        using ProfileTestHarness harness = new();
        harness.Initializer.EnsureInitialized();

        ProfileRecord profile = harness.Repository.GetProfileByKey(InitialProfileSeeds.Program1.CreateRequest.ProfileKey)
            ?? throw new InvalidOperationException("Program 1 was not imported.");
        ProfileVersionDocument current = harness.Repository.ReadVersion(profile.Versions[0].Id);

        HeadCommandProfileModel invalidCommands = current.Commands with
        {
            Den = current.Commands.Den with
            {
                RunSequence = new byte[] { 99 },
            },
        };

        Assert.Throws<ProfileValidationException>(() => harness.Repository.SaveNewVersion(
            profile.Id,
            new ProfileVersionWriteRequest(
                VersionNumber: 2,
                SchemaVersion: 1,
                Notes: "invalid",
                SourceKind: ProfileSourceKind.Manual,
                Crc32: null,
                IsPublished: false,
                Commands: invalidCommands)));

        ProfileRecord reloaded = harness.Repository.GetProfileById(profile.Id)
            ?? throw new InvalidOperationException("Program 1 was not reloaded.");
        Assert.Single(reloaded.Versions);
        Assert.Equal(1, reloaded.Versions[0].VersionNumber);
    }

    [Fact]
    public void ForeignKeysAreEnabled()
    {
        using ProfileTestHarness harness = new();
        using SqliteConnection connection = harness.ConnectionFactory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        long foreignKeys = (long)command.ExecuteScalar()!;

        Assert.Equal(1L, foreignKeys);
    }

    private static void AssertProfileMatchesSeed(InitialProfileSeed seed, ProfileRecord profile)
    {
        Assert.Equal(seed.CreateRequest.ProfileKey, profile.ProfileKey);
        Assert.Equal(seed.CreateRequest.ProgramNumber, profile.ProgramNumber);
        Assert.Equal(seed.CreateRequest.DisplayName, profile.DisplayName);
        Assert.Equal(seed.CreateRequest.Description, profile.Description);
        Assert.Equal(seed.CreateRequest.Enabled, profile.Enabled);
        Assert.NotEmpty(profile.Versions);
        Assert.Equal(seed.VersionRequest.VersionNumber, profile.Versions[0].VersionNumber);
    }

    private static void AssertProfileVersionMatchesSeed(InitialProfileSeed seed, ProfileVersionDocument actual)
    {
        Assert.Equal(seed.CreateRequest.ProfileKey, actual.Profile.ProfileKey);
        Assert.Equal(seed.CreateRequest.ProgramNumber, actual.Profile.ProgramNumber);
        Assert.Equal(seed.CreateRequest.DisplayName, actual.Profile.DisplayName);
        Assert.Equal(seed.CreateRequest.Description, actual.Profile.Description);
        Assert.Equal(seed.CreateRequest.Enabled, actual.Profile.Enabled);
        Assert.Equal(seed.VersionRequest.VersionNumber, actual.Version.VersionNumber);
        Assert.Equal(seed.VersionRequest.SchemaVersion, actual.Version.SchemaVersion);
        Assert.Equal(seed.VersionRequest.Notes, actual.Version.Notes);
        Assert.Equal(seed.VersionRequest.SourceKind, actual.Version.SourceKind);
        Assert.Equal(seed.VersionRequest.Crc32, actual.Version.Crc32);
        Assert.Equal(seed.VersionRequest.IsPublished, actual.Version.IsPublished);
        Assert.Equal(seed.VersionRequest.Commands.ProgramNumber, actual.Commands.ProgramNumber);
        Assert.Equal(seed.VersionRequest.Commands.ProgramName, actual.Commands.ProgramName);

        AssertInitSequence(seed.VersionRequest.Commands.InitSequence, actual.Commands.InitSequence);
        AssertTesteo(seed.VersionRequest.Commands.Testeo, actual.Commands.Testeo);
        AssertMotion(seed.VersionRequest.Commands.Den, actual.Commands.Den);
        AssertMotion(seed.VersionRequest.Commands.Sic, actual.Commands.Sic);
        AssertMotion(seed.VersionRequest.Commands.Feet, actual.Commands.Feet);
        Assert.Equal(seed.VersionRequest.Commands.J, actual.Commands.J);
        AssertCascade(seed.VersionRequest.Commands.Yarn, actual.Commands.Yarn);
        AssertCascade(seed.VersionRequest.Commands.Stitch, actual.Commands.Stitch);
        AssertStop(seed.VersionRequest.Commands.Stop, actual.Commands.Stop);
    }

    private static void AssertInitSequence(HeadInitCommandSequenceModel expected, HeadInitCommandSequenceModel actual)
    {
        Assert.Equal(expected.Phase1StepDelayMs, actual.Phase1StepDelayMs);
        Assert.Equal(expected.PhaseGapMs, actual.PhaseGapMs);
        Assert.Equal(expected.Phase2StepDelayMs, actual.Phase2StepDelayMs);
        Assert.Equal(expected.Phase1Steps.Count, actual.Phase1Steps.Count);
        Assert.Equal(expected.Phase2Steps.Count, actual.Phase2Steps.Count);

        for (int i = 0; i < expected.Phase1Steps.Count; i++) {
            AssertInitStep(expected.Phase1Steps[i], actual.Phase1Steps[i]);
        }

        for (int i = 0; i < expected.Phase2Steps.Count; i++) {
            AssertInitStep(expected.Phase2Steps[i], actual.Phase2Steps[i]);
        }
    }

    private static void AssertInitStep(HeadInitStepModel expected, HeadInitStepModel actual)
    {
        Assert.Equal(expected.Phase, actual.Phase);
        Assert.Equal(expected.StepOrder, actual.StepOrder);
        Assert.Equal(expected.RawText, actual.RawText);
        Assert.Equal(expected.StepKind, actual.StepKind);
        Assert.Equal(expected.Bus, actual.Bus);
        Assert.Equal(expected.CanId, actual.CanId);
        Assert.Equal(expected.Dlc, actual.Dlc);
        Assert.Equal(expected.WaitMs, actual.WaitMs);
        Assert.Equal(expected.Data ?? Array.Empty<byte>(), actual.Data ?? Array.Empty<byte>());
    }

    private static void AssertTesteo(HeadTesteoCommandProfileModel expected, HeadTesteoCommandProfileModel actual)
    {
        Assert.Equal(expected.Ping.CanId, actual.Ping.CanId);
        Assert.Equal(expected.Ping.Dlc, actual.Ping.Dlc);
        Assert.Equal(expected.Ping.Data, actual.Ping.Data);
        Assert.Equal(expected.ResponseCanId, actual.ResponseCanId);
        Assert.Equal(expected.ResetCanId, actual.ResetCanId);
        Assert.Equal(expected.Reset.Dlc, actual.Reset.Dlc);
        Assert.Equal(expected.Reset.Data, actual.Reset.Data);
        Assert.Equal(expected.SuccessCode, actual.SuccessCode);
        Assert.Equal(expected.MissingExpansionCode, actual.MissingExpansionCode);
        Assert.Equal(expected.MissingForceCode, actual.MissingForceCode);
        Assert.Equal(expected.ForceBoard1Code, actual.ForceBoard1Code);
        Assert.Equal(expected.ForceBoard2Code, actual.ForceBoard2Code);
        Assert.Equal(expected.MaxTries, actual.MaxTries);
        Assert.Equal(expected.ResponseTimeoutMs, actual.ResponseTimeoutMs);
        Assert.Equal(expected.RetryDelayMs, actual.RetryDelayMs);
        Assert.Equal(expected.ResetDebounceMs, actual.ResetDebounceMs);
    }

    private static void AssertMotion(HeadMotionCommandProfileModel expected, HeadMotionCommandProfileModel actual)
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

    private static void AssertCascade(HeadCascadeCommandProfileModel expected, HeadCascadeCommandProfileModel actual)
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

    private static void AssertStop(HeadStopCommandProfileModel expected, HeadStopCommandProfileModel actual)
    {
        Assert.Equal(expected.SendsCanFrame, actual.SendsCanFrame);
        if (expected.Frame is null || actual.Frame is null) {
            Assert.Null(expected.Frame);
            Assert.Null(actual.Frame);
            return;
        }

        Assert.Equal(expected.Frame.CanId, actual.Frame.CanId);
        Assert.Equal(expected.Frame.Dlc, actual.Frame.Dlc);
        Assert.Equal(expected.Frame.Data, actual.Frame.Data);
    }

    private sealed class ProfileTestHarness : IDisposable
    {
        public ProfileTestHarness()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "AcuratexFastControl.Tests", Guid.NewGuid().ToString("N"));
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
                // Intentionally ignored: the OS can keep transient file locks for a brief moment after tests.
            }
        }
    }
}


