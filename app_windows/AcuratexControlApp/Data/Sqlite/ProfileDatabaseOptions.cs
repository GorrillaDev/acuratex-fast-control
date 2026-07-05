namespace AcuratexControlApp.Data.Sqlite;

public sealed record ProfileDatabaseOptions(string DatabaseDirectory, string DatabaseFileName)
{
    public const string DefaultDatabaseFileName = "acuratex_profiles.db";

    public string DatabasePath => Path.Combine(DatabaseDirectory, DatabaseFileName);

    public string ExportDirectory => Path.Combine(DatabaseDirectory, "Exports");

    public static ProfileDatabaseOptions CreateDefault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string databaseDirectory = Path.Combine(localAppData, "AcuratexFastControl", "AcuratexControlApp");
        return new ProfileDatabaseOptions(databaseDirectory, DefaultDatabaseFileName);
    }

    public static ProfileDatabaseOptions ForRoot(string databaseDirectory, string databaseFileName = DefaultDatabaseFileName)
    {
        if (string.IsNullOrWhiteSpace(databaseDirectory)) {
            throw new ArgumentException("Database directory cannot be empty.", nameof(databaseDirectory));
        }

        string fullDirectory = Path.GetFullPath(databaseDirectory);
        return new ProfileDatabaseOptions(fullDirectory, databaseFileName);
    }
}
