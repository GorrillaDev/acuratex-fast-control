using Microsoft.Data.Sqlite;

namespace AcuratexControlApp.Data.Sqlite;

public sealed class SqliteConnectionFactory
{
    private readonly ProfileDatabaseOptions _options;

    public SqliteConnectionFactory(ProfileDatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string DatabasePath => _options.DatabasePath;

    public SqliteConnection CreateConnection()
    {
        string? directory = Path.GetDirectoryName(_options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        };

        return new SqliteConnection(builder.ToString());
    }
}
