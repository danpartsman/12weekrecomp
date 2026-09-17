using System.Text.Json;

namespace WebRecomp.WorkoutIntelligence.Catalog;

/// <summary>
/// Adapter for the open Kinetic exercise dataset.
/// The rest of 12WR consumes ExerciseCatalogSnapshot, not Kinetic's JSON shape.
/// </summary>
public sealed class KineticExerciseCatalogReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ExerciseCatalogSnapshot Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var rows = JsonSerializer.Deserialize<List<KineticExerciseRow>>(json, JsonOptions)
            ?? throw new InvalidDataException("Kinetic exercise JSON produced no rows.");

        var items = new List<ExerciseCatalogItem>(rows.Count);

        foreach (var row in rows)
        {
            if (!Guid.TryParse(row.Id, out var externalGuid))
            {
                throw new InvalidDataException($"Kinetic exercise has invalid UUID: {row.Id}");
            }

            if (string.IsNullOrWhiteSpace(row.Name))
            {
                throw new InvalidDataException($"Kinetic exercise {row.Id} has no name.");
            }

            items.Add(new ExerciseCatalogItem(
                Id: externalGuid,
                SourceCode: ExerciseCatalogSources.Kinetic,
                SourceExternalId: row.Id!,
                Name: row.Name.Trim(),
                MeasurementType: Normalize(row.Type, "reps"),
                DifficultyLevel: NullIfBlank(row.DifficultyLevel),
                ForceType: NullIfBlank(row.ForceType),
                Mechanics: NullIfBlank(row.Mechanics),
                Category: NullIfBlank(row.Category),
                Instructions: (row.Instructions ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray(),
                MuscleTargets: (row.MuscleGroups ?? new()).Select(MapMuscle).ToArray(),
                Equipment: (row.Equipment ?? new()).Select(MapEquipment).ToArray()));
        }

        return new ExerciseCatalogSnapshot(
            SourceCode: ExerciseCatalogSources.Kinetic,
            LicenseCode: "MIT",
            Exercises: items);
    }

    public ExerciseCatalogSnapshot ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllText(path));
    }

    private static ExerciseMuscleTarget MapMuscle(KineticMuscleRow row)
    {
        return new ExerciseMuscleTarget(
            ExternalId: Normalize(row.Id, "unknown"),
            Slug: Normalize(row.Slug, "unknown"),
            Name: Normalize(row.Name, "Unknown"),
            Role: Normalize(row.Type, "tertiary"));
    }

    private static ExerciseEquipmentRequirement MapEquipment(KineticEquipmentRow row)
    {
        return new ExerciseEquipmentRequirement(
            ExternalId: Normalize(row.Id, "unknown"),
            Name: Normalize(row.Name, "Unknown"),
            Type: Normalize(row.Type, "unknown"),
            UsageType: NullIfBlank(row.UsageType),
            NumItems: row.NumItems);
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class KineticExerciseRow
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Type { get; init; }
        public string? DifficultyLevel { get; init; }
        public string? ForceType { get; init; }
        public string? Mechanics { get; init; }
        public string? Category { get; init; }
        public List<string>? Instructions { get; init; }
        public List<KineticMuscleRow>? MuscleGroups { get; init; }
        public List<KineticEquipmentRow>? Equipment { get; init; }
    }

    private sealed class KineticMuscleRow
    {
        public string? Id { get; init; }
        public string? Slug { get; init; }
        public string? Name { get; init; }
        public string? Type { get; init; }
    }

    private sealed class KineticEquipmentRow
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Type { get; init; }
        public string? UsageType { get; init; }
        public int? NumItems { get; init; }
    }
}
