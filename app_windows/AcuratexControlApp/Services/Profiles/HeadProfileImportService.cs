using AcuratexControlApp.Data.Sqlite.Importers;
using AcuratexControlApp.Repositories.Profiles;
using AcuratexControlApp.Models.Profiles;

namespace AcuratexControlApp.Services.Profiles;

public sealed class HeadProfileImportService
{
    private readonly IProfileRepository _repository;
    private readonly ProfileValidator _validator;

    public HeadProfileImportService(IProfileRepository repository, ProfileValidator validator)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public void ImportInitialProfilesIfMissing()
    {
        foreach (InitialProfileSeed seed in InitialProfileSeeds.All) {
            ImportSeedIfMissing(seed);
        }
    }

    private void ImportSeedIfMissing(InitialProfileSeed seed)
    {
        _validator.ValidateCreateRequestOrThrow(seed.CreateRequest);
        _validator.ValidateVersionWriteRequestOrThrow(seed.VersionRequest);

        ProfileRecord? existing = _repository.GetProfileByKey(seed.CreateRequest.ProfileKey);
        if (existing is null) {
            ProfileRecord created = _repository.CreateProfile(seed.CreateRequest);
            _repository.SaveNewVersion(created.Id, seed.VersionRequest);
            return;
        }

        if (!existing.Versions.Any(version => version.VersionNumber == seed.VersionRequest.VersionNumber)) {
            _repository.SaveNewVersion(existing.Id, seed.VersionRequest);
        }
    }
}

