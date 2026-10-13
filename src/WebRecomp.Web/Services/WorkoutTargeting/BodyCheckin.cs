using System.Text.Json;
using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.Services.WorkoutTargeting;

public sealed record BodyRegion(string Code, string Name, string View, params string[] MuscleTerms);

public static class BodyRegions
{
    public static readonly BodyRegion[] All =
    [
        new("shoulders", "Front shoulders", "front", "anterior deltoid", "front shoulder", "shoulder", "deltoid"),
        new("biceps", "Biceps", "front", "bicep"),
        new("chest", "Chest", "front", "chest", "pectoral"),
        new("core", "Abs", "front", "abdomin", "oblique", "core"),
        new("hips", "Hips", "front", "hip", "adductor", "abductor"),
        new("quads", "Quads", "front", "quad"),
        new("upperback", "Shoulders / upper back", "back", "shoulder", "deltoid", "trapezius", "traps", "rhomboid", "upper back", "middle back", "latissimus", "lats"),
        new("lowerback", "Lower back", "back", "lower back", "erector"),
        new("triceps", "Triceps", "back", "tricep"),
        new("glutes", "Glutes", "back", "glute"),
        new("hamstrings", "Hamstrings", "back", "hamstring", "biceps femoris"),
        new("calves", "Calves", "back", "calf", "calves", "gastrocnemius", "soleus")
    ];
    public static IReadOnlyList<string> FromMuscles(IEnumerable<string> muscles)
    {
        var names = muscles.ToList();
        return All.Where(r => r.MuscleTerms.Any(term => names.Any(m => m.Contains(term, StringComparison.OrdinalIgnoreCase)
                && (r.Code != "biceps" || !m.Contains("femoris", StringComparison.OrdinalIgnoreCase)))))
            .Select(r => r.Code).Distinct().ToList();
    }
    public static HashSet<string> LoadingRegions(IEnumerable<string> muscles)
    {
        var names = muscles.ToList();
        var regions = FromMuscles(names).ToHashSet();
        if (names.Any(m => m.Trim().Equals("back", StringComparison.OrdinalIgnoreCase))) regions.UnionWith(["upperback", "lowerback"]);
        return regions;
    }
}

public sealed class RegionFeeling
{
    [Range(0, 2)] public int Mark { get; set; }
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
    private sealed record Envelope(string? Note, bool Checked, string[][] Regions, bool UnlocatedPain = false, bool KeepPain = false, int SorenessRating = 3, int PainRating = 3);
    public static (string? Note, bool Checked, List<RegionFeeling> Regions, bool UnlocatedPain, bool KeepPain, int SorenessRating, int PainRating) Read(string? value)
    {
        if (value?.StartsWith(Prefix, StringComparison.Ordinal) != true) return (value, false, [], false, false, 3, 3);
        try
        {
            var e = JsonSerializer.Deserialize<Envelope>(value[Prefix.Length..]);
            if (e is null) return (value, false, [], false, false, 3, 3);
            var regions = e.Regions.Select(r => new RegionFeeling
            {
                Mark = r.Length > 6 ? int.Parse(r[6]) : int.Parse(r[3]) > 0 ? 2 : int.Parse(r[2]) > 0 ? 1 : 0,
                Region = r[0], Side = r[1], Soreness = int.Parse(r[2]), Pain = int.Parse(r[3]),
                MovementLimited = r[4] == "1", SharpOrWorsening = r[5] == "1"
            }).ToList();
            // Consolidate old map locations without silently clearing an old injury.
            foreach (var region in regions)
                region.Region = region.Region switch { "lats" or "neck" => "upperback", "knees" => "quads", "ankles" => "calves", _ => region.Region };
            var unlocated = e.UnlocatedPain || regions.Any(x => !BodyRegions.All.Any(r => r.Code == x.Region) && (x.Pain > 0 || x.MovementLimited || x.SharpOrWorsening));
            regions = regions.Where(x => BodyRegions.All.Any(r => r.Code == x.Region)).GroupBy(x => x.Region)
                .Select(g => new RegionFeeling { Region = g.Key, Side = g.First().Side, Mark = g.Max(x => x.Mark),
                    Soreness = g.Max(x => x.Soreness), Pain = g.Max(x => x.Pain),
                    MovementLimited = g.Any(x => x.MovementLimited), SharpOrWorsening = g.Any(x => x.SharpOrWorsening) }).ToList();
            return (e.Note, e.Checked, regions, unlocated, e.KeepPain, e.SorenessRating, e.PainRating);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IndexOutOfRangeException or OverflowException)
        { return (value, false, [], false, false, 3, 3); }
    }
    public static string Write(string? note, bool bodyChecked, IEnumerable<RegionFeeling> regions, bool unlocatedPain = false, bool keepPain = false, int sorenessRating = 3, int painRating = 3)
    {
        var rows = regions.Where(r => r.Mark > 0 || r.Soreness > 0 || r.Pain > 0 || r.MovementLimited || r.SharpOrWorsening)
            .Select(r => new[] { r.Region, r.Side, r.Soreness.ToString(), r.Pain.ToString(), r.MovementLimited ? "1" : "0", r.SharpOrWorsening ? "1" : "0", r.Mark.ToString() }).ToArray();
        var result = Prefix + JsonSerializer.Serialize(new Envelope(note, bodyChecked, rows, unlocatedPain, keepPain, sorenessRating, painRating));
        if (result.Length > 2000) throw new ArgumentException("Shorten your recovery notes to save this body check.");
        return result;
    }
    public static async Task<(bool KeepPain, int PainRating, List<RegionFeeling> Regions)> PersistentPainAsync(
        WebRecomp.Web.Data.RecompDbContext db, Guid programId, DateOnly date, CancellationToken ct)
    {
        var notes = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.DailyCheckins.Where(x => x.ProgramId == programId && x.CheckinDate < date)
                .OrderByDescending(x => x.CheckinDate).Select(x => x.Notes), ct);
        var latest = notes.FirstOrDefault(x => x?.StartsWith(Prefix, StringComparison.Ordinal) == true);
        var body = Read(latest);
        return (body.KeepPain, body.PainRating, body.KeepPain
            ? body.Regions.Where(x => x.Mark == 2 || x.Pain > 0 || x.MovementLimited || x.SharpOrWorsening)
                .Select(x => new RegionFeeling { Region = x.Region, Mark = 2, Pain = body.PainRating }).ToList() : []);
    }
    public static string UpdateNote(string? stored, string? note)
    {
        var previous = Read(stored);
        return previous.Checked || previous.Regions.Count > 0 ? Write(note, previous.Checked, previous.Regions, previous.UnlocatedPain, previous.KeepPain, previous.SorenessRating, previous.PainRating) : note ?? "";
    }
}

public static class WorkoutPlanPolicy
{
    public static bool CanEdit(DateOnly date, DateOnly today, bool hasCompletedWorkout)
        => date >= today && !hasCompletedWorkout;
}
