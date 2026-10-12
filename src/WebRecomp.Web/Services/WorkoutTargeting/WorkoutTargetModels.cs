using System.ComponentModel.DataAnnotations;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.WorkoutTargeting;

public sealed class TodayWorkoutViewModel
{
    [RegularExpression("^(auto|targeted|fullbody|conditioning|cardio|mobility|recovery)$")]
    public string Activity { get; set; } = "auto";
    [RegularExpression("^(hard|moderate|easy)$")]
    public string Effort { get; set; } = "moderate";
    [Range(10, 120)] public int Minutes { get; set; } = 45;
    [RegularExpression("^(gym|home|bodyweight)$")]
    public string Location { get; set; } = "gym";
    public List<string> Targets { get; set; } = [];
    public List<string> Additions { get; set; } = [];
    public List<string> Equipment { get; set; } = [];
    public DateOnly Date { get; set; }
    public bool BodyChecked { get; set; }
    public bool CanStart { get; set; }
    public string? FuelSummary { get; set; }
    public string? PlanName { get; set; }
    public List<string> Notices { get; set; } = [];
    public List<RegionReadiness> Readiness { get; set; } = [];
    public List<WorkoutTargetChoice> Choices { get; set; } = [];
}
public sealed record RegionReadiness(string Region, string Name, string Status, decimal RecentHardSets, int Soreness, int Pain);
public sealed record TargetExercise(Guid ExerciseId, string Name, string? Mechanics, int Sets, int Reps, decimal Rpe,
    IReadOnlyList<string> Regions, string? Note = null, bool IsAddon = false, int? DurationSeconds = null);
public sealed class WorkoutTargetChoice
{
    public string Key { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string TypeCode { get; set; } = "";
    public string? SystemCode { get; set; }
    public Guid? TemplateId { get; set; }
    public int Minutes { get; set; }
    public decimal Rpe { get; set; }
    public decimal Score { get; set; }
    public bool Adapted { get; set; }
    public List<string> Reasons { get; set; } = [];
    public List<TargetExercise> Exercises { get; set; } = [];
}

public static class WorkoutTargetPolicy
{
    // Explicit recommendation heuristics, not validated recovery-time or diagnostic thresholds.
    public static bool Excluded(IEnumerable<string> loadingRegions, IEnumerable<RegionFeeling> feelings)
        => feelings.Any(f => loadingRegions.Contains(f.Region)
            && (f.Pain > 0 || f.SharpOrWorsening || f.MovementLimited || f.Soreness >= 8));
    public static decimal EffortCap(string requested, bool lowFuel, DailyCheckin? checkin)
    {
        var cap = requested == "hard" ? 8m : requested == "easy" ? 4m : 6m;
        if (lowFuel || checkin?.SleepHours is < 5.5m || checkin?.EnergyScore is <= 3 || checkin?.SorenessScore is >= 8)
            cap = Math.Min(cap, 6m);
        return cap;
    }
    public static string ActivityFor(string type)
        => type == "REST" ? "recovery" : type == "MOBILITY_RECOVERY" ? "mobility"
            : type.Contains("HIIT") || type.Contains("CIRCUIT") || type.Contains("ORANGETHEORY") ? "conditioning"
            : type.Contains("CARDIO") || type.Contains("WALK") || type.Contains("RUN") || type.Contains("CYCL") || type.Contains("SWIM") ? "cardio"
            : "strength";
}
