using System.Reflection;
using AcuratexControlApp.Services.Profiles;
using Microsoft.Data.Sqlite;

namespace AcuratexControlApp.Data.Sqlite;

public sealed class ProfileDatabaseInitializer
{
    private const string SchemaResourceName = "AcuratexControlApp.Data.Sqlite.ESQUEMA_SQLITE_ACURATEX_V1.sql";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly HeadProfileImportService _importService;

    public ProfileDatabaseInitializer(SqliteConnectionFactory connectionFactory, HeadProfileImportService importService)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
    }

    public void EnsureInitialized()
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        ExecuteSchema(connection);
        _importService.ImportInitialProfilesIfMissing();
    }

    private static void ExecuteSchema(SqliteConnection connection)
    {
        string schemaSql = ReadSchemaResource();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = schemaSql;
        command.ExecuteNonQuery();
    }

    private static string ReadSchemaResource()
    {
        Assembly assembly = typeof(ProfileDatabaseInitializer).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(SchemaResourceName);
        if (stream is null) {
            throw new InvalidOperationException($"Embedded schema resource '{SchemaResourceName}' was not found.");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
