using AcuratexControlApp.Data.Sqlite;
using AcuratexControlApp.Repositories.Profiles;
using AcuratexControlApp.Services.Profiles.Acx;
using Microsoft.Extensions.DependencyInjection;

namespace AcuratexControlApp.Services.Profiles;

public static class ProfileServiceCollectionExtensions
{
    public static IServiceCollection AddAcuratexProfiles(this IServiceCollection services, ProfileDatabaseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(options ?? ProfileDatabaseOptions.CreateDefault());
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<ProfileValidator>();
        services.AddSingleton<IProfileRepository, SqliteProfileRepository>();
        services.AddSingleton<HeadProfileImportService>();
        services.AddSingleton<IAcxProfileCompiler, AcxProfileCompiler>();
        services.AddSingleton<AcxProfilePackageReader>();
        services.AddSingleton<IAcxProfileExportService, AcxProfileExportService>();
        services.AddSingleton<ProfileDatabaseInitializer>();
        return services;
    }
}
