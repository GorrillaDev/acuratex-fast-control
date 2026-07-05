using AcuratexControlApp.Data.Sqlite;
using AcuratexControlApp.Models.Profiles;
using AcuratexControlApp.Repositories.Profiles;
using Microsoft.Data.Sqlite;

namespace AcuratexControlApp.Services.Profiles.Acx;

public sealed class AcxProfileExportService : IAcxProfileExportService
{
    private readonly IProfileRepository _repository;
    private readonly IAcxProfileCompiler _compiler;
    private readonly AcxProfilePackageReader _reader;
    private readonly ProfileValidator _validator;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ProfileDatabaseOptions _options;

    public AcxProfileExportService(
        IProfileRepository repository,
        IAcxProfileCompiler compiler,
        AcxProfilePackageReader reader,
        ProfileValidator validator,
        SqliteConnectionFactory connectionFactory,
        ProfileDatabaseOptions options)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<AcxCompilationResult> ExportProfileVersionAsync(long profileVersionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProfileVersionDocument document = _repository.ReadVersion(profileVersionId);
        return ExportDocumentAsync(document, cancellationToken);
    }

    public Task<AcxCompilationResult> ExportLatestProfileAsync(string profileKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(profileKey)) {
            throw new ArgumentException("Profile key cannot be empty.", nameof(profileKey));
        }

        ProfileRecord profile = _repository.GetProfileByKey(profileKey)
            ?? throw new InvalidOperationException($"Profile '{profileKey}' was not found.");

        ProfileVersionSummary latestVersion = profile.Versions
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Profile '{profile.ProfileKey}' has no versions.");

        return ExportDocumentAsync(_repository.ReadVersion(latestVersion.Id), cancellationToken);
    }

    private async Task<AcxCompilationResult> ExportDocumentAsync(ProfileVersionDocument document, CancellationToken cancellationToken)
    {
        _validator.ValidateVersionDocumentOrThrow(document);

        AcxCompilationResult compiled = _compiler.Compile(document);
        string exportDirectory = _options.ExportDirectory;
        Directory.CreateDirectory(exportDirectory);

        string fileName = AcxFormatConstants.BuildFileName(document.Profile.ProfileKey, document.Version.VersionNumber);
        string finalPath = Path.Combine(exportDirectory, fileName);
        string tempPath = finalPath + ".tmp";

        try {
            await WriteTempFileAsync(tempPath, compiled.PackageBytes, cancellationToken).ConfigureAwait(false);
            AcxProfilePackage tempPackage = _reader.Read(tempPath);
            _validator.ValidateVersionDocumentOrThrow(tempPackage.Document);

            if (File.Exists(finalPath)) {
                File.Delete(finalPath);
            }

            File.Move(tempPath, finalPath);

            AcxProfilePackage finalPackage = _reader.Read(finalPath);
            _validator.ValidateVersionDocumentOrThrow(finalPackage.Document);
            RegisterExportedPackage(document.Version.Id, finalPath, compiled.PayloadCrc32, compiled.PackageSizeBytes);

            return compiled with
            {
                FilePath = finalPath,
                Document = finalPackage.Document,
                Header = finalPackage.Header,
                Sections = finalPackage.Sections,
            };
        } catch {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static async Task WriteTempFileAsync(string tempPath, byte[] bytes, CancellationToken cancellationToken)
    {
        if (File.Exists(tempPath)) {
            File.Delete(tempPath);
        }

        await using FileStream stream = new(
            tempPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            });

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RegisterExportedPackage(long profileVersionId, string finalPath, uint crc32, int packageSize)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO exported_packages(profile_version_id, package_format, package_path, package_crc32, package_size)
VALUES ($profile_version_id, $package_format, $package_path, $package_crc32, $package_size);";
        command.Parameters.AddWithValue("$profile_version_id", profileVersionId);
        command.Parameters.AddWithValue("$package_format", AcxFormatConstants.PackageFormatTag);
        command.Parameters.AddWithValue("$package_path", finalPath);
        command.Parameters.AddWithValue("$package_crc32", crc32);
        command.Parameters.AddWithValue("$package_size", packageSize);
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
