using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30WorkoutProfileHistoryItem(
    Guid Engine30WorkoutProfileVersionId,
    int VersionNumber,
    string VersionCode,
    bool IsBaseline,
    bool IsActive,
    Guid? RestoredFromProfileVersionId,
    string? ChangeNotes,
    string? CreatedBy,
    DateTime CreatedAtUtc);

public sealed record Engine30WorkoutProfileState(
    Engine30WorkoutAdminProfile ActiveProfile,
    Engine30WorkoutProfileVersion ActiveVersion,
    Engine30WorkoutProfileVersion BaselineVersion,
    IReadOnlyList<Engine30WorkoutProfileHistoryItem> History);

/// <summary>
/// Versioned persistence for administrator-editable Engine 3.0 workout profiles.
///
/// Baseline rows are immutable. Saving or restoring always creates a NEW
/// version and switches IsActive to that new row. This preserves the full
/// calibration audit trail.
/// </summary>
public sealed class Engine30WorkoutProfileService(
    RecompDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private readonly Engine30WorkoutTypeCatalog _catalog = new();

    public async Task<Engine30WorkoutProfileState> GetOrCreateStateAsync(
        WorkoutTypeDefinition workout,
        string? createdBy,
        CancellationToken cancellationToken = default)
    {
        var versions =
            await db.Set<Engine30WorkoutProfileVersion>()
                .Where(x =>
                    x.WorkoutTypeDefinitionId ==
                    workout.WorkoutTypeDefinitionId)
                .OrderBy(x => x.VersionNumber)
                .ToListAsync(cancellationToken);

        if (versions.Count == 0)
        {
            var baselineProfile =
                BuildBaselineProfile(workout);

            var baseline =
                new Engine30WorkoutProfileVersion
                {
                    Engine30WorkoutProfileVersionId = Guid.NewGuid(),
                    WorkoutTypeDefinitionId = workout.WorkoutTypeDefinitionId,
                    VersionNumber = 1,
                    VersionCode = VersionCode(workout.WorkoutTypeCode, 1),
                    IsBaseline = true,
                    IsActive = true,
                    ProfileJson = Serialize(baselineProfile),
                    ChangeNotes = "Engine 3.0 baseline generated from the current workout definition and Engine 3.0 draft defaults.",
                    CreatedBy = NormalizeCreatedBy(createdBy),
                    CreatedAtUtc = DateTime.UtcNow
                };

            db.Set<Engine30WorkoutProfileVersion>().Add(baseline);
            await db.SaveChangesAsync(cancellationToken);

            versions.Add(baseline);
        }

        var baselineVersion =
            versions.SingleOrDefault(x => x.IsBaseline)
            ?? versions.OrderBy(x => x.VersionNumber).First();

        var activeVersion =
            versions.SingleOrDefault(x => x.IsActive)
            ?? versions.OrderByDescending(x => x.VersionNumber).First();

        return new Engine30WorkoutProfileState(
            Deserialize(activeVersion.ProfileJson),
            activeVersion,
            baselineVersion,
            versions
                .OrderByDescending(x => x.VersionNumber)
                .Select(ToHistory)
                .ToArray());
    }

    public async Task<Engine30WorkoutProfileState> CreateBaselineAsync(
        WorkoutTypeDefinition workout,
        Engine30WorkoutAdminProfile profile,
        string? createdBy,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var exists =
            await db.Set<Engine30WorkoutProfileVersion>()
                .AnyAsync(
                    x =>
                        x.WorkoutTypeDefinitionId ==
                        workout.WorkoutTypeDefinitionId,
                    cancellationToken);

        if (exists)
        {
            return await GetOrCreateStateAsync(
                workout,
                createdBy,
                cancellationToken);
        }

        Validate(profile);

        var baseline =
            new Engine30WorkoutProfileVersion
            {
                Engine30WorkoutProfileVersionId = Guid.NewGuid(),
                WorkoutTypeDefinitionId = workout.WorkoutTypeDefinitionId,
                VersionNumber = 1,
                VersionCode = VersionCode(workout.WorkoutTypeCode, 1),
                IsBaseline = true,
                IsActive = true,
                ProfileJson = Serialize(profile),
                ChangeNotes =
                    string.IsNullOrWhiteSpace(notes)
                        ? "Initial Engine 3.0 workout profile."
                        : notes.Trim(),
                CreatedBy = NormalizeCreatedBy(createdBy),
                CreatedAtUtc = DateTime.UtcNow
            };

        db.Set<Engine30WorkoutProfileVersion>().Add(baseline);
        await db.SaveChangesAsync(cancellationToken);

        return await GetOrCreateStateAsync(
            workout,
            createdBy,
            cancellationToken);
    }

    public async Task<Engine30WorkoutProfileState> SaveNewVersionAsync(
        WorkoutTypeDefinition workout,
        Engine30WorkoutAdminProfile profile,
        string? createdBy,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        Validate(profile);

        var state =
            await GetOrCreateStateAsync(
                workout,
                createdBy,
                cancellationToken);

        var serialized =
            Serialize(profile);

        if (JsonEquivalent(
            state.ActiveVersion.ProfileJson,
            serialized))
        {
            return state;
        }

        var versions =
            await db.Set<Engine30WorkoutProfileVersion>()
                .Where(x =>
                    x.WorkoutTypeDefinitionId ==
                    workout.WorkoutTypeDefinitionId)
                .ToListAsync(cancellationToken);

        foreach (var version in versions.Where(x => x.IsActive))
        {
            version.IsActive = false;
        }

        var nextVersion =
            versions.Count == 0
                ? 1
                : versions.Max(x => x.VersionNumber) + 1;

        db.Set<Engine30WorkoutProfileVersion>()
            .Add(
                new Engine30WorkoutProfileVersion
                {
                    Engine30WorkoutProfileVersionId = Guid.NewGuid(),
                    WorkoutTypeDefinitionId = workout.WorkoutTypeDefinitionId,
                    VersionNumber = nextVersion,
                    VersionCode = VersionCode(
                        workout.WorkoutTypeCode,
                        nextVersion),
                    IsBaseline = false,
                    IsActive = true,
                    ProfileJson = serialized,
                    ChangeNotes =
                        string.IsNullOrWhiteSpace(notes)
                            ? "Administrator calibration update."
                            : notes.Trim(),
                    CreatedBy = NormalizeCreatedBy(createdBy),
                    CreatedAtUtc = DateTime.UtcNow
                });

        await db.SaveChangesAsync(cancellationToken);

        return await GetOrCreateStateAsync(
            workout,
            createdBy,
            cancellationToken);
    }

    public async Task<Engine30WorkoutProfileState> RestoreVersionAsync(
        WorkoutTypeDefinition workout,
        Guid sourceVersionId,
        string? createdBy,
        CancellationToken cancellationToken = default)
    {
        var state =
            await GetOrCreateStateAsync(
                workout,
                createdBy,
                cancellationToken);

        var source =
            await db.Set<Engine30WorkoutProfileVersion>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.Engine30WorkoutProfileVersionId ==
                            sourceVersionId
                        &&
                        x.WorkoutTypeDefinitionId ==
                            workout.WorkoutTypeDefinitionId,
                    cancellationToken)
            ?? throw new InvalidOperationException(
                "The selected Engine 3.0 workout profile version was not found.");

        var versions =
            await db.Set<Engine30WorkoutProfileVersion>()
                .Where(x =>
                    x.WorkoutTypeDefinitionId ==
                    workout.WorkoutTypeDefinitionId)
                .ToListAsync(cancellationToken);

        foreach (var version in versions.Where(x => x.IsActive))
        {
            version.IsActive = false;
        }

        var nextVersion =
            versions.Max(x => x.VersionNumber) + 1;

        var restored =
            new Engine30WorkoutProfileVersion
            {
                Engine30WorkoutProfileVersionId = Guid.NewGuid(),
                WorkoutTypeDefinitionId = workout.WorkoutTypeDefinitionId,
                VersionNumber = nextVersion,
                VersionCode = VersionCode(
                    workout.WorkoutTypeCode,
                    nextVersion),
                IsBaseline = false,
                IsActive = true,
                ProfileJson = source.ProfileJson,
                RestoredFromProfileVersionId =
                    source.Engine30WorkoutProfileVersionId,
                ChangeNotes =
                    $"Restored from {source.VersionCode}.",
                CreatedBy = NormalizeCreatedBy(createdBy),
                CreatedAtUtc = DateTime.UtcNow
            };

        db.Set<Engine30WorkoutProfileVersion>().Add(restored);
        await db.SaveChangesAsync(cancellationToken);

        // Keep the legacy duration synchronized for Engine 2 compatibility.
        var restoredProfile =
            Deserialize(restored.ProfileJson);

        workout.DefaultMinutes =
            restoredProfile.DefaultDurationMinutes;

        await db.SaveChangesAsync(cancellationToken);

        return await GetOrCreateStateAsync(
            workout,
            createdBy,
            cancellationToken);
    }

    public async Task<Engine30WorkoutProfileState> RestoreBaselineAsync(
        WorkoutTypeDefinition workout,
        string? createdBy,
        CancellationToken cancellationToken = default)
    {
        var state =
            await GetOrCreateStateAsync(
                workout,
                createdBy,
                cancellationToken);

        return await RestoreVersionAsync(
            workout,
            state.BaselineVersion.Engine30WorkoutProfileVersionId,
            createdBy,
            cancellationToken);
    }


    public Engine30WorkoutAdminProfile ReadProfileJson(
        string json)
        => Deserialize(json);

    public Engine30WorkoutAdminProfile BuildBaselineProfile(
        WorkoutTypeDefinition workout)
    {
        Engine30WorkoutTypeDefinition? definition = null;

        _catalog.TryGet(
            workout.WorkoutTypeCode,
            out definition);

        var family =
            definition?.WorkoutFamilyCode
            ?? InferFamily(workout.WorkoutCategoryCode);

        var modality =
            definition?.DefaultModalityCode
            ?? InferModality(family);

        var pattern =
            definition?.TrainingPatternCode
            ?? InferPattern(
                family,
                workout.WorkoutCategoryCode);

        var structure =
            definition?.SessionStructureCode
            ?? InferStructure(family);

        var defaultRpe =
            definition?.DefaultRpe
            ?? InferRpe(family, pattern);

        var profile =
            new Engine30WorkoutAdminProfile
            {
                WorkoutFamilyCode = family,
                DefaultModalityCode = modality,
                TrainingPatternCode = pattern,
                SessionStructureCode = structure,

                DefaultDurationMinutes =
                    workout.DefaultMinutes > 0m
                        ? workout.DefaultMinutes
                        : DefaultDuration(family, pattern, modality),

                DefaultRpe = defaultRpe,

                SupportsObjectiveWorkload =
                    definition?.SupportsObjectiveWorkload
                    ?? (
                        family == Engine30WorkoutFamilyCodes.Cardio
                        || family == Engine30WorkoutFamilyCodes.Intervals
                        || family == Engine30WorkoutFamilyCodes.Composite),

                SupportsResistanceDetails =
                    definition?.SupportsResistanceDetails
                    ?? (
                        family == Engine30WorkoutFamilyCodes.Resistance
                        || family == Engine30WorkoutFamilyCodes.Composite),

                SupportsIntervalDetails =
                    definition?.SupportsIntervalDetails
                    ?? (
                        family == Engine30WorkoutFamilyCodes.Intervals
                        || family == Engine30WorkoutFamilyCodes.Composite),

                SupportsSegments =
                    definition?.SupportsSegments
                    ?? family == Engine30WorkoutFamilyCodes.Composite,

                ReferenceWorkoutCaloriesPerMinute =
                    DefaultEnergyRate(
                        workout.WorkoutTypeCode,
                        family,
                        modality,
                        pattern),

                ReferenceRpe = Math.Max(1m, defaultRpe),
                ReferenceWeightLb = 180m,

                ScalingBasisCode =
                    family == Engine30WorkoutFamilyCodes.Resistance
                        ? "FAT_FREE_MASS"
                        : "BODY_WEIGHT",

                ReferenceFatFreeMassLb =
                    family == Engine30WorkoutFamilyCodes.Resistance
                        ? 130m
                        : null,

                DefaultRestSeconds =
                    family == Engine30WorkoutFamilyCodes.Resistance
                        ? workout.DefaultRestSec > 0
                            ? workout.DefaultRestSec
                            : 90m
                        : null,

                GlycogenModelCode =
                    GlycogenModel(family),

                GlycogenReferenceDurationMinutes =
                    workout.DefaultMinutes > 0m
                        ? workout.DefaultMinutes
                        : DefaultDuration(family, pattern, modality),

                GlycogenReferenceDepletionFraction =
                    DefaultDepletion(
                        family,
                        modality,
                        pattern),

                GlycogenIntensitySensitivity =
                    family == Engine30WorkoutFamilyCodes.Intervals
                        ? 0.50m
                        : 0.50m,

                GlycogenMinimumDepletionFraction =
                    family == Engine30WorkoutFamilyCodes.Resistance
                        ? 0.05m
                        : 0.01m,

                GlycogenMaximumDepletionFraction =
                    family == Engine30WorkoutFamilyCodes.Intervals
                        ? 0.75m
                        : family == Engine30WorkoutFamilyCodes.Resistance
                            ? 0.65m
                            : 0.70m,

                IntervalWorkRpe =
                    family == Engine30WorkoutFamilyCodes.Intervals
                        ? Math.Max(1m, defaultRpe)
                        : 8m,

                IntervalRecoveryRpe = 3m
            };

        profile.MuscleRegions =
            DefaultMuscleRegions(
                workout.WorkoutTypeCode,
                family,
                modality,
                pattern);

        profile.Segments =
            DefaultSegments(
                workout.WorkoutTypeCode,
                defaultRpe);

        return profile;
    }

    public static void Validate(
        Engine30WorkoutAdminProfile profile)
    {
        if (profile.DefaultDurationMinutes < 0m)
            throw new InvalidOperationException(
                "Default duration cannot be negative.");

        if (profile.DefaultRpe is < 0m or > 10m)
            throw new InvalidOperationException(
                "Default RPE must be between 0 and 10.");

        if (profile.ReferenceWorkoutCaloriesPerMinute < 0m)
            throw new InvalidOperationException(
                "Reference workout kcal/min cannot be negative.");

        if (profile.ReferenceRpe is < 0m or > 10m)
            throw new InvalidOperationException(
                "Reference RPE must be between 0 and 10.");

        if (profile.ReferenceWeightLb <= 0m)
            throw new InvalidOperationException(
                "Reference body weight must be greater than zero.");

        if (profile.GlycogenReferenceDepletionFraction is < 0m or > 1m)
            throw new InvalidOperationException(
                "Glycogen reference depletion must be between 0 and 1.");

        if (profile.GlycogenMinimumDepletionFraction is < 0m or > 1m
            ||
            profile.GlycogenMaximumDepletionFraction is < 0m or > 1m
            ||
            profile.GlycogenMinimumDepletionFraction >
            profile.GlycogenMaximumDepletionFraction)
        {
            throw new InvalidOperationException(
                "Glycogen depletion bounds are invalid.");
        }

        foreach (var region in profile.MuscleRegions)
        {
            if (string.IsNullOrWhiteSpace(region.MuscleRegionCode))
                continue;

            if (region.WholeBodyMuscleFraction is < 0m or > 1m)
                throw new InvalidOperationException(
                    $"Muscle fraction for {region.MuscleRegionCode} must be between 0 and 1.");

            if (region.RecruitmentFraction is < 0m or > 1m)
                throw new InvalidOperationException(
                    $"Recruitment for {region.MuscleRegionCode} must be between 0 and 1.");

            if (region.HardSets < 0m)
                throw new InvalidOperationException(
                    $"Hard sets for {region.MuscleRegionCode} cannot be negative.");
        }

        foreach (var segment in profile.Segments.Where(x => x.Enabled))
        {
            if (string.IsNullOrWhiteSpace(segment.SegmentCode))
                throw new InvalidOperationException(
                    "Enabled composite segments require a segment code.");

            if (segment.DurationMinutes <= 0m)
                throw new InvalidOperationException(
                    $"Segment {segment.SegmentCode} requires a positive duration.");

            if (segment.DefaultRpe is < 0m or > 10m)
                throw new InvalidOperationException(
                    $"Segment {segment.SegmentCode} RPE must be between 0 and 10.");

            if (segment.GlycogenReferenceDepletionFraction is < 0m or > 1m)
                throw new InvalidOperationException(
                    $"Segment {segment.SegmentCode} depletion must be between 0 and 1.");
        }
    }

    public static decimal ImpliedMetAt210Lb(
        Engine30WorkoutAdminProfile profile)
    {
        if (profile.ReferenceWorkoutCaloriesPerMinute <= 0m)
            return 0m;

        const decimal poundsPerKg = 2.2046226218m;
        const decimal subjectWeightLb = 210m;
        const decimal subjectFfmLb = 168m;

        var scale =
            profile.ScalingBasisCode.Equals(
                "FAT_FREE_MASS",
                StringComparison.OrdinalIgnoreCase)
            &&
            profile.ReferenceFatFreeMassLb.HasValue
            &&
            profile.ReferenceFatFreeMassLb.Value > 0m
                ? subjectFfmLb /
                  profile.ReferenceFatFreeMassLb.Value
                : subjectWeightLb /
                  profile.ReferenceWeightLb;

        static decimal IntensityFactor(decimal rpe)
            => Math.Clamp(
                0.70m + rpe * 0.04m,
                0.70m,
                1.20m);

        var referenceIntensity =
            IntensityFactor(profile.ReferenceRpe);

        var defaultIntensity =
            IntensityFactor(profile.DefaultRpe);

        var intensityAdjustment =
            referenceIntensity > 0m
                ? defaultIntensity / referenceIntensity
                : 1m;

        var grossKcalPerMinute =
            profile.ReferenceWorkoutCaloriesPerMinute
            * scale
            * intensityAdjustment;

        return grossKcalPerMinute
               * 60m
               / (subjectWeightLb / poundsPerKg);
    }

    private static string Serialize(
        Engine30WorkoutAdminProfile profile)
        => JsonSerializer.Serialize(
            profile,
            JsonOptions);

    private static Engine30WorkoutAdminProfile Deserialize(
        string json)
        => JsonSerializer.Deserialize<Engine30WorkoutAdminProfile>(
               json,
               JsonOptions)
           ?? throw new InvalidOperationException(
               "Stored Engine 3.0 workout profile JSON could not be read.");

    private static bool JsonEquivalent(
        string left,
        string right)
    {
        using var leftDoc = JsonDocument.Parse(left);
        using var rightDoc = JsonDocument.Parse(right);

        return JsonSerializer.Serialize(
                   leftDoc.RootElement,
                   JsonOptions)
               ==
               JsonSerializer.Serialize(
                   rightDoc.RootElement,
                   JsonOptions);
    }

    private static Engine30WorkoutProfileHistoryItem ToHistory(
        Engine30WorkoutProfileVersion version)
        => new(
            version.Engine30WorkoutProfileVersionId,
            version.VersionNumber,
            version.VersionCode,
            version.IsBaseline,
            version.IsActive,
            version.RestoredFromProfileVersionId,
            version.ChangeNotes,
            version.CreatedBy,
            version.CreatedAtUtc);

    private static string VersionCode(
        string workoutTypeCode,
        int versionNumber)
        => $"E30-{workoutTypeCode.ToUpperInvariant()}-V{versionNumber:0000}";

    private static string? NormalizeCreatedBy(
        string? createdBy)
        => string.IsNullOrWhiteSpace(createdBy)
            ? "Administrator"
            : createdBy.Trim();

    private static string InferFamily(
        string category)
        => category.ToUpperInvariant() switch
        {
            WorkoutCategoryCatalog.MajorStrength =>
                Engine30WorkoutFamilyCodes.Resistance,

            WorkoutCategoryCatalog.AccessoryStrength =>
                Engine30WorkoutFamilyCodes.Resistance,

            WorkoutCategoryCatalog.ConditioningCardio =>
                Engine30WorkoutFamilyCodes.Cardio,

            _ =>
                Engine30WorkoutFamilyCodes.Recovery
        };

    private static string InferModality(
        string family)
        => family switch
        {
            Engine30WorkoutFamilyCodes.Resistance =>
                Engine30WorkoutModalityCodes.Resistance,

            Engine30WorkoutFamilyCodes.Composite =>
                Engine30WorkoutModalityCodes.Mixed,

            Engine30WorkoutFamilyCodes.Recovery =>
                Engine30WorkoutModalityCodes.Mobility,

            _ =>
                Engine30WorkoutModalityCodes.GeneralCardio
        };

    private static string InferPattern(
        string family,
        string category)
        => family switch
        {
            Engine30WorkoutFamilyCodes.Resistance =>
                category.Equals(
                    WorkoutCategoryCatalog.AccessoryStrength,
                    StringComparison.OrdinalIgnoreCase)
                    ? Engine30TrainingPatternCodes.ResistanceAccessory
                    : Engine30TrainingPatternCodes.ResistanceMajor,

            Engine30WorkoutFamilyCodes.Recovery =>
                Engine30TrainingPatternCodes.Recovery,

            Engine30WorkoutFamilyCodes.Intervals =>
                Engine30TrainingPatternCodes.Intervals,

            Engine30WorkoutFamilyCodes.Composite =>
                Engine30TrainingPatternCodes.Composite,

            _ =>
                Engine30TrainingPatternCodes.Steady
        };

    private static string InferStructure(
        string family)
        => family switch
        {
            Engine30WorkoutFamilyCodes.Resistance =>
                Engine30WorkoutSessionCodes.StructureResistanceSets,

            Engine30WorkoutFamilyCodes.Intervals =>
                Engine30WorkoutSessionCodes.StructureIntervals,

            Engine30WorkoutFamilyCodes.Composite =>
                Engine30WorkoutSessionCodes.StructureComposite,

            _ =>
                Engine30WorkoutSessionCodes.StructureContinuous
        };

    private static decimal InferRpe(
        string family,
        string pattern)
        => family switch
        {
            Engine30WorkoutFamilyCodes.Resistance =>
                pattern == Engine30TrainingPatternCodes.ResistanceAccessory
                    ? 7m
                    : 8m,

            Engine30WorkoutFamilyCodes.Intervals =>
                pattern == Engine30TrainingPatternCodes.Hiit
                    ? 9m
                    : 8m,

            Engine30WorkoutFamilyCodes.Composite =>
                7m,

            Engine30WorkoutFamilyCodes.Recovery =>
                2m,

            _ =>
                4m
        };

    private static decimal DefaultDuration(
        string family,
        string pattern,
        string modality)
        => family switch
        {
            Engine30WorkoutFamilyCodes.Recovery =>
                20m,

            Engine30WorkoutFamilyCodes.Resistance =>
                pattern == Engine30TrainingPatternCodes.ResistanceAccessory
                    ? 45m
                    : 60m,

            Engine30WorkoutFamilyCodes.Intervals =>
                30m,

            Engine30WorkoutFamilyCodes.Composite =>
                60m,

            _ when modality == Engine30WorkoutModalityCodes.Running =>
                40m,

            _ when modality == Engine30WorkoutModalityCodes.StairClimber =>
                30m,

            _ =>
                45m
        };

    private static string GlycogenModel(
        string family)
        => family switch
        {
            Engine30WorkoutFamilyCodes.Recovery =>
                "ZERO",

            Engine30WorkoutFamilyCodes.Resistance =>
                "RESISTANCE",

            Engine30WorkoutFamilyCodes.Intervals =>
                "INTERVAL",

            Engine30WorkoutFamilyCodes.Composite =>
                "SEQUENTIAL_COMPOSITE",

            _ =>
                "CONTINUOUS"
        };

    private static decimal DefaultEnergyRate(
        string workoutTypeCode,
        string family,
        string modality,
        string pattern)
    {
        decimal rate =
            modality.ToUpperInvariant() switch
            {
                "NONE" => 0m,
                "MOBILITY" => 2.2m,
                "GENERAL_CARDIO" => 5.5m,
                "WALKING" => 3.8m,
                "RUNNING" => 9.0m,
                "TREADMILL" => 7.5m,
                "CYCLING" => 6.0m,
                "ROWING" => 7.0m,
                "ELLIPTICAL" => 6.2m,
                "STAIR_CLIMBER" => 8.0m,
                "SWIMMING" => 7.5m,
                "RESISTANCE" => 5.5m,
                "MIXED" => 7.0m,
                _ => 5.5m
            };

        if (family == Engine30WorkoutFamilyCodes.Intervals)
        {
            rate =
                modality.ToUpperInvariant() switch
                {
                    "RUNNING" => 11.5666m,
                    "CYCLING" => 11.9748m,
                    "ROWING" => 10.2058m,
                    "MIXED" => 9.5254m,
                    "GENERAL_CARDIO" => 9.5254m,
                    _ => rate
                };
        }

        if (family == Engine30WorkoutFamilyCodes.Resistance
            &&
            workoutTypeCode.Contains(
                "LEGS",
                StringComparison.OrdinalIgnoreCase))
        {
            rate *= 1.10m;
        }

        if (pattern == Engine30TrainingPatternCodes.ResistanceAccessory)
        {
            rate *= 0.88m;
        }

        return rate;
    }

    private static decimal DefaultDepletion(
        string family,
        string modality,
        string pattern)
    {
        if (family == Engine30WorkoutFamilyCodes.Recovery)
            return 0m;

        if (family == Engine30WorkoutFamilyCodes.Resistance)
            return 0.28m;

        if (family == Engine30WorkoutFamilyCodes.Intervals)
            return pattern == Engine30TrainingPatternCodes.Hiit
                ? 0.30m
                : 0.22m;

        if (family == Engine30WorkoutFamilyCodes.Composite)
            return 0.18m;

        if (pattern == Engine30TrainingPatternCodes.Easy)
            return 0.06m;

        if (pattern == Engine30TrainingPatternCodes.Zone2)
            return 0.10m;

        if (modality == Engine30WorkoutModalityCodes.Walking)
            return 0.07m;

        return 0.14m;
    }

    private static List<Engine30WorkoutAdminMuscleRegion> DefaultMuscleRegions(
        string workoutTypeCode,
        string family,
        string modality,
        string pattern)
    {
        if (family == Engine30WorkoutFamilyCodes.Recovery)
            return [];

        if (family == Engine30WorkoutFamilyCodes.Resistance)
        {
            if (workoutTypeCode.Contains(
                    "LEGS",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    Region("LOWER_BODY", 0.50m, 0.85m, 8m)
                ];
            }

            if (workoutTypeCode.Contains(
                    "PUSH",
                    StringComparison.OrdinalIgnoreCase)
                ||
                workoutTypeCode.Contains(
                    "PULL",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    Region("UPPER_BODY", 0.20m, 0.80m, 8m)
                ];
            }

            if (workoutTypeCode.Contains(
                    "FULL",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    Region("LOWER_BODY", 0.50m, 0.70m, 6m),
                    Region("UPPER_BODY", 0.20m, 0.70m, 6m)
                ];
            }

            if (pattern ==
                Engine30TrainingPatternCodes.ResistanceCircuit)
            {
                return
                [
                    Region("LOWER_BODY", 0.50m, 0.65m, 6m),
                    Region("UPPER_BODY", 0.20m, 0.65m, 6m)
                ];
            }

            return
            [
                Region("LOWER_BODY", 0.50m, 0.45m, 4m),
                Region("UPPER_BODY", 0.20m, 0.60m, 4m)
            ];
        }

        if (modality == Engine30WorkoutModalityCodes.Rowing)
        {
            return
            [
                Region("LOWER_BODY", 0.50m, 0.75m, 0m),
                Region("UPPER_BODY", 0.20m, 0.45m, 0m)
            ];
        }

        if (modality == Engine30WorkoutModalityCodes.Swimming)
        {
            return
            [
                Region("LOWER_BODY", 0.50m, 0.45m, 0m),
                Region("UPPER_BODY", 0.20m, 0.65m, 0m)
            ];
        }

        if (modality == Engine30WorkoutModalityCodes.Mixed)
        {
            return
            [
                Region("LOWER_BODY", 0.50m, 0.70m, 0m),
                Region("UPPER_BODY", 0.20m, 0.60m, 0m)
            ];
        }

        var recruitment =
            modality switch
            {
                Engine30WorkoutModalityCodes.StairClimber => 0.80m,
                Engine30WorkoutModalityCodes.Running => 0.75m,
                Engine30WorkoutModalityCodes.Treadmill => 0.70m,
                Engine30WorkoutModalityCodes.Cycling => 0.70m,
                Engine30WorkoutModalityCodes.Elliptical => 0.65m,
                Engine30WorkoutModalityCodes.Walking => 0.55m,
                _ => 0.65m
            };

        return
        [
            Region("LOWER_BODY", 0.50m, recruitment, 0m)
        ];
    }

    private static Engine30WorkoutAdminMuscleRegion Region(
        string code,
        decimal muscleFraction,
        decimal recruitment,
        decimal hardSets)
        => new()
        {
            MuscleRegionCode = code,
            WholeBodyMuscleFraction = muscleFraction,
            RecruitmentFraction = recruitment,
            HardSets = hardSets
        };

    private static List<Engine30WorkoutAdminSegment> DefaultSegments(
        string workoutTypeCode,
        decimal workoutRpe)
    {
        if (workoutTypeCode.Equals(
            "ORANGETHEORY",
            StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                Segment(
                    10,
                    "TREAD",
                    "Treadmill",
                    Engine30WorkoutModalityCodes.Treadmill,
                    Engine30TrainingPatternCodes.Steady,
                    Engine30WorkoutSessionCodes.StructureContinuous,
                    20m,
                    7m,
                    7.5m,
                    "BODY_WEIGHT",
                    "CONTINUOUS",
                    0.14m,
                    lowerRecruitment: 0.70m),

                Segment(
                    20,
                    "ROW",
                    "Row",
                    Engine30WorkoutModalityCodes.Rowing,
                    Engine30TrainingPatternCodes.Intervals,
                    Engine30WorkoutSessionCodes.StructureIntervals,
                    20m,
                    8m,
                    7.0m,
                    "BODY_WEIGHT",
                    "INTERVAL",
                    0.18m,
                    lowerRecruitment: 0.75m,
                    upperRecruitment: 0.45m),

                Segment(
                    30,
                    "FLOOR",
                    "Floor",
                    Engine30WorkoutModalityCodes.Resistance,
                    Engine30TrainingPatternCodes.ResistanceCircuit,
                    Engine30WorkoutSessionCodes.StructureResistanceSets,
                    20m,
                    7m,
                    5.0m,
                    "FAT_FREE_MASS",
                    "RESISTANCE",
                    0.21m,
                    lowerRecruitment: 0.60m,
                    upperRecruitment: 0.70m,
                    hardSetsPerRegion: 4m)
            ];
        }

        if (workoutTypeCode.Equals(
                "MIXED_GYM_SESSION",
                StringComparison.OrdinalIgnoreCase)
            ||
            workoutTypeCode.Equals(
                "CUSTOM_COMPOSITE",
                StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                Segment(
                    10,
                    "CARDIO",
                    "Cardio",
                    Engine30WorkoutModalityCodes.Mixed,
                    Engine30TrainingPatternCodes.Steady,
                    Engine30WorkoutSessionCodes.StructureContinuous,
                    30m,
                    workoutRpe,
                    6.5m,
                    "BODY_WEIGHT",
                    "CONTINUOUS",
                    0.18m,
                    lowerRecruitment: 0.70m,
                    upperRecruitment: 0.60m),

                Segment(
                    20,
                    "RESISTANCE",
                    "Resistance",
                    Engine30WorkoutModalityCodes.Resistance,
                    Engine30TrainingPatternCodes.ResistanceCircuit,
                    Engine30WorkoutSessionCodes.StructureResistanceSets,
                    30m,
                    workoutRpe,
                    5.3m,
                    "FAT_FREE_MASS",
                    "RESISTANCE",
                    0.21m,
                    lowerRecruitment: 0.60m,
                    upperRecruitment: 0.65m,
                    hardSetsPerRegion: 4m)
            ];
        }

        return [];
    }

    private static Engine30WorkoutAdminSegment Segment(
        int order,
        string code,
        string display,
        string modality,
        string pattern,
        string structure,
        decimal duration,
        decimal rpe,
        decimal kcalPerMinute,
        string scaling,
        string glycogenModel,
        decimal depletion,
        decimal lowerRecruitment = 0m,
        decimal upperRecruitment = 0m,
        decimal hardSetsPerRegion = 0m)
        => new()
        {
            Enabled = true,
            DisplayOrder = order,
            SegmentCode = code,
            DisplayName = display,
            ModalityCode = modality,
            TrainingPatternCode = pattern,
            SessionStructureCode = structure,
            DurationMinutes = duration,
            DefaultRpe = rpe,
            ReferenceWorkoutCaloriesPerMinute = kcalPerMinute,
            ReferenceRpe = Math.Max(1m, rpe),
            ScalingBasisCode = scaling,
            ReferenceFatFreeMassLb =
                scaling == "FAT_FREE_MASS"
                    ? 130m
                    : null,
            DefaultRestSeconds =
                scaling == "FAT_FREE_MASS"
                    ? 90m
                    : null,
            GlycogenModelCode = glycogenModel,
            GlycogenReferenceDepletionFraction = depletion,
            LowerBodyRecruitmentFraction = lowerRecruitment,
            UpperBodyRecruitmentFraction = upperRecruitment,
            HardSetsPerRegion = hardSetsPerRegion
        };
}
