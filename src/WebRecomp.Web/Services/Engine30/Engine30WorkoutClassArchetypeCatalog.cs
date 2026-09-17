namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// 12WR v1009 workout class archetype layer.
///
/// Purpose:
/// - present common boutique/group-class language to clients/coaches;
/// - keep brand examples separate from physiological Engine logic;
/// - resolve each broad class archetype to an already-tested Engine 3.0
///   workout profile as a DEFAULT;
/// - allow future dedicated profiles or richer segment input without changing
///   the user-facing taxonomy.
///
/// IMPORTANT:
/// A brand example is not a physiological guarantee. A specific class can be
/// overridden to another Engine workout type when its actual structure differs.
/// </summary>
public sealed record Engine30WorkoutClassArchetypeDefinition(
    string ArchetypeCode,
    string DisplayName,
    string DefaultWorkoutTypeCode,
    IReadOnlyList<string> Examples,
    string Description,
    string MappingNote);

public static class Engine30WorkoutClassArchetypeCodes
{
    public const string StrengthClass = "STRENGTH_CLASS";
    public const string HybridConditioningClass = "HYBRID_CONDITIONING_CLASS";
    public const string IndoorCyclingClass = "INDOOR_CYCLING_CLASS";
    public const string BoxingKickboxingClass = "BOXING_KICKBOXING_CLASS";
    public const string PilatesBarreClass = "PILATES_BARRE_CLASS";
}

public sealed class Engine30WorkoutClassArchetypeCatalog
{
    private static readonly IReadOnlyList<Engine30WorkoutClassArchetypeDefinition>
        Definitions =
            new[]
            {
                new Engine30WorkoutClassArchetypeDefinition(
                    ArchetypeCode:
                        Engine30WorkoutClassArchetypeCodes.StrengthClass,
                    DisplayName:
                        "Strength Class",
                    DefaultWorkoutTypeCode:
                        "RESISTANCE_CIRCUIT",
                    Examples:
                        new[]
                        {
                            "Orangetheory Strength 50",
                            "F45 Resistance",
                            "Coach-led functional strength class"
                        },
                    Description:
                        "Primarily resistance training performed in a coached class format, usually using stations, timed blocks, or circuit organization.",
                    MappingNote:
                        "Defaults to RESISTANCE_CIRCUIT. Use a major/full-body strength profile instead when the actual session is conventional heavy set-based lifting."),

                new Engine30WorkoutClassArchetypeDefinition(
                    ArchetypeCode:
                        Engine30WorkoutClassArchetypeCodes.HybridConditioningClass,
                    DisplayName:
                        "Hybrid Conditioning Class",
                    DefaultWorkoutTypeCode:
                        "MIXED_GYM_SESSION",
                    Examples:
                        new[]
                        {
                            "Orangetheory",
                            "F45 Hybrid",
                            "Barry's-style bootcamp",
                            "Mixed strength + cardio bootcamp"
                        },
                    Description:
                        "Coach-led sessions that deliberately combine resistance and conditioning blocks in the same workout.",
                    MappingNote:
                        "Defaults to MIXED_GYM_SESSION so generic hybrid classes do not inherit Orangetheory-specific treadmill/rower assumptions. Use ORANGETHEORY when that known class structure is actually performed."),

                new Engine30WorkoutClassArchetypeDefinition(
                    ArchetypeCode:
                        Engine30WorkoutClassArchetypeCodes.IndoorCyclingClass,
                    DisplayName:
                        "Indoor Cycling Class",
                    DefaultWorkoutTypeCode:
                        "CYCLING_INTERVALS",
                    Examples:
                        new[]
                        {
                            "CycleBar",
                            "SoulCycle",
                            "Spin class",
                            "Studio cycling intervals"
                        },
                    Description:
                        "Indoor bicycle classes using coached changes in cadence, resistance, climbs, sprints, or recovery.",
                    MappingNote:
                        "Defaults to CYCLING_INTERVALS. Use steady CYCLING when the class is predominantly continuous endurance work."),

                new Engine30WorkoutClassArchetypeDefinition(
                    ArchetypeCode:
                        Engine30WorkoutClassArchetypeCodes.BoxingKickboxingClass,
                    DisplayName:
                        "Boxing / Kickboxing Class",
                    DefaultWorkoutTypeCode:
                        "MIXED_CONDITIONING",
                    Examples:
                        new[]
                        {
                            "TITLE-style boxing",
                            "Rumble-style boxing",
                            "Fitness kickboxing",
                            "Bag-work interval class"
                        },
                    Description:
                        "Interval-oriented striking classes combining bag work or shadowboxing with footwork, active recovery, and sometimes bodyweight or strength blocks.",
                    MappingNote:
                        "Defaults to MIXED_CONDITIONING until a dedicated boxing/kickboxing profile is scientifically calibrated. A class with substantial strength blocks can instead use HYBRID_CONDITIONING_CLASS."),

                new Engine30WorkoutClassArchetypeDefinition(
                    ArchetypeCode:
                        Engine30WorkoutClassArchetypeCodes.PilatesBarreClass,
                    DisplayName:
                        "Pilates / Barre Class",
                    DefaultWorkoutTypeCode:
                        "ACCESSORY",
                    Examples:
                        new[]
                        {
                            "Club Pilates",
                            "Pure Barre",
                            "Reformer Pilates",
                            "Barre strength/endurance class"
                        },
                    Description:
                        "Low-impact classes emphasizing muscular endurance, controlled resistance, trunk/hip work, posture, and repeated submaximal contractions.",
                    MappingNote:
                        "Defaults to ACCESSORY as the nearest current Engine profile. This is a provisional mapping and is the strongest candidate for a future dedicated profile if usage justifies it.")
            };

    private static readonly IReadOnlyDictionary<string, Engine30WorkoutClassArchetypeDefinition>
        ByCode =
            Definitions.ToDictionary(
                x => x.ArchetypeCode,
                StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Engine30WorkoutClassArchetypeDefinition> GetAll()
        => Definitions;

    public Engine30WorkoutClassArchetypeDefinition GetRequired(
        string archetypeCode)
    {
        if (string.IsNullOrWhiteSpace(archetypeCode))
        {
            throw new ArgumentException(
                "Workout class archetype code is required.",
                nameof(archetypeCode));
        }

        var normalized =
            archetypeCode
                .Trim()
                .ToUpperInvariant();

        if (!ByCode.TryGetValue(
                normalized,
                out var definition))
        {
            throw new KeyNotFoundException(
                $"Unknown Engine 3.0 workout class archetype '{archetypeCode}'.");
        }

        return definition;
    }

    public bool TryGet(
        string archetypeCode,
        out Engine30WorkoutClassArchetypeDefinition? definition)
    {
        definition = null;

        if (string.IsNullOrWhiteSpace(archetypeCode))
            return false;

        return ByCode.TryGetValue(
            archetypeCode.Trim(),
            out definition);
    }

    public Engine30WorkoutTypeDefinition ResolveDefaultWorkoutType(
        string archetypeCode)
    {
        var archetype =
            GetRequired(
                archetypeCode);

        var coreCatalog =
            new Engine30WorkoutTypeCatalog();

        return coreCatalog.GetRequired(
            archetype.DefaultWorkoutTypeCode);
    }
}