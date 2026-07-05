using System.Globalization;
using AcuratexControlApp.Models.Profiles;
using Microsoft.Data.Sqlite;

namespace AcuratexControlApp.Repositories.Profiles;

public sealed partial class SqliteProfileRepository
{
    private IReadOnlyList<ProfileActionModel> LoadActions(SqliteConnection connection, long profileVersionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    id,
    action_name,
    category,
    enabled
FROM actions
WHERE profile_version_id = $profile_version_id
ORDER BY action_name COLLATE NOCASE ASC, id ASC;";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);

        List<ProfileActionModel> actions = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) {
            long actionId = reader.GetInt64(reader.GetOrdinal("id"));
            string actionName = reader.GetString(reader.GetOrdinal("action_name"));
            string category = reader.GetString(reader.GetOrdinal("category"));
            bool enabled = reader.GetInt32(reader.GetOrdinal("enabled")) != 0;
            actions.Add(new ProfileActionModel(
                Id: actionId,
                ActionName: actionName,
                Category: category,
                Enabled: enabled,
                Steps: LoadActionSteps(connection, actionId)));
        }

        return actions;
    }

    private IReadOnlyList<ProfileActionStepModel> LoadActionSteps(SqliteConnection connection, long actionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    step_order,
    step_type,
    bus,
    can_id,
    dlc,
    data,
    wait_ms
FROM action_steps
WHERE action_id = $action_id
ORDER BY step_order ASC;";
        command.Parameters.AddWithValue("$action_id", actionId);

        List<ProfileActionStepModel> steps = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) {
            HeadInitStepKind stepKind = ProfileSqliteMappings.ParseHeadInitStepKind(reader.GetString(reader.GetOrdinal("step_type")));
            steps.Add(new ProfileActionStepModel(
                StepOrder: reader.GetInt32(reader.GetOrdinal("step_order")),
                StepKind: stepKind,
                Bus: ReadNullableInt32(reader, "bus"),
                CanId: ReadNullableUInt32(reader, "can_id"),
                Dlc: ReadNullableByte(reader, "dlc"),
                Data: ReadNullableBlob(reader, "data"),
                WaitMs: ReadNullableInt32(reader, "wait_ms")));
        }

        return steps;
    }

    private void InsertActions(SqliteConnection connection, SqliteTransaction transaction, long profileVersionId, IReadOnlyList<ProfileActionModel> actions)
    {
        for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++) {
            ProfileActionModel action = actions[actionIndex];
            using SqliteCommand actionCommand = connection.CreateCommand();
            actionCommand.Transaction = transaction;
            actionCommand.CommandText = @"
INSERT INTO actions(profile_version_id, action_name, category, enabled)
VALUES ($profile_version_id, $action_name, $category, $enabled);";
            actionCommand.Parameters.AddWithValue("$profile_version_id", profileVersionId);
            actionCommand.Parameters.AddWithValue("$action_name", action.ActionName);
            actionCommand.Parameters.AddWithValue("$category", action.Category ?? string.Empty);
            actionCommand.Parameters.AddWithValue("$enabled", action.Enabled ? 1 : 0);
            actionCommand.ExecuteNonQuery();

            long actionId = GetLastInsertRowId(connection);
            InsertActionSteps(connection, transaction, actionId, action.Steps);
        }
    }

    private void InsertActionSteps(SqliteConnection connection, SqliteTransaction transaction, long actionId, IReadOnlyList<ProfileActionStepModel> steps)
    {
        for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++) {
            ProfileActionStepModel step = steps[stepIndex];
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO action_steps(action_id, step_order, step_type, bus, can_id, dlc, data, wait_ms)
VALUES ($action_id, $step_order, $step_type, $bus, $can_id, $dlc, $data, $wait_ms);";
            command.Parameters.AddWithValue("$action_id", actionId);
            command.Parameters.AddWithValue("$step_order", step.StepOrder);
            command.Parameters.AddWithValue("$step_type", step.StepKind.ToDatabaseValue());
            command.Parameters.AddWithValue("$bus", step.Bus.HasValue ? (object)step.Bus.Value : DBNull.Value);
            command.Parameters.AddWithValue("$can_id", step.CanId.HasValue ? (object)step.CanId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$dlc", step.Dlc.HasValue ? (object)step.Dlc.Value : DBNull.Value);
            command.Parameters.AddWithValue("$data", step.Data is null ? DBNull.Value : step.Data);
            command.Parameters.AddWithValue("$wait_ms", step.WaitMs.HasValue ? (object)step.WaitMs.Value : DBNull.Value);
            command.ExecuteNonQuery();
        }
    }


    private static byte[]? ReadNullableBlob(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) {
            return null;
        }

        return (byte[])reader.GetValue(ordinal);
    }

    private static byte? ReadNullableByte(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) {
            return null;
        }

        return checked((byte)reader.GetInt32(ordinal));
    }
}
