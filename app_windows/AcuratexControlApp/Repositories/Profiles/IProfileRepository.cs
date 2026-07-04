using AcuratexControlApp.Models.Profiles;

namespace AcuratexControlApp.Repositories.Profiles;

public interface IProfileRepository
{
    ProfileRecord CreateProfile(ProfileCreateRequest request);

    ProfileRecord? GetProfileById(long profileId);

    ProfileRecord? GetProfileByKey(string profileKey);

    IReadOnlyList<ProfileListItem> ListProfiles();

    ProfileVersionDocument SaveNewVersion(long profileId, ProfileVersionWriteRequest request);

    ProfileVersionDocument ReadVersion(long profileVersionId);

    ProfileRecord DuplicateProfile(
        long sourceProfileId,
        string profileKey,
        string displayName,
        string? description = null,
        bool enabled = true);

    void SetProfileEnabled(long profileId, bool enabled);
}
