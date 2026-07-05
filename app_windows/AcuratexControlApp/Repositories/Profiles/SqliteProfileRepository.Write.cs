using System.Globalization;
using AcuratexControlApp.Models.Profiles;
using Microsoft.Data.Sqlite;

namespace AcuratexControlApp.Repositories.Profiles;

public sealed partial class SqliteProfileRepository
{
    private long InsertProfile(SqliteConnection connection, SqliteTransaction transaction, ProfileCreateRequest request)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO profiles(profile_key, program_number, display_name, description, enabled)
VALUES ($profile_key, $program_number, $display_name, $description, $enabled);";
        command.Parameters.AddWithValue("$profile_key", request.ProfileKey);
        command.Parameters.AddWithValue("$program_number", (object?)request.ProgramNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$display_name", request.DisplayName);
        command.Parameters.AddWithValue("$description", request.Description ?? string.Empty);
        command.Parameters.AddWithValue("$enabled", request.Enabled ? 1 : 0);
        command.ExecuteNonQuery();
        return GetLastInsertRowId(connection);
    }

    private long InsertProfileVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long profileId,
        int versionNumber,
        ProfileVersionWriteRequest request)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO profile_versions(profile_id, version_number, schema_version, notes, source_kind, crc32, is_published)
VALUES ($profile_id, $version_number, $schema_version, $notes, $source_kind, $crc32, $is_published);";
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$version_number", versionNumber);
        command.Parameters.AddWithValue("$schema_version", request.SchemaVersion);
        command.Parameters.AddWithValue("$notes", request.Notes ?? string.Empty);
        command.Parameters.AddWithValue("$source_kind", request.SourceKind.ToDatabaseValue());
        command.Parameters.AddWithValue("$crc32", request.Crc32.HasValue ? (object)request.Crc32.Value : DBNull.Value);
        command.Parameters.AddWithValue("$is_published", request.IsPublished ? 1 : 0);
        command.ExecuteNonQuery();
        return GetLastInsertRowId(connection);
    }

    private void InsertProfileVersionContent(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, HeadCommandProfileModel commands, IReadOnlyList<ProfileActionModel> actions)
    {
        InsertInitSequence(connection, transaction, profileVersionId, commands.InitSequence);
        InsertTesteo(connection, transaction, profileVersionId, commands.Testeo);
        InsertMotionModule(connection, transaction, profileVersionId, commands.Den);
        InsertMotionModule(connection, transaction, profileVersionId, commands.Sic);
        InsertMotionModule(connection, transaction, profileVersionId, commands.Feet);
        InsertJModule(connection, transaction, profileVersionId, commands.J);
        InsertCascadeModule(connection, transaction, profileVersionId, commands.Yarn);
        InsertCascadeModule(connection, transaction, profileVersionId, commands.Stitch);
        InsertStop(connection, transaction, profileVersionId, commands.Stop);
        InsertActions(connection, transaction, profileVersionId, actions);
    }

    private void InsertInitSequence(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, HeadInitCommandSequenceModel sequence)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO init_config(profile_version_id, phase1_step_delay_ms, phase_gap_ms, phase2_step_delay_ms)
VALUES ($profile_version_id, $phase1_step_delay_ms, $phase_gap_ms, $phase2_step_delay_ms);";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$phase1_step_delay_ms", sequence.Phase1StepDelayMs);
        command.Parameters.AddWithValue("$phase_gap_ms", sequence.PhaseGapMs);
        command.Parameters.AddWithValue("$phase2_step_delay_ms", sequence.Phase2StepDelayMs);
        command.ExecuteNonQuery();

        InsertInitSteps(connection, transaction, profileVersionId, 1, sequence.Phase1Steps);
        InsertInitSteps(connection, transaction, profileVersionId, 2, sequence.Phase2Steps);
    }

    private void InsertInitSteps(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, int phase, IReadOnlyList<HeadInitStepModel> steps)
    {
        for (int i = 0; i < steps.Count; i++) {
            HeadInitStepModel step = steps[i];
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO init_steps(profile_version_id, phase, step_order, step_type, bus, can_id, dlc, data, wait_ms, raw_text)
VALUES ($profile_version_id, $phase, $step_order, $step_type, $bus, $can_id, $dlc, $data, $wait_ms, $raw_text);";
            command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
            command.Parameters.AddWithValue("$phase", phase);
            command.Parameters.AddWithValue("$step_order", i);
            command.Parameters.AddWithValue("$step_type", step.StepKind.ToDatabaseValue());
            command.Parameters.AddWithValue("$bus", step.Bus.HasValue ? (object)step.Bus.Value : DBNull.Value);
            command.Parameters.AddWithValue("$can_id", step.CanId.HasValue ? (object)step.CanId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$dlc", step.Dlc.HasValue ? (object)step.Dlc.Value : DBNull.Value);
            command.Parameters.AddWithValue("$data", step.Data is null ? DBNull.Value : step.Data);
            command.Parameters.AddWithValue("$wait_ms", step.WaitMs.HasValue ? (object)step.WaitMs.Value : DBNull.Value);
            command.Parameters.AddWithValue("$raw_text", step.RawText);
            command.ExecuteNonQuery();
        }
    }

    private void InsertTesteo(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, HeadTesteoCommandProfileModel testeo)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO testeo_profiles(
    profile_version_id,
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
    reset_debounce_ms)
VALUES (
    $profile_version_id,
    $ping_can_id,
    $ping_dlc,
    $ping_data,
    $response_can_id,
    $reset_can_id,
    $reset_dlc,
    $reset_data,
    $success_code,
    $missing_expansion_code,
    $missing_force_code,
    $force_board_1_code,
    $force_board_2_code,
    $max_tries,
    $response_timeout_ms,
    $retry_delay_ms,
    $reset_debounce_ms);";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$ping_can_id", testeo.Ping.CanId);
        command.Parameters.AddWithValue("$ping_dlc", testeo.Ping.Dlc);
        command.Parameters.AddWithValue("$ping_data", testeo.Ping.Data);
        command.Parameters.AddWithValue("$response_can_id", testeo.ResponseCanId);
        command.Parameters.AddWithValue("$reset_can_id", testeo.ResetCanId);
        command.Parameters.AddWithValue("$reset_dlc", testeo.Reset.Dlc);
        command.Parameters.AddWithValue("$reset_data", testeo.Reset.Data);
        command.Parameters.AddWithValue("$success_code", testeo.SuccessCode);
        command.Parameters.AddWithValue("$missing_expansion_code", testeo.MissingExpansionCode);
        command.Parameters.AddWithValue("$missing_force_code", testeo.MissingForceCode);
        command.Parameters.AddWithValue("$force_board_1_code", testeo.ForceBoard1Code);
        command.Parameters.AddWithValue("$force_board_2_code", testeo.ForceBoard2Code);
        command.Parameters.AddWithValue("$max_tries", testeo.MaxTries);
        command.Parameters.AddWithValue("$response_timeout_ms", testeo.ResponseTimeoutMs);
        command.Parameters.AddWithValue("$retry_delay_ms", testeo.RetryDelayMs);
        command.Parameters.AddWithValue("$reset_debounce_ms", testeo.ResetDebounceMs);
        command.ExecuteNonQuery();
    }

    private void InsertMotionModule(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, HeadMotionCommandProfileModel motion)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO motion_modules(
    profile_version_id,
    module_type,
    can_id,
    opcode,
    motor_index_base,
    instance_count,
    run_period_ms,
    alternate_run_period_ms)
VALUES (
    $profile_version_id,
    $module_type,
    $can_id,
    $opcode,
    $motor_index_base,
    $instance_count,
    $run_period_ms,
    $alternate_run_period_ms);";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$module_type", motion.ModuleKind.ToDatabaseValue());
        command.Parameters.AddWithValue("$can_id", motion.CanId);
        command.Parameters.AddWithValue("$opcode", motion.Opcode);
        command.Parameters.AddWithValue("$motor_index_base", motion.MotorIndexBase);
        command.Parameters.AddWithValue("$instance_count", motion.InstanceCount);
        command.Parameters.AddWithValue("$run_period_ms", motion.RunPeriodMs);
        command.Parameters.AddWithValue("$alternate_run_period_ms", motion.AlternateRunPeriodMs);
        command.ExecuteNonQuery();

        long motionModuleId = GetLastInsertRowId(connection);
        for (int i = 0; i < motion.Positions.Count; i++) {
            using SqliteCommand positionCommand = connection.CreateCommand();
            positionCommand.Transaction = transaction;
            positionCommand.CommandText = @"
INSERT INTO motion_positions(motion_module_id, position_order, position_value)
VALUES ($motion_module_id, $position_order, $position_value);";
            positionCommand.Parameters.AddWithValue("$motion_module_id", motionModuleId);
            positionCommand.Parameters.AddWithValue("$position_order", i);
            positionCommand.Parameters.AddWithValue("$position_value", motion.Positions[i]);
            positionCommand.ExecuteNonQuery();
        }

        InsertMotionSequence(connection, transaction, motionModuleId, "RUN", motion.RunSequence);
        InsertMotionSequence(connection, transaction, motionModuleId, "ALTERNATE", motion.AlternateRunSequence);
    }

    private void InsertMotionSequence(SqliteConnection connection, SqliteTransaction transaction, long motionModuleId, string sequenceKind, IReadOnlyList<byte> sequence)
    {
        for (int i = 0; i < sequence.Count; i++) {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO motion_sequences(motion_module_id, sequence_kind, step_order, position_index)
VALUES ($motion_module_id, $sequence_kind, $step_order, $position_index);";
            command.Parameters.AddWithValue("$motion_module_id", motionModuleId);
            command.Parameters.AddWithValue("$sequence_kind", sequenceKind);
            command.Parameters.AddWithValue("$step_order", i);
            command.Parameters.AddWithValue("$position_index", sequence[i]);
            command.ExecuteNonQuery();
        }
    }

    private void InsertJModule(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, HeadJCommandProfileModel jProfile)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO j_modules(
    profile_version_id,
    can_id,
    opcode,
    instance_index_base,
    instance_count,
    channel_count,
    initial_register,
    on_all_register,
    off_all_register,
    run_period_ms)
VALUES (
    $profile_version_id,
    $can_id,
    $opcode,
    $instance_index_base,
    $instance_count,
    $channel_count,
    $initial_register,
    $on_all_register,
    $off_all_register,
    $run_period_ms);";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$can_id", jProfile.CanId);
        command.Parameters.AddWithValue("$opcode", jProfile.Opcode);
        command.Parameters.AddWithValue("$instance_index_base", jProfile.InstanceIndexBase);
        command.Parameters.AddWithValue("$instance_count", jProfile.InstanceCount);
        command.Parameters.AddWithValue("$channel_count", jProfile.ChannelCount);
        command.Parameters.AddWithValue("$initial_register", jProfile.InitialRegister);
        command.Parameters.AddWithValue("$on_all_register", jProfile.OnAllRegister);
        command.Parameters.AddWithValue("$off_all_register", jProfile.OffAllRegister);
        command.Parameters.AddWithValue("$run_period_ms", jProfile.RunPeriodMs);
        command.ExecuteNonQuery();
    }

    private void InsertCascadeModule(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, HeadCascadeCommandProfileModel cascade)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO cascade_modules(
    profile_version_id,
    module_type,
    can_id,
    opcode,
    addresses_per_instance,
    instance_count,
    on_value,
    off_value,
    run_period_ms)
VALUES (
    $profile_version_id,
    $module_type,
    $can_id,
    $opcode,
    $addresses_per_instance,
    $instance_count,
    $on_value,
    $off_value,
    $run_period_ms);";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$module_type", cascade.ModuleKind.ToDatabaseValue());
        command.Parameters.AddWithValue("$can_id", cascade.CanId);
        command.Parameters.AddWithValue("$opcode", cascade.Opcode);
        command.Parameters.AddWithValue("$addresses_per_instance", cascade.AddressesPerInstance);
        command.Parameters.AddWithValue("$instance_count", cascade.InstanceCount);
        command.Parameters.AddWithValue("$on_value", cascade.OnValue);
        command.Parameters.AddWithValue("$off_value", cascade.OffValue);
        command.Parameters.AddWithValue("$run_period_ms", cascade.RunPeriodMs);
        command.ExecuteNonQuery();

        long cascadeModuleId = GetLastInsertRowId(connection);
        for (int i = 0; i < cascade.Addresses.Count; i++) {
            using SqliteCommand addressCommand = connection.CreateCommand();
            addressCommand.Transaction = transaction;
            addressCommand.CommandText = @"
INSERT INTO cascade_addresses(cascade_module_id, address_order, address_value)
VALUES ($cascade_module_id, $address_order, $address_value);";
            addressCommand.Parameters.AddWithValue("$cascade_module_id", cascadeModuleId);
            addressCommand.Parameters.AddWithValue("$address_order", i);
            addressCommand.Parameters.AddWithValue("$address_value", cascade.Addresses[i]);
            addressCommand.ExecuteNonQuery();
        }
    }

    private void InsertStop(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, HeadStopCommandProfileModel stop)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO stop_profiles(profile_version_id, sends_can_frame, bus, can_id, dlc, data)
VALUES ($profile_version_id, $sends_can_frame, $bus, $can_id, $dlc, $data);";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$sends_can_frame", stop.SendsCanFrame ? 1 : 0);
        if (stop.SendsCanFrame && stop.Frame is not null) {
            command.Parameters.AddWithValue("$bus", 1);
            command.Parameters.AddWithValue("$can_id", stop.Frame.CanId);
            command.Parameters.AddWithValue("$dlc", stop.Frame.Dlc);
            command.Parameters.AddWithValue("$data", stop.Frame.Data);
        } else {
            command.Parameters.AddWithValue("$bus", DBNull.Value);
            command.Parameters.AddWithValue("$can_id", DBNull.Value);
            command.Parameters.AddWithValue("$dlc", DBNull.Value);
            command.Parameters.AddWithValue("$data", DBNull.Value);
        }
        command.ExecuteNonQuery();
    }

    private long GetLastInsertRowId(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT last_insert_rowid();";
        object? value = command.ExecuteScalar();
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private int GetNextVersionNumber(SqliteConnection connection, long profileId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT COALESCE(MAX(version_number), 0) + 1
FROM profile_versions
WHERE profile_id = $profile_id;";
        command.Parameters.AddWithValue("$profile_id", profileId);
        object? value = command.ExecuteScalar();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private bool VersionExists(SqliteConnection connection, long profileId, int versionNumber)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT 1
FROM profile_versions
WHERE profile_id = $profile_id AND version_number = $version_number
LIMIT 1;";
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$version_number", versionNumber);
        object? value = command.ExecuteScalar();
        return value is not null && value is not DBNull;
    }
}
