using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Search;
using WebRecomp.WorkoutIntelligence.Catalog;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Services.WorkoutIntelligence;

public sealed class SqlExerciseCatalogStore(RecompDbContext db) : IExerciseCatalogStore
{
    public async Task<ExerciseCatalogImportResult> ImportSnapshotAsync(
        ExerciseCatalogSnapshot snapshot,
        ExerciseCatalogProvenance provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(provenance);

        if (!string.Equals(snapshot.SourceCode, provenance.SourceCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Catalog snapshot source does not match provenance source.");

        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var import = new ExerciseCatalogImportRecord
        {
            ExerciseCatalogImportId = Guid.NewGuid(),
            SourceCode = provenance.SourceCode,
            LicenseCode = provenance.LicenseCode,
            SourceVersion = provenance.SourceVersion,
            ManifestSha256 = provenance.ManifestSha256,
            CapturedAtUtc = provenance.CapturedAtUtc,
            ExerciseCount = snapshot.Exercises.Count,
            CreatedAtUtc = now
        };
        db.ExerciseCatalogImports.Add(import);

        var existing = await db.ExerciseCatalogItems
            .Include(x => x.MuscleTargets)
            .Include(x => x.Equipment)
            .Where(x => x.SourceCode == provenance.SourceCode)
            .ToListAsync(cancellationToken);

        var existingByExternalId = existing.ToDictionary(x => x.SourceExternalId, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;

        foreach (var item in snapshot.Exercises)
        {
            if (!seen.Add(item.SourceExternalId))
                throw new InvalidDataException($"Duplicate exercise external id: {item.SourceExternalId}");

            if (!existingByExternalId.TryGetValue(item.SourceExternalId, out var row))
            {
                row = new ExerciseCatalogItemRecord
                {
                    ExerciseCatalogItemId = item.Id,
                    SourceCode = item.SourceCode,
                    SourceExternalId = item.SourceExternalId,
                    Name = item.Name,
                    MeasurementType = item.MeasurementType,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                db.ExerciseCatalogItems.Add(row);
                added++;
            }
            else
            {
                updated++;
                db.ExerciseMuscleTargets.RemoveRange(row.MuscleTargets);
                db.ExerciseEquipmentRequirements.RemoveRange(row.Equipment);
                row.MuscleTargets.Clear();
                row.Equipment.Clear();
            }

            row.Name = item.Name;
            row.MeasurementType = item.MeasurementType;
            row.DifficultyLevel = item.DifficultyLevel;
            row.ForceType = item.ForceType;
            row.Mechanics = item.Mechanics;
            row.Category = item.Category;
            row.InstructionsJson = JsonSerializer.Serialize(item.Instructions);
            row.IsActive = true;
            row.LastImportId = import.ExerciseCatalogImportId;
            row.UpdatedAtUtc = now;

            short muscleOrder = 0;
            foreach (var target in item.MuscleTargets)
            {
                row.MuscleTargets.Add(new ExerciseMuscleTargetRecord
                {
                    ExerciseMuscleTargetId = Guid.NewGuid(),
                    ExerciseCatalogItemId = row.ExerciseCatalogItemId,
                    ExternalId = target.ExternalId,
                    MuscleSlug = target.Slug,
                    MuscleName = target.Name,
                    RoleCode = target.Role,
                    DisplayOrder = muscleOrder++
                });
            }

            short equipmentOrder = 0;
            foreach (var equipment in item.Equipment)
            {
                row.Equipment.Add(new ExerciseEquipmentRequirementRecord
                {
                    ExerciseEquipmentRequirementId = Guid.NewGuid(),
                    ExerciseCatalogItemId = row.ExerciseCatalogItemId,
                    ExternalId = equipment.ExternalId,
                    EquipmentName = equipment.Name,
                    EquipmentType = equipment.Type,
                    UsageType = equipment.UsageType,
                    NumItems = equipment.NumItems,
                    DisplayOrder = equipmentOrder++
                });
            }
        }

        var deactivated = 0;
        foreach (var row in existing)
        {
            if (seen.Contains(row.SourceExternalId) || !row.IsActive)
                continue;

            row.IsActive = false;
            row.UpdatedAtUtc = now;
            deactivated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ExerciseCatalogImportResult(
            import.ExerciseCatalogImportId,
            provenance.SourceCode,
            added,
            updated,
            deactivated,
            snapshot.Exercises.Count);
    }

    public async Task<IReadOnlyList<ExerciseCatalogSearchItem>> SearchAsync(
        string? query,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var normalized = query?.Trim();

        var rows = db.ExerciseCatalogItems
            .AsNoTracking()
            .Include(x => x.MuscleTargets)
            .Include(x => x.Equipment)
            // B2.35.15 CSV UNKNOWN EXERCISE FALLBACK
            // Technical FK placeholders created by CSV import are intentionally
            // not part of the visible/shared exercise library.
            .Where(
                x =>
                    x.IsActive
                    && x.SourceCode
                        != "12WR_CSV_UNMAPPED");

        if (string.IsNullOrWhiteSpace(normalized))
        {
            var alphabetical =
                await rows
                    .OrderBy(x => x.Name)
                    .Take(take)
                    .ToListAsync(cancellationToken);

            return alphabetical
                .Select(ToSearchItem)
                .ToArray();
        }

        // The exercise catalog is intentionally small (currently under 1,000
        // active rows), so fuzzy ranking is done in memory after one bounded
        // read. This supports typos and searches across exercise name, muscle,
        // equipment, mechanics, category, and difficulty without changing the
        // Workout Intelligence module contract or database schema.
        var materialized =
            await rows
                .OrderBy(x => x.Name)
                .Take(2000)
                .ToListAsync(cancellationToken);

        return materialized
            .Select(x => new
            {
                Row = x,
                Score = FuzzySearch.Score(
                    normalized,
                    x.Name,
                    x.Mechanics,
                    x.Category,
                    x.DifficultyLevel,
                    string.Join(
                        ' ',
                        x.MuscleTargets.Select(m =>
                            $"{m.MuscleName} {m.MuscleSlug}")),
                    string.Join(
                        ' ',
                        x.Equipment.Select(e =>
                            $"{e.EquipmentName} {e.EquipmentType}")))
            })
            .Where(x => x.Score >= 48)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Row.Name)
            .Take(take)
            .Select(x => ToSearchItem(x.Row))
            .ToArray();
    }

    private static ExerciseCatalogSearchItem ToSearchItem(
        ExerciseCatalogItemRecord x)
        => new(
            x.ExerciseCatalogItemId,
            x.Name,
            x.Mechanics,
            x.Category,
            x.DifficultyLevel,
            x.MuscleTargets
                .Where(m => m.RoleCode == "primary")
                .OrderBy(m => m.DisplayOrder)
                .Select(m => m.MuscleName)
                .ToArray(),
            x.Equipment
                .OrderBy(e => e.DisplayOrder)
                .Select(e => e.EquipmentName)
                .ToArray());

    public async Task<ExerciseCatalogItem?> GetAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        var row = await db.ExerciseCatalogItems
            .AsNoTracking()
            .Include(x => x.MuscleTargets)
            .Include(x => x.Equipment)
            .SingleOrDefaultAsync(x => x.ExerciseCatalogItemId == exerciseId && x.IsActive, cancellationToken);

        if (row is null) return null;

        IReadOnlyList<string> instructions = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(row.InstructionsJson))
            instructions = JsonSerializer.Deserialize<string[]>(row.InstructionsJson) ?? Array.Empty<string>();

        return new ExerciseCatalogItem(
            row.ExerciseCatalogItemId,
            row.SourceCode,
            row.SourceExternalId,
            row.Name,
            row.MeasurementType,
            row.DifficultyLevel,
            row.ForceType,
            row.Mechanics,
            row.Category,
            instructions,
            row.MuscleTargets.OrderBy(x => x.DisplayOrder)
                .Select(x => new ExerciseMuscleTarget(x.ExternalId, x.MuscleSlug, x.MuscleName, x.RoleCode)).ToArray(),
            row.Equipment.OrderBy(x => x.DisplayOrder)
                .Select(x => new ExerciseEquipmentRequirement(x.ExternalId, x.EquipmentName, x.EquipmentType, x.UsageType, x.NumItems)).ToArray());
    }
}
