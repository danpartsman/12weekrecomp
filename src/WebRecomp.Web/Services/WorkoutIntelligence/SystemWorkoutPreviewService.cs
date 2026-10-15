using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.ViewModels;
using WebRecomp.WorkoutIntelligence;
namespace WebRecomp.Web.Services.WorkoutIntelligence;
// Shared exercise resolution for client workouts and the coach plan preview.
public sealed class SystemWorkoutPreviewService(RecompDbContext db)
{
    private const string SystemWorkoutConfigVersionCode = "CFG-3000.0";
    private const string CsvUnmappedExerciseSourceCode = "12WR_CSV_UNMAPPED";
    private const string CsvUnmappedExerciseMechanics = "CSV_UNMAPPED_ESTIMATED";
    public async Task<SystemWorkoutPreviewViewModel?> BuildAsync(
        Guid clientId,
        Guid configVersionId,
        string systemCode,
        string defaultLoadUnitCode,
        CancellationToken cancellationToken, bool allowCatalogWrites = true)
    {
        SystemWorkoutRecipeCatalog.TryGet(
            systemCode,
            out var recipe);

        var engineWorkoutTypeCode =
            recipe?.EngineWorkoutTypeCode
            ?? systemCode;

        // System Workouts are a platform-level Engine 3.0 library, not a
        // reflection of whichever historical config version a client program
        // happens to reference. Always resolve their prescription metadata
        // from the authoritative CFG-3000.0 System Workout configuration.
        _ = configVersionId;

        var systemConfigVersionId =
            await GetSystemWorkoutConfigVersionIdAsync(
                cancellationToken);

        if (!systemConfigVersionId.HasValue)
            return null;

        var workoutType =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.ConfigVersionId
                            == systemConfigVersionId.Value
                        && x.WorkoutTypeCode
                            == engineWorkoutTypeCode
                        && x.IsActive
                        && x.IsSelectable,
                    cancellationToken);

        if (workoutType is null)
            return null;

        var slots =
            recipe?.Slots
            ?? Array.Empty<SystemWorkoutExerciseSlot>();

        // Recovery, pure cardio, interval-cardio, and class presets can be
        // duration/intensity driven. Do not invent rep-based exercise rows
        // merely to make every System Workout look populated.
        if (slots.Count == 0)
        {
            var emptyRecipeEngineCatalog =
                new Engine30WorkoutTypeCatalog();

            var emptyRecipeBaseRpe =
                emptyRecipeEngineCatalog.TryGet(
                    engineWorkoutTypeCode,
                    out var emptyRecipeEngineDefinition)
                && emptyRecipeEngineDefinition is not null
                    ? emptyRecipeEngineDefinition.DefaultRpe
                    : 5m;

            return new SystemWorkoutPreviewViewModel
            {
                SystemCode = systemCode,
                WorkoutTypeCode =
                    engineWorkoutTypeCode,
                DisplayName =
                    recipe?.DisplayName
                    ?? workoutType.DisplayName,
                Description =
                    recipe?.Description
                    ?? workoutType.Description,
                GroupName =
                    recipe?.GroupName
                    ?? "Standard",
                DefaultMinutes =
                    recipe is null
                        ? workoutType.DefaultMinutes
                        : SystemWorkoutRecipeCatalog
                            .ResolveDefaultMinutes(
                                recipe,
                                workoutType.DefaultMinutes),
                DefaultRpe =
                    recipe is null
                        ? emptyRecipeBaseRpe
                        : SystemWorkoutRecipeCatalog
                            .ResolveDefaultRpe(
                                recipe,
                                emptyRecipeBaseRpe),
                Exercises = []
            };
        }

        var candidates =
            await db.ExerciseCatalogItems
                .AsNoTracking()
                .Include(x => x.MuscleTargets)
                .Where(x => x.IsActive && (x.SourceCode != CsvUnmappedExerciseSourceCode || x.SourceExternalId.StartsWith(clientId.ToString("N") + ":")))
                .Take(1000)
                .ToListAsync(
                    cancellationToken);

        var candidateIds =
            candidates
                .Select(
                    x =>
                        x.ExerciseCatalogItemId)
                .ToArray();

        var candidatePreferences =
            await db.ClientExercisePreferences
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == clientId
                        && candidateIds.Contains(
                            x.ExerciseCatalogItemId))
                .ToDictionaryAsync(
                    x => x.ExerciseCatalogItemId,
                    cancellationToken);

        var candidateUseCounts =
            await db.DetailedWorkoutExercises
                .AsNoTracking()
                .Join(
                    db.WorkoutSessions
                        .AsNoTracking()
                        .Where(
                            x =>
                                x.ClientId == clientId),
                    exercise =>
                        exercise.WorkoutSessionId,
                    session =>
                        session.WorkoutSessionId,
                    (exercise, session) =>
                        exercise)
                .Where(
                    x =>
                        candidateIds.Contains(
                            x.ExerciseCatalogItemId))
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .Select(
                    g =>
                        new
                        {
                            ExerciseId = g.Key,
                            Count = g.Count()
                        })
                .ToDictionaryAsync(
                    x => x.ExerciseId,
                    x => x.Count,
                    cancellationToken);

        var candidateGlobalFavoriteCounts =
            await db.ClientExercisePreferences
                .AsNoTracking()
                .Where(
                    x =>
                        x.IsFavorite
                        && candidateIds.Contains(
                            x.ExerciseCatalogItemId))
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .Select(
                    g =>
                        new
                        {
                            ExerciseId = g.Key,
                            Count = g.Count()
                        })
                .ToDictionaryAsync(
                    x => x.ExerciseId,
                    x => x.Count,
                    cancellationToken);

        var selected =
            new List<(ExerciseCatalogItemRecord Exercise, SystemWorkoutExerciseSlot Slot)>();

        var selectedIds =
            new HashSet<Guid>();

        // Dated OTF recipes are a transcription of a specific published class.
        // Keep every movement in order, including repeated finishers. Catalog
        // search cannot be allowed to silently drop unfamiliar exercise names
        // or substitute another movement just because its name is similar.
        if (string.Equals(
                recipe?.GroupName,
                "Orangetheory",
                StringComparison.OrdinalIgnoreCase)
            && slots.Count > 0)
        {
            foreach (var slot in slots)
            {
                var publishedName =
                    slot.PreferredNameTerms.First();

                var exact =
                    candidates.FirstOrDefault(
                        candidate =>
                            string.Equals(
                                candidate.Name,
                                publishedName,
                                StringComparison.OrdinalIgnoreCase)
                            && candidate.SourceCode
                                != CsvUnmappedExerciseSourceCode);

                var exercise =
                    exact
                    ?? (allowCatalogWrites ? await ResolveCsvUnmappedExerciseAsync(clientId, publishedName, cancellationToken) : null);
                if (exercise is null) continue;

                selected.Add((exercise, slot));
            }
        }
        else
        {
            foreach (var slot in slots)
            {
                var match =
                    candidates
                        .Where(
                            candidate =>
                                !selectedIds.Contains(
                                    candidate.ExerciseCatalogItemId))
                        .Select(
                            candidate =>
                                new
                                {
                                    Exercise = candidate,
                                    Score =
                                        SystemRecipeExerciseScore(
                                            candidate,
                                            slot,
                                            candidatePreferences,
                                            candidateUseCounts,
                                            candidateGlobalFavoriteCounts)
                                })
                        .Where(x => x.Score > int.MinValue)
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.Exercise.Name)
                        .FirstOrDefault();
    
                if (match is null)
                    continue;
    
                selected.Add(
                    (match.Exercise, slot));
    
                selectedIds.Add(
                    match.Exercise.ExerciseCatalogItemId);
            }
    
        }

        var exercises =
            selected
                .Select(
                    item =>
                    {
                        candidatePreferences.TryGetValue(
                            item.Exercise.ExerciseCatalogItemId,
                            out var preference);

                        var setCount =
                            Math.Clamp(
                                item.Slot.Sets,
                                1,
                                20);

                        var reps =
                            Math.Clamp(
                                item.Slot.Reps,
                                1,
                                500);

                        return new DetailedWorkoutExerciseInputModel
                        {
                            ExerciseId =
                                item.Exercise.ExerciseCatalogItemId,
                            ExerciseName =
                                item.Exercise.Name,
                            Mechanics =
                                item.Exercise.Mechanics,
                            Notes = null, // Personal notes stay blank for system recipes.
                            BlockName =
                                item.Slot.BlockName,
                            BlockType =
                                item.Slot.BlockType,
                            DurationSeconds =
                                item.Slot.DurationSeconds,
                            DistanceMeters =
                                item.Slot.DistanceMeters,
                            InclinePercent =
                                item.Slot.InclinePercent,
                            EstimatedRounds =
                                item.Slot.EstimatedRounds,
                            IsEstimated =
                                item.Slot.IsEstimated,
                            WorkloadText =
                                item.Slot.WorkloadText,
                            Sets =
                                setCount,
                            Reps =
                                reps,
                            SetReps =
                                Enumerable.Repeat(
                                        reps,
                                        setCount)
                                    .ToList(),
                            MaxWeight =
                                preference?.DefaultMaxWeight,
                            LoadUnitCode =
                                NormalizeLoadUnit(
                                    preference?.LoadUnitCode,
                                    defaultLoadUnitCode)
                        };
                    })
                .ToList();

        var engineCatalog =
            new Engine30WorkoutTypeCatalog();

        var baseRpe =
            engineCatalog.TryGet(
                engineWorkoutTypeCode,
                out var engineDefinition)
            && engineDefinition is not null
                ? engineDefinition.DefaultRpe
                : 5m;

        return new SystemWorkoutPreviewViewModel
        {
            SystemCode = systemCode,
            WorkoutTypeCode =
                engineWorkoutTypeCode,
            DisplayName =
                recipe?.DisplayName
                ?? workoutType.DisplayName,
            Description =
                recipe?.Description
                ?? workoutType.Description,
            GroupName =
                recipe?.GroupName
                ?? "Standard",
            DefaultMinutes =
                recipe is null
                    ? workoutType.DefaultMinutes
                    : SystemWorkoutRecipeCatalog
                        .ResolveDefaultMinutes(
                            recipe,
                            workoutType.DefaultMinutes),
            DefaultRpe =
                recipe is null
                    ? baseRpe
                    : SystemWorkoutRecipeCatalog
                        .ResolveDefaultRpe(
                            recipe,
                            baseRpe),
            Exercises =
                exercises
        };
    }

    internal async Task<Guid?> GetSystemWorkoutConfigVersionIdAsync(
        CancellationToken cancellationToken)
    {
        return await db.ConfigVersions
            .AsNoTracking()
            .Where(
                x =>
                    x.VersionCode
                        == SystemWorkoutConfigVersionCode)
            .Select(
                x =>
                    (Guid?)x.ConfigVersionId)
            .SingleOrDefaultAsync(
                cancellationToken);
    }

    internal async Task<ExerciseCatalogItemRecord> ResolveCsvUnmappedExerciseAsync(
        Guid clientId,
        string exerciseName,
        CancellationToken cancellationToken)
    {
        var normalizedName =
            exerciseName.Trim();

        if (normalizedName.Length == 0
            || normalizedName.Length > 200)
        {
            throw new InvalidOperationException(
                "CSV unmapped exercise name is outside the supported length.");
        }

        // Technical identity is per-client and deterministic. This avoids one
        // client's arbitrary CSV label becoming another client's catalog identity.
        var normalizedKey =
            System.Text.RegularExpressions.Regex.Replace(
                    normalizedName,
                    @"\s+",
                    " ")
                .ToUpperInvariant();

        var nameHash =
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        normalizedKey)));

        var sourceExternalId =
            $"{clientId:N}:{nameHash[..32]}";

        var existing =
            await db.ExerciseCatalogItems
                .SingleOrDefaultAsync(
                    x =>
                        x.SourceCode
                            == CsvUnmappedExerciseSourceCode
                        && x.SourceExternalId
                            == sourceExternalId,
                    cancellationToken);

        var now =
            DateTime.UtcNow;

        if (existing is not null)
        {
            existing.Name =
                normalizedName;
            existing.MeasurementType =
                "reps";
            existing.Mechanics =
                CsvUnmappedExerciseMechanics;
            existing.Category =
                "CSV_UNMAPPED";
            existing.InstructionsJson =
                "[]";
            existing.IsActive =
                true;
            existing.UpdatedAtUtc =
                now;

            await db.SaveChangesAsync(
                cancellationToken);

            return existing;
        }

        var entity =
            new ExerciseCatalogItemRecord
            {
                ExerciseCatalogItemId =
                    Guid.NewGuid(),
                SourceCode =
                    CsvUnmappedExerciseSourceCode,
                SourceExternalId =
                    sourceExternalId,
                Name =
                    normalizedName,
                MeasurementType =
                    "reps",
                DifficultyLevel =
                    null,
                ForceType =
                    null,
                Mechanics =
                    CsvUnmappedExerciseMechanics,
                Category =
                    "CSV_UNMAPPED",
                InstructionsJson =
                    "[]",
                IsActive =
                    true,
                LastImportId =
                    null,
                CreatedAtUtc =
                    now,
                UpdatedAtUtc =
                    now
            };

        db.ExerciseCatalogItems.Add(
            entity);

        await db.SaveChangesAsync(
            cancellationToken);

        return entity;
    }

    internal static int SystemRecipeExerciseScore(
        ExerciseCatalogItemRecord exercise,
        SystemWorkoutExerciseSlot slot,
        IReadOnlyDictionary<Guid, ClientExercisePreferenceRecord> preferences,
        IReadOnlyDictionary<Guid, int> useCounts,
        IReadOnlyDictionary<Guid, int> globalFavoriteCounts)
    {
        if (!slot.AllowCardio
            && string.Equals(
                exercise.Category,
                "cardio",
                StringComparison.OrdinalIgnoreCase))
        {
            return int.MinValue;
        }

        if (!slot.AllowStretching
            && string.Equals(
                exercise.Category,
                "stretching",
                StringComparison.OrdinalIgnoreCase))
        {
            return int.MinValue;
        }

        var nameScore = 0;
        int? trainerSeedRank = null;

        for (var preferredIndex = 0;
             preferredIndex < slot.PreferredNameTerms.Count;
             preferredIndex++)
        {
            var match =
                SystemNameMatchScore(
                    exercise.Name,
                    slot.PreferredNameTerms[preferredIndex]);

            nameScore =
                Math.Max(
                    nameScore,
                    match);

            if (match > 0 && !trainerSeedRank.HasValue)
            {
                // PreferredNameTerms are deliberately ordered. Early terms
                // reflect the initial evidence-oriented trainer seed. Hearts
                // can later overtake this bounded starter bonus.
                trainerSeedRank =
                    preferredIndex;
            }
        }

        var muscleScore = 0;

        foreach (var target in exercise.MuscleTargets)
        {
            foreach (var muscleTerm in slot.MuscleTerms)
            {
                var textMatch =
                    Math.Max(
                        TextMatchPriority(
                            target.MuscleName,
                            muscleTerm),
                        TextMatchPriority(
                            target.MuscleSlug,
                            muscleTerm));

                if (textMatch == 0)
                    continue;

                muscleScore =
                    Math.Max(
                        muscleScore,
                        (MuscleRolePriority(
                                target.RoleCode)
                            * 400)
                        + textMatch);
            }
        }

        // Corrective / mobility slots can require an explicit movement-name
        // match so a missing dead-bug or pelvic-tilt row never falls back to an
        // unrelated abdominal exercise simply because it targets the same area.
        if (slot.RequireNameMatch && nameScore == 0)
            return int.MinValue;

        // A System Workout slot must match the intended movement by name,
        // intended muscle by catalog metadata, or both. Popularity can rank
        // appropriate candidates but can never make an unrelated exercise win.
        if (nameScore == 0 && muscleScore == 0)
            return int.MinValue;

        var score =
            (nameScore * 3)
            + muscleScore
            + SystemTrainerSeedBonus(
                trainerSeedRank);

        if (slot.PreferCompound
            && string.Equals(
                exercise.Mechanics,
                "compound",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }

        if (slot.PreferIsolation
            && string.Equals(
                exercise.Mechanics,
                "isolation",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }

        if (slot.AllowStretching
            && string.Equals(
                exercise.Category,
                "stretching",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        if (preferences.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var preference)
            && preference.IsFavorite)
        {
            score += 150;
        }

        if (useCounts.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var useCount))
        {
            score +=
                Math.Min(
                    useCount * 5,
                    75);
        }

        if (globalFavoriteCounts.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var globalFavoriteCount))
        {
            score +=
                SystemWorkoutRatingBonus(
                    globalFavoriteCount);
        }

        return score;
    }

    internal static int MuscleRolePriority(
        string? roleCode)
    {
        if (string.Equals(
                roleCode,
                "primary",
                StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(
                roleCode,
                "secondary",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(
                roleCode,
                "stabilizer",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    internal static int TextMatchPriority(
        string? value,
        string query)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        if (string.Equals(
                value,
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (value.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 75;
        }

        return value.Contains(
                query,
                StringComparison.OrdinalIgnoreCase)
            ? 50
            : 0;
    }

    internal static int SystemNameMatchScore(
        string? exerciseName,
        string? preferredTerm)
    {
        if (string.IsNullOrWhiteSpace(exerciseName)
            || string.IsNullOrWhiteSpace(preferredTerm))
        {
            return 0;
        }

        var name = exerciseName.Trim();
        var term = preferredTerm.Trim();

        if (string.Equals(
                name,
                term,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        if (name.StartsWith(
                term,
                StringComparison.OrdinalIgnoreCase))
        {
            return 850;
        }

        if (name.Contains(
                term,
                StringComparison.OrdinalIgnoreCase))
        {
            return 700;
        }

        var tokens =
            term.Split(
                new[] { ' ', '-', '/', '_' },
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);

        if (tokens.Length > 1
            && tokens.All(
                token =>
                    name.Contains(
                        token,
                        StringComparison.OrdinalIgnoreCase)))
        {
            return 550;
        }

        return 0;
    }

    internal static int SystemTrainerSeedBonus(
        int? preferredTermIndex)
    {
        if (!preferredTermIndex.HasValue)
            return 0;

        // Public trainer/program research seeds the empty-platform starting
        // order. Keep it bounded: this is a bootstrap signal, not permanent
        // authority. Earlier PreferredNameTerms receive the larger bonus.
        return Math.Max(
            25,
            300 - (preferredTermIndex.Value * 35));
    }

    internal static int SystemWorkoutRatingBonus(
        int globalFavoriteCount)
    {
        if (globalFavoriteCount <= 0)
            return 0;

        // Hearts are deliberately able to overtake the bounded trainer seed
        // once real 12WR preference data accumulates. Anatomical/movement fit
        // still qualifies candidates first, so popularity cannot make an
        // unrelated exercise eligible.
        return Math.Min(
            900,
            (int)Math.Round(
                Math.Log2(
                    globalFavoriteCount + 1d)
                * 100d,
                MidpointRounding.AwayFromZero));
    }

    internal static string NormalizeLoadUnit(
        string? requested,
        string fallback)
    {
        if (string.Equals(
                requested,
                "KG",
                StringComparison.OrdinalIgnoreCase))
        {
            return "KG";
        }

        if (string.Equals(
                requested,
                "LB",
                StringComparison.OrdinalIgnoreCase))
        {
            return "LB";
        }

        return string.Equals(
            fallback,
            "KG",
            StringComparison.OrdinalIgnoreCase)
            ? "KG"
            : "LB";
    }
}
