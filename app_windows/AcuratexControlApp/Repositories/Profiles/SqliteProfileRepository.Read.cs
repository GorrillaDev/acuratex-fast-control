using System.Globalization;
using AcuratexControlApp.Models.Profiles;
using Microsoft.Data.Sqlite;

namespace AcuratexControlApp.Repositories.Profiles;

public sealed partial class SqliteProfileRepository
{
    private ProfileVersionDocument ReadVersionDocument(SqliteConnection connection, long profileVersionId)
    {
        long profileId;
        ProfileVersionSummary version;

        using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = @"
SELECT
    pv.id AS version_id,
    pv.profile_id,
    pv.version_number,
    pv.schema_version,
    pv.notes,
    pv.source_kind,
    pv.crc32,
    pv.is_published,
    pv.created_utc
FROM profile_versions pv
WHERE pv.id = $version_id;";
            command.Parameters.AddWithValue("$version_id", profileVersionId);

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) {
                throw new InvalidOperationException($"Profile version '{profileVersionId}' was not found.");
            }

            profileId = reader.GetInt64(reader.GetOrdinal("profile_id"));
            version = new ProfileVersionSummary(
                Id: reader.GetInt64(reader.GetOrdinal("version_id")),
                ProfileId: profileId,
                VersionNumber: reader.GetInt32(reader.GetOrdinal("version_number")),
                SchemaVersion: reader.GetInt32(reader.GetOrdinal("schema_version")),
                Notes: reader.GetString(reader.GetOrdinal("notes")),
                SourceKind: ProfileSqliteMappings.ParseProfileSourceKind(reader.GetString(reader.GetOrdinal("source_kind"))),
                Crc32: ReadNullableUInt32(reader, "crc32"),
                IsPublished: reader.GetInt32(reader.GetOrdinal("is_published")) != 0,
                CreatedUtc: ParseUtcTimestamp(reader.GetString(reader.GetOrdinal("created_utc"))));
        }

        ProfileRecord profile = LoadProfileRecordById(connection, profileId, loadVersions: true)
            ?? throw new InvalidOperationException($"Profile '{profileId}' was not found.");

        HeadCommandProfileModel commands = LoadCommands(connection, profile, version.Id);
        return new ProfileVersionDocument(profile, version, commands);
    }

    private ProfileRecord? LoadProfileRecordById(SqliteConnection connection, long profileId, bool loadVersions)
    {
        return TryLoadProfileRecord(connection, "p.id = $profile_id", cmd => cmd.Parameters.AddWithValue("$profile_id", profileId), loadVersions);
    }

    private ProfileRecord? TryLoadProfileRecord(SqliteConnection connection, string whereClause, Action<SqliteCommand> bindCommand, bool loadVersions)
    {
        long profileId;
        string profileKey;
        int? programNumber;
        string displayName;
        string description;
        bool enabled;
        DateTimeOffset createdUtc;
        DateTimeOffset updatedUtc;

        using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = $@"
SELECT
    p.id,
    p.profile_key,
    p.program_number,
    p.display_name,
    p.description,
    p.enabled,
    p.created_utc,
    p.updated_utc
FROM profiles p
WHERE {whereClause};";
            bindCommand(command);

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) {
                return null;
            }

            profileId = reader.GetInt64(reader.GetOrdinal("id"));
            profileKey = reader.GetString(reader.GetOrdinal("profile_key"));
            programNumber = ReadNullableInt32(reader, "program_number");
            displayName = reader.GetString(reader.GetOrdinal("display_name"));
            description = reader.GetString(reader.GetOrdinal("description"));
            enabled = reader.GetInt32(reader.GetOrdinal("enabled")) != 0;
            createdUtc = ParseUtcTimestamp(reader.GetString(reader.GetOrdinal("created_utc")));
            updatedUtc = ParseUtcTimestamp(reader.GetString(reader.GetOrdinal("updated_utc")));
        }

        IReadOnlyList<ProfileVersionSummary> versions = loadVersions
            ? LoadVersionSummaries(connection, profileId)
            : Array.Empty<ProfileVersionSummary>();

        return new ProfileRecord(
            Id: profileId,
            ProfileKey: profileKey,
            ProgramNumber: programNumber,
            DisplayName: displayName,
            Description: description,
            Enabled: enabled,
            CreatedUtc: createdUtc,
            UpdatedUtc: updatedUtc,
            Versions: versions);
    }

    private List<ProfileVersionSummary> LoadVersionSummaries(SqliteConnection connection, long profileId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    id,
    profile_id,
    version_number,
    schema_version,
    notes,
    source_kind,
    crc32,
    is_published,
    created_utc
FROM profile_versions
WHERE profile_id = $profile_id
ORDER BY version_number ASC;";
        command.Parameters.AddWithValue("$profile_id", profileId);

        List<ProfileVersionSummary> versions = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) {
            versions.Add(ReadVersionSummary(reader));
        }

        return versions;
    }

    private ProfileVersionSummary ReadVersionSummary(SqliteDataReader reader)
    {
        return new ProfileVersionSummary(
            Id: reader.GetInt64(reader.GetOrdinal("id")),
            ProfileId: reader.GetInt64(reader.GetOrdinal("profile_id")),
            VersionNumber: reader.GetInt32(reader.GetOrdinal("version_number")),
            SchemaVersion: reader.GetInt32(reader.GetOrdinal("schema_version")),
            Notes: reader.GetString(reader.GetOrdinal("notes")),
            SourceKind: ProfileSqliteMappings.ParseProfileSourceKind(reader.GetString(reader.GetOrdinal("source_kind"))),
            Crc32: ReadNullableUInt32(reader, "crc32"),
            IsPublished: reader.GetInt32(reader.GetOrdinal("is_published")) != 0,
            CreatedUtc: ParseUtcTimestamp(reader.GetString(reader.GetOrdinal("created_utc"))));
    }

    private HeadCommandProfileModel LoadCommands(SqliteConnection connection, ProfileRecord profile, long profileVersionId)
    {
        HeadInitCommandSequenceModel initSequence = LoadInitSequence(connection, profileVersionId);
        HeadTesteoCommandProfileModel testeo = LoadTesteo(connection, profileVersionId);
        HeadMotionCommandProfileModel den = LoadMotion(connection, profileVersionId, HeadMotionModuleKind.Den);
        HeadMotionCommandProfileModel sic = LoadMotion(connection, profileVersionId, HeadMotionModuleKind.Sic);
        HeadMotionCommandProfileModel feet = LoadMotion(connection, profileVersionId, HeadMotionModuleKind.Feet);
        HeadJCommandProfileModel j = LoadJ(connection, profileVersionId);
        HeadCascadeCommandProfileModel yarn = LoadCascade(connection, profileVersionId, HeadCascadeModuleKind.Yarn);
        HeadCascadeCommandProfileModel stitch = LoadCascade(connection, profileVersionId, HeadCascadeModuleKind.Stitch);
        HeadStopCommandProfileModel stop = LoadStop(connection, profileVersionId);

        return new HeadCommandProfileModel(
            ProgramNumber: profile.ProgramNumber ?? 0,
            ProgramName: profile.DisplayName,
            InitSequence: initSequence,
            Testeo: testeo,
            Den: den,
            Sic: sic,
            Feet: feet,
            J: j,
            Yarn: yarn,
            Stitch: stitch,
            Stop: stop);
    }

    private HeadInitCommandSequenceModel LoadInitSequence(SqliteConnection connection, long profileVersionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    phase1_step_delay_ms,
    phase_gap_ms,
    phase2_step_delay_ms
FROM init_config
WHERE profile_version_id = $profile_version_id;";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) {
            throw new InvalidOperationException($"INIT config missing for version '{profileVersionId}'.");
        }

        uint phase1Delay = (uint)reader.GetInt64(reader.GetOrdinal("phase1_step_delay_ms"));
        uint phaseGap = (uint)reader.GetInt64(reader.GetOrdinal("phase_gap_ms"));
        uint phase2Delay = (uint)reader.GetInt64(reader.GetOrdinal("phase2_step_delay_ms"));

        using SqliteCommand stepsCommand = connection.CreateCommand();
        stepsCommand.CommandText = @"
SELECT
    phase,
    step_order,
    step_type,
    bus,
    can_id,
    dlc,
    data,
    wait_ms,
    raw_text
FROM init_steps
WHERE profile_version_id = $profile_version_id
ORDER BY phase ASC, step_order ASC;";
        stepsCommand.Parameters.AddWithValue("$profile_version_id", profileVersionId);

        List<HeadInitStepModel> phase1Steps = new();
        List<HeadInitStepModel> phase2Steps = new();
        using SqliteDataReader stepsReader = stepsCommand.ExecuteReader();
        while (stepsReader.Read()) {
            int phase = stepsReader.GetInt32(stepsReader.GetOrdinal("phase"));
            int stepOrder = stepsReader.GetInt32(stepsReader.GetOrdinal("step_order"));
            string rawText = stepsReader.GetString(stepsReader.GetOrdinal("raw_text"));
            HeadInitStepKind stepKind = ProfileSqliteMappings.ParseHeadInitStepKind(stepsReader.GetString(stepsReader.GetOrdinal("step_type")));

            HeadInitStepModel step = stepKind switch
            {
                HeadInitStepKind.Can => new HeadInitStepModel(
                    phase,
                    stepOrder,
                    rawText,
                    stepKind,
                    ReadNullableInt32(stepsReader, "bus"),
                    ReadRequiredUInt32(stepsReader, "can_id"),
                    ReadRequiredByte(stepsReader, "dlc"),
                    ReadBlob(stepsReader, "data"),
                    null),
                HeadInitStepKind.Wait => new HeadInitStepModel(
                    phase,
                    stepOrder,
                    rawText,
                    stepKind,
                    null,
                    null,
                    null,
                    null,
                    ReadNullableInt32(stepsReader, "wait_ms")),
                HeadInitStepKind.Status => new HeadInitStepModel(
                    phase,
                    stepOrder,
                    rawText,
                    stepKind,
                    null,
                    null,
                    null,
                    null,
                    null),
                _ => throw new InvalidOperationException($"Unsupported INIT step kind '{stepKind}'."),
            };

            if (phase == 1) {
                phase1Steps.Add(step);
            } else if (phase == 2) {
                phase2Steps.Add(step);
            } else {
                throw new InvalidOperationException($"Unsupported INIT phase '{phase}'.");
            }
        }

        return new HeadInitCommandSequenceModel(phase1Delay, phaseGap, phase2Delay, phase1Steps, phase2Steps);
    }

    private HeadTesteoCommandProfileModel LoadTesteo(SqliteConnection connection, long profileVersionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    ping_can_id,
    ping_dlc,
    ping_data,
    response_can_id,
    reset_can_id,
    reset_dlc,
    reset_data,
    success_code,
    missing_expansion_code,
    missing_force_code,
    force_board_1_code,
    force_board_2_code,
    max_tries,
    response_timeout_ms,
    retry_delay_ms,
    reset_debounce_ms
FROM testeo_profiles
WHERE profile_version_id = $profile_version_id;";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) {
            throw new InvalidOperationException($"TESTEO profile missing for version '{profileVersionId}'.");
        }

        return new HeadTesteoCommandProfileModel(
            Ping: new HeadCanCommandModel(
                CanId: ReadRequiredUInt32(reader, "ping_can_id"),
                Dlc: ReadRequiredByte(reader, "ping_dlc"),
                Data: ReadBlob(reader, "ping_data")),
            ResponseCanId: ReadRequiredUInt32(reader, "response_can_id"),
            ResetCanId: ReadRequiredUInt32(reader, "reset_can_id"),
            Reset: new HeadCanCommandModel(
                CanId: ReadRequiredUInt32(reader, "reset_can_id"),
                Dlc: ReadRequiredByte(reader, "reset_dlc"),
                Data: ReadBlob(reader, "reset_data")),
            SuccessCode: ReadRequiredByte(reader, "success_code"),
            MissingExpansionCode: ReadRequiredByte(reader, "missing_expansion_code"),
            MissingForceCode: ReadRequiredByte(reader, "missing_force_code"),
            ForceBoard1Code: ReadRequiredByte(reader, "force_board_1_code"),
            ForceBoard2Code: ReadRequiredByte(reader, "force_board_2_code"),
            MaxTries: (ushort)reader.GetInt32(reader.GetOrdinal("max_tries")),
            ResponseTimeoutMs: (uint)reader.GetInt64(reader.GetOrdinal("response_timeout_ms")),
            RetryDelayMs: (uint)reader.GetInt64(reader.GetOrdinal("retry_delay_ms")),
            ResetDebounceMs: (uint)reader.GetInt64(reader.GetOrdinal("reset_debounce_ms")));
    }

    private HeadMotionCommandProfileModel LoadMotion(SqliteConnection connection, long profileVersionId, HeadMotionModuleKind moduleKind)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    id,
    module_type,
    can_id,
    opcode,
    motor_index_base,
    instance_count,
    run_period_ms,
    alternate_run_period_ms
FROM motion_modules
WHERE profile_version_id = $profile_version_id
  AND module_type = $module_type;";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$module_type", moduleKind.ToDatabaseValue());

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) {
            throw new InvalidOperationException($"Motion module '{moduleKind}' missing for version '{profileVersionId}'.");
        }

        long moduleId = reader.GetInt64(reader.GetOrdinal("id"));
        List<ushort> positions = new();
        using (SqliteCommand positionsCommand = connection.CreateCommand()) {
            positionsCommand.CommandText = @"
SELECT position_order, position_value
FROM motion_positions
WHERE motion_module_id = $motion_module_id
ORDER BY position_order ASC;";
            positionsCommand.Parameters.AddWithValue("$motion_module_id", moduleId);
            using SqliteDataReader positionsReader = positionsCommand.ExecuteReader();
            while (positionsReader.Read()) {
                positions.Add((ushort)positionsReader.GetInt32(positionsReader.GetOrdinal("position_value")));
            }
        }

        List<byte> runSequence = new();
        List<byte> alternateRunSequence = new();
        using (SqliteCommand sequencesCommand = connection.CreateCommand()) {
            sequencesCommand.CommandText = @"
SELECT sequence_kind, step_order, position_index
FROM motion_sequences
WHERE motion_module_id = $motion_module_id
ORDER BY sequence_kind ASC, step_order ASC;";
            sequencesCommand.Parameters.AddWithValue("$motion_module_id", moduleId);
            using SqliteDataReader sequencesReader = sequencesCommand.ExecuteReader();
            while (sequencesReader.Read()) {
                string sequenceKind = sequencesReader.GetString(sequencesReader.GetOrdinal("sequence_kind"));
                byte positionIndex = checked((byte)sequencesReader.GetInt32(sequencesReader.GetOrdinal("position_index")));
                if (string.Equals(sequenceKind, "RUN", StringComparison.OrdinalIgnoreCase)) {
                    runSequence.Add(positionIndex);
                } else if (string.Equals(sequenceKind, "ALTERNATE", StringComparison.OrdinalIgnoreCase)) {
                    alternateRunSequence.Add(positionIndex);
                } else {
                    throw new InvalidOperationException($"Unsupported motion sequence kind '{sequenceKind}'.");
                }
            }
        }

        return new HeadMotionCommandProfileModel(
            ModuleKind: moduleKind,
            CanId: ReadRequiredUInt32(reader, "can_id"),
            Opcode: ReadRequiredByte(reader, "opcode"),
            MotorIndexBase: ReadRequiredByte(reader, "motor_index_base"),
            InstanceCount: reader.GetInt32(reader.GetOrdinal("instance_count")),
            RunSequence: runSequence,
            AlternateRunSequence: alternateRunSequence,
            Positions: positions,
            RunPeriodMs: (uint)reader.GetInt64(reader.GetOrdinal("run_period_ms")),
            AlternateRunPeriodMs: (uint)reader.GetInt64(reader.GetOrdinal("alternate_run_period_ms")));
    }

    private HeadJCommandProfileModel LoadJ(SqliteConnection connection, long profileVersionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    can_id,
    opcode,
    instance_index_base,
    instance_count,
    channel_count,
    initial_register,
    on_all_register,
    off_all_register,
    run_period_ms
FROM j_modules
WHERE profile_version_id = $profile_version_id;";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) {
            throw new InvalidOperationException($"J profile missing for version '{profileVersionId}'.");
        }

        return new HeadJCommandProfileModel(
            CanId: ReadRequiredUInt32(reader, "can_id"),
            Opcode: ReadRequiredByte(reader, "opcode"),
            InstanceIndexBase: ReadRequiredByte(reader, "instance_index_base"),
            InstanceCount: reader.GetInt32(reader.GetOrdinal("instance_count")),
            ChannelCount: ReadRequiredByte(reader, "channel_count"),
            InitialRegister: ReadRequiredByte(reader, "initial_register"),
            OnAllRegister: ReadRequiredByte(reader, "on_all_register"),
            OffAllRegister: ReadRequiredByte(reader, "off_all_register"),
            RunPeriodMs: (uint)reader.GetInt64(reader.GetOrdinal("run_period_ms")));
    }

    private HeadCascadeCommandProfileModel LoadCascade(SqliteConnection connection, long profileVersionId, HeadCascadeModuleKind moduleKind)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    id,
    module_type,
    can_id,
    opcode,
    addresses_per_instance,
    instance_count,
    on_value,
    off_value,
    run_period_ms
FROM cascade_modules
WHERE profile_version_id = $profile_version_id
  AND module_type = $module_type;";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$module_type", moduleKind.ToDatabaseValue());

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) {
            throw new InvalidOperationException($"Cascade module '{moduleKind}' missing for version '{profileVersionId}'.");
        }

        long moduleId = reader.GetInt64(reader.GetOrdinal("id"));
        List<byte> addresses = new();
        using (SqliteCommand addressesCommand = connection.CreateCommand()) {
            addressesCommand.CommandText = @"
SELECT address_order, address_value
FROM cascade_addresses
WHERE cascade_module_id = $cascade_module_id
ORDER BY address_order ASC;";
            addressesCommand.Parameters.AddWithValue("$cascade_module_id", moduleId);
            using SqliteDataReader addressesReader = addressesCommand.ExecuteReader();
            while (addressesReader.Read()) {
                addresses.Add(checked((byte)addressesReader.GetInt32(addressesReader.GetOrdinal("address_value"))));
            }
        }

        return new HeadCascadeCommandProfileModel(
            ModuleKind: moduleKind,
            CanId: ReadRequiredUInt32(reader, "can_id"),
            Opcode: ReadRequiredByte(reader, "opcode"),
            AddressesPerInstance: ReadRequiredByte(reader, "addresses_per_instance"),
            InstanceCount: reader.GetInt32(reader.GetOrdinal("instance_count")),
            Addresses: addresses,
            OnValue: ReadRequiredByte(reader, "on_value"),
            OffValue: ReadRequiredByte(reader, "off_value"),
            RunPeriodMs: (uint)reader.GetInt64(reader.GetOrdinal("run_period_ms")));
    }

    private HeadStopCommandProfileModel LoadStop(SqliteConnection connection, long profileVersionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT sends_can_frame, bus, can_id, dlc, data
FROM stop_profiles
WHERE profile_version_id = $profile_version_id;";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) {
            throw new InvalidOperationException($"STOP profile missing for version '{profileVersionId}'.");
        }

        bool sendsCanFrame = reader.GetInt32(reader.GetOrdinal("sends_can_frame")) != 0;
        if (!sendsCanFrame) {
            return new HeadStopCommandProfileModel(false, null);
        }

        HeadCanCommandModel frame = new(
            CanId: ReadRequiredUInt32(reader, "can_id"),
            Dlc: ReadRequiredByte(reader, "dlc"),
            Data: ReadBlob(reader, "data"));
        return new HeadStopCommandProfileModel(true, frame);
    }
}
