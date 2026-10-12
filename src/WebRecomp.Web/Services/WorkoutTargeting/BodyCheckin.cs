using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.Services.WorkoutTargeting;

public sealed record BodyRegion(string Code, string Name, string View, params string[] MuscleTerms);

public static class BodyRegions
{
    public static readonly BodyRegion[] All =
    [
        new("chest", "Chest", "front", "chest", "pectoral"),
        new("shoulders", "Shoulders", "both", "shoulder", "deltoid"),
        new("biceps", "Biceps", "front", "bicep"),
        new("triceps", "Triceps", "back", "tricep"),
        new("forearms", "Forearms", "front", "forearm"),
        new("core", "Abdominals / core", "front", "abdomin", "oblique", "core"),
        new("quads", "Quads", "front", "quad"),
        new("calves", "Calves", "back", "calf", "calves", "gastrocnemius", "soleus"),
        new("upperback", "Upper back / traps", "back", "trapezius", "traps", "rhomboid", "upper back", "middle back"),
        new("lats", "Lats", "back", "latissimus", "lats"),
        new("lowerback", "Lower back", "back", "lower back", "erector"),
        new("glutes", "Glutes", "back", "glute"),
        new("hamstrings", "Hamstrings", "back", "hamstring"),
        new("neck", "Neck", "both", "neck"),
        new("hips", "Hips", "both", "hip", "adductor", "abductor"),
        new("knees", "Knees", "front"),
        new("ankles", "Ankles / feet", "front"),
        new("elbows", "Elbows", "both"),
        new("wrists", "Wrists / hands", "front")
    ];
    public static IReadOnlyList<string> FromMuscles(IEnumerable<string> muscles)
        => All.Where(r => r.MuscleTerms.Any(term => muscles.Any(m => m.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .Select(r => r.Code).Distinct().ToList();

    // Joint loading is intentionally broader than primary muscle tags.
    public static HashSet<string> LoadingRegions(IEnumerable<string> muscles)
    {
        var names = muscles.ToList();
        var regions = FromMuscles(names).ToHashSet();
        if (names.Any(m => m.Trim().Equals("back", StringComparison.OrdinalIgnoreCase))) regions.UnionWith(["upperback", "lats", "lowerback"]);
        if (regions.Overlaps(["chest", "lats", "upperback", "shoulders", "biceps", "triceps", "forearms"]))
            regions.UnionWith(["shoulders", "elbows", "wrists"]);
        if (regions.Overlaps(["quads", "hamstrings", "glutes", "calves", "hips"]))
            regions.UnionWith(["hips", "knees", "ankles"]);
        return regions;
    }
}

public sealed class RegionFeeling
{
    [Required] public string Region { get; set; } = "";
    [RegularExpression("^(both|left|right)$")] public string Side { get; set; } = "both";
    [Range(0, 10)] public int Soreness { get; set; }
    [Range(0, 10)] public int Pain { get; set; }
    public bool MovementLimited { get; set; }
    public bool SharpOrWorsening { get; set; }
}

/// <summary>Versioned, compact envelope in the existing 2,000-character check-in notes column.
/// Legacy prose remains readable. No new database privileges or schema are required.</summary>
public static class BodyCheckinCodec
{
    private const string Prefix = "12WR_BODY_V1:";
    private sealed record Envelope(string? Note, bool Checked, string[][] Regions, bool UnlocatedPain = false);
    public static (string? Note, bool Checked, List<RegionFeeling> Regions, bool UnlocatedPain) Read(string? value)
    {
        if (value?.StartsWith(Prefix, StringComparison.Ordinal) != true) return (value, false, [], false);
        try
        {
            var e = JsonSerializer.Deserialize<Envelope>(value[Prefix.Length..]);
            if (e is null) return (value, false, [], false);
            var regions = e.Regions.Select(r => new RegionFeeling
            {
                Region = r[0], Side = r[1], Soreness = int.Parse(r[2]), Pain = int.Parse(r[3]),
                MovementLimited = r[4] == "1", SharpOrWorsening = r[5] == "1"
            }).ToList();
            return (e.Note, e.Checked, regions, e.UnlocatedPain);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IndexOutOfRangeException or OverflowException)
        { return (value, false, [], false); }
    }
    public static string Write(string? note, bool bodyChecked, IEnumerable<RegionFeeling> regions, bool unlocatedPain = false)
    {
        var rows = regions.Where(r => r.Soreness > 0 || r.Pain > 0 || r.MovementLimited || r.SharpOrWorsening)
            .Select(r => new[] { r.Region, r.Side, r.Soreness.ToString(), r.Pain.ToString(), r.MovementLimited ? "1" : "0", r.SharpOrWorsening ? "1" : "0" }).ToArray();
        var result = Prefix + JsonSerializer.Serialize(new Envelope(note, bodyChecked, rows, unlocatedPain));
        if (result.Length > 2000) throw new ArgumentException("Shorten your recovery notes to save this body check.");
        return result;
    }
    public static string UpdateNote(string? stored, string? note)
    {
        var previous = Read(stored);
        return previous.Checked || previous.Regions.Count > 0 ? Write(note, previous.Checked, previous.Regions, previous.UnlocatedPain) : note ?? "";
    }
}

public static class WorkoutPlanPolicy
{
    public static bool CanEdit(DateOnly date, DateOnly today, bool hasCompletedWorkout)
        => date >= today && !hasCompletedWorkout;
}
