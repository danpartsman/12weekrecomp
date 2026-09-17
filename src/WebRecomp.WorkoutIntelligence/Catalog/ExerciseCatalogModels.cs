namespace WebRecomp.WorkoutIntelligence.Catalog;

public static class ExerciseCatalogSources
{
    public const string Kinetic = "KINETIC";
    public const string Custom = "12WR_CUSTOM";
}

public sealed record ExerciseCatalogItem(
    Guid Id,
    string SourceCode,
    string SourceExternalId,
    string Name,
    string MeasurementType,
    string? DifficultyLevel,
    string? ForceType,
    string? Mechanics,
    string? Category,
    IReadOnlyList<string> Instructions,
    IReadOnlyList<ExerciseMuscleTarget> MuscleTargets,
    IReadOnlyList<ExerciseEquipmentRequirement> Equipment);

public sealed record ExerciseMuscleTarget(
    string ExternalId,
    string Slug,
    string Name,
    string Role);

public sealed record ExerciseEquipmentRequirement(
    string ExternalId,
    string Name,
    string Type,
    string? UsageType,
    int? NumItems);

public sealed record ExerciseCatalogSnapshot(
    string SourceCode,
    string LicenseCode,
    IReadOnlyList<ExerciseCatalogItem> Exercises);
