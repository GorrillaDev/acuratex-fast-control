using System.Globalization;
using AcuratexControlApp.Data.Sqlite;
using AcuratexControlApp.Models.Profiles;
using AcuratexControlApp.Services.Profiles;
using Microsoft.Data.Sqlite;

namespace AcuratexControlApp.Repositories.Profiles;

public sealed partial class SqliteProfileRepository : IProfileRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ProfileValidator _validator;

    public SqliteProfileRepository(SqliteConnectionFactory connectionFactory, ProfileValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public ProfileRecord CreateProfile(ProfileCreateRequest request)
    {
        _validator.ValidateCreateRequestOrThrow(request);

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        if (TryLoadProfileRecord(connection, "p.profile_key = $profile_key", cmd => cmd.Parameters.AddWithValue("$profile_key", request.ProfileKey), false) is not null) {
            throw new InvalidOperationException($"Profile key '{request.ProfileKey}' already exists.");
        }

        long profileId = InsertProfile(connection, transaction, request);
        transaction.Commit();
        return GetProfileById(profileId) ?? throw new InvalidOperationException("Profile was created but could not be reloaded.");
    }

    public ProfileRecord? GetProfileById(long profileId)
    {
        using SqliteConnection connection = OpenConnection();
        return TryLoadProfileRecord(connection, "p.id = $profile_id", cmd => cmd.Parameters.AddWithValue("$profile_id", profileId), true);
    }

    public ProfileRecord? GetProfileByKey(string profileKey)
    {
        if (string.IsNullOrWhiteSpace(profileKey)) {
            throw new ArgumentException("Profile key cannot be empty.", nameof(profileKey));
        }

        using SqliteConnection connection = OpenConnection();
        return TryLoadProfileRecord(connection, "p.profile_key = $profile_key", cmd => cmd.Parameters.AddWithValue("$profile_key", profileKey), true);
    }

    public IReadOnlyList<ProfileListItem> ListProfiles()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    p.id,
    p.profile_key,
    p.program_number,
    p.display_name,
    p.enabled,
    p.updated_utc,
    COUNT(pv.id) AS version_count,
    MAX(pv.version_number) AS latest_version_number,
    (
        SELECT pv2.source_kind
        FROM profile_versions pv2
        WHERE pv2.profile_id = p.id
        ORDER BY pv2.version_number DESC
        LIMIT 1
    ) AS latest_source_kind,
    (
        SELECT pv3.is_published
        FROM profile_versions pv3
        WHERE pv3.profile_id = p.id
        ORDER BY pv3.version_number DESC
        LIMIT 1
    ) AS latest_published
FROM profiles p
LEFT JOIN profile_versions pv ON pv.profile_id = p.id
GROUP BY p.id, p.profile_key, p.program_number, p.display_name, p.enabled, p.updated_utc
ORDER BY p.profile_key;";

        List<ProfileListItem> items = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) {
            items.Add(new ProfileListItem(
                Id: reader.GetInt64(reader.GetOrdinal("id")),
                ProfileKey: reader.GetString(reader.GetOrdinal("profile_key")),
                ProgramNumber: ReadNullableInt32(reader, "program_number"),
                DisplayName: reader.GetString(reader.GetOrdinal("display_name")),
                Enabled: reader.GetInt32(reader.GetOrdinal("enabled")) != 0,
                VersionCount: reader.GetInt32(reader.GetOrdinal("version_count")),
                LatestVersionNumber: ReadNullableInt32(reader, "latest_version_number"),
                LatestSourceKind: ReadNullableSourceKind(reader, "latest_source_kind"),
                LatestPublished: ReadNullableInt32(reader, "latest_published") is int published && published != 0,
                UpdatedUtc: ParseUtcTimestamp(reader.GetString(reader.GetOrdinal("updated_utc")))));
        }

        return items;
    }

    public ProfileVersionDocument ReadVersion(long profileVersionId)
    {
        using SqliteConnection connection = OpenConnection();
        return ReadVersionDocument(connection, profileVersionId);
    }

    public ProfileRecord DuplicateProfile(
        long sourceProfileId,
        string profileKey,
        string displayName,
        string? description = null,
        bool enabled = true)
    {
        ProfileVersionDocument source = ReadLatestVersionDocument(sourceProfileId);
        ProfileCreateRequest createRequest = new(
            ProfileKey: profileKey,
            ProgramNumber: source.Profile.ProgramNumber,
            DisplayName: displayName,
            Description: description ?? source.Profile.Description,
            Enabled: enabled);

        ProfileRecord created = CreateProfile(createRequest);
        HeadCommandProfileModel copiedCommands = source.Commands with
        {
            ProgramName = displayName,
        };

        SaveNewVersion(created.Id, new ProfileVersionWriteRequest(
            VersionNumber: 1,
            SchemaVersion: source.Version.SchemaVersion,
            Notes: $"Duplicated from {source.Profile.ProfileKey} v{source.Version.VersionNumber}",
            SourceKind: ProfileSourceKind.Manual,
            Crc32: source.Version.Crc32,
            IsPublished: false,
            Commands: copiedCommands,
            Actions: source.Actions));

        return GetProfileById(created.Id) ?? throw new InvalidOperationException("Duplicated profile could not be reloaded.");
    }

    public void SetProfileEnabled(long profileId, bool enabled)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
UPDATE profiles
SET enabled = $enabled,
    updated_utc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE id = $profile_id;";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$profile_id", profileId);
        int rows = command.ExecuteNonQuery();
        if (rows == 0) {
            throw new InvalidOperationException($"Profile '{profileId}' was not found.");
        }

        transaction.Commit();
    }

    public ProfileVersionDocument SaveNewVersion(long profileId, ProfileVersionWriteRequest request)
    {
        _validator.ValidateVersionWriteRequestOrThrow(request);

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        ProfileRecord profile = LoadProfileRecordById(connection, profileId, loadVersions: false)
            ?? throw new InvalidOperationException($"Profile '{profileId}' was not found.");

        int versionNumber = request.VersionNumber ?? GetNextVersionNumber(connection, profileId);
        if (VersionExists(connection, profileId, versionNumber)) {
            throw new InvalidOperationException($"Version {versionNumber} already exists for profile '{profile.ProfileKey}'.");
        }

        long profileVersionId = InsertProfileVersion(connection, transaction, profileId, versionNumber, request);
        InsertProfileVersionContent(connection, transaction, profileVersionId, request.Commands, request.Actions);

        using (SqliteCommand updateProfile = connection.CreateCommand()) {
            updateProfile.Transaction = transaction;
            updateProfile.CommandText = @"
UPDATE profiles
SET program_number = COALESCE(program_number, $program_number),
    updated_utc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE id = $profile_id;";
            updateProfile.Parameters.AddWithValue("$program_number", request.Commands.ProgramNumber);
            updateProfile.Parameters.AddWithValue("$profile_id", profileId);
            updateProfile.ExecuteNonQuery();
        }

        transaction.Commit();
        return ReadVersion(profileVersionId);
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        return connection;
    }

    private ProfileVersionDocument ReadLatestVersionDocument(long profileId)
    {
        ProfileRecord profile = GetProfileById(profileId) ?? throw new InvalidOperationException($"Profile '{profileId}' was not found.");
        ProfileVersionSummary latestVersion = profile.Versions
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Profile '{profile.ProfileKey}' has no versions.");

        return ReadVersion(latestVersion.Id);
    }

    private static uint ReadRequiredUInt32(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        return checked((uint)reader.GetInt64(ordinal));
    }

    private static byte ReadRequiredByte(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        return checked((byte)reader.GetInt64(ordinal));
    }

    private static byte[] ReadBlob(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) {
            return Array.Empty<byte>();
        }

        return (byte[])reader.GetValue(ordinal);
    }

    private static uint? ReadNullableUInt32(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) {
            return null;
        }

        return checked((uint)reader.GetInt64(ordinal));
    }

    private static int? ReadNullableInt32(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) {
            return null;
        }

        return reader.GetInt32(ordinal);
    }

    private static ProfileSourceKind? ReadNullableSourceKind(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) {
            return null;
        }

        return ProfileSqliteMappings.ParseProfileSourceKind(reader.GetString(ordinal));
    }

    private static DateTimeOffset ParseUtcTimestamp(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
