using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services.Engine30;

public static class Engine30WorkoutRuntimeProfileSourceCodes
{
    public const string AdminActiveProfile = "ADMIN_ACTIVE_PROFILE";
    public const string BaselineFallback = "BASELINE_FALLBACK";
}

public sealed record Engine30WorkoutRuntimeProfileProvenance(
    string ConfigVersionCode,
    Guid WorkoutTypeDefinitionId,
    string WorkoutTypeCode,
    string WorkoutDisplayName,
    string SourceCode,
    Guid? ProfileVersionId,
    string? ProfileVersionCode,
    int? ProfileVersionNumber,
    bool IsBaseline,
    DateTime? ProfileCreatedAtUtc);

public sealed record Engine30WorkoutRuntimeProfileResolution(
    Engine30WorkoutAdminProfile Profile,
    Engine30WorkoutRuntimeProfileProvenance Provenance);

public interface IEngine30WorkoutRuntimeProfileProvider
{
    Task<Engine30WorkoutRuntimeProfileResolution> ResolveAsync(
        string workoutTypeCode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the persisted ACTIVE Engine 3.0 administrator profile for a
/// workout. Runtime reads never create or mutate configuration rows.
///
/// If a workout exists in CFG-3000.0 but has no persisted active profile yet,
/// the provider returns the deterministic baseline built from the Engine 3.0
/// catalog and marks provenance as BASELINE_FALLBACK.
///
/// An existing-but-invalid active profile is NOT silently replaced by the
/// fallback. Invalid administrator configuration must fail visibly.
/// </summary>
public sealed class Engine30WorkoutRuntimeProfileProvider(
    RecompDbContext db,
    Engine30WorkoutProfileService profileService)
    : IEngine30WorkoutRuntimeProfileProvider
{
    public const string TargetConfigVersionCode = "CFG-3000.0";

    public async Task<Engine30WorkoutRuntimeProfileResolution> ResolveAsync(
        string workoutTypeCode,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(workoutTypeCode);

        var config =
            await db.ConfigVersions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.VersionCode == TargetConfigVersionCode,
                    cancellationToken)
            ?? throw new InvalidOperationException(
                $"Engine 3.0 configuration '{TargetConfigVersionCode}' was not found.");

        var workout =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .Where(x =>
                    x.ConfigVersionId == config.ConfigVersionId
                    && x.WorkoutTypeCode == code
                    && x.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Active Engine 3.0 workout '{code}' was not found in {TargetConfigVersionCode}.");

        var activeVersion =
            await db.Engine30WorkoutProfileVersions
                .AsNoTracking()
                .Where(x =>
                    x.WorkoutTypeDefinitionId == workout.WorkoutTypeDefinitionId
                    && x.IsActive)
                .OrderByDescending(x => x.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);

        if (activeVersion is not null)
        {
            var activeProfile =
                profileService.ReadProfileJson(activeVersion.ProfileJson);

            Engine30WorkoutProfileService.Validate(activeProfile);

            return new Engine30WorkoutRuntimeProfileResolution(
                activeProfile,
                new Engine30WorkoutRuntimeProfileProvenance(
                    ConfigVersionCode: TargetConfigVersionCode,
                    WorkoutTypeDefinitionId: workout.WorkoutTypeDefinitionId,
                    WorkoutTypeCode: workout.WorkoutTypeCode,
                    WorkoutDisplayName: workout.DisplayName,
                    SourceCode: Engine30WorkoutRuntimeProfileSourceCodes.AdminActiveProfile,
                    ProfileVersionId: activeVersion.Engine30WorkoutProfileVersionId,
                    ProfileVersionCode: activeVersion.VersionCode,
                    ProfileVersionNumber: activeVersion.VersionNumber,
                    IsBaseline: activeVersion.IsBaseline,
                    ProfileCreatedAtUtc: activeVersion.CreatedAtUtc));
        }

        var fallback =
            profileService.BuildBaselineProfile(workout);

        Engine30WorkoutProfileService.Validate(fallback);

        return new Engine30WorkoutRuntimeProfileResolution(
            fallback,
            new Engine30WorkoutRuntimeProfileProvenance(
                ConfigVersionCode: TargetConfigVersionCode,
                WorkoutTypeDefinitionId: workout.WorkoutTypeDefinitionId,
                WorkoutTypeCode: workout.WorkoutTypeCode,
                WorkoutDisplayName: workout.DisplayName,
                SourceCode: Engine30WorkoutRuntimeProfileSourceCodes.BaselineFallback,
                ProfileVersionId: null,
                ProfileVersionCode: null,
                ProfileVersionNumber: null,
                IsBaseline: true,
                ProfileCreatedAtUtc: null));
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Workout type code is required.",
                nameof(code));
        }

        return code.Trim().ToUpperInvariant();
    }
}
