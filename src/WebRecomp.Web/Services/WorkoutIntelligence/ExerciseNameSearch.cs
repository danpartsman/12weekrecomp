using WebRecomp.Web.Services.Search;

namespace WebRecomp.Web.Services.WorkoutIntelligence;

/// <summary>
/// Ranks exercise names ahead of loose equipment/muscle metadata.
/// Handles word order ("lateral raise dumbbell" vs "Dumbbell Lateral Raise")
/// and common equipment plurals without introducing a provider dependency.
/// </summary>
public static class ExerciseNameSearch
{
    public static int Score(string? query, string? name)
    {
        var q = Tokens(query);
        var n = Tokens(name);
        if (q.Length == 0 || n.Length == 0) return 0;
        var queryPhrase = string.Join(" ", q);
        var namePhrase = string.Join(" ", n);
        if (queryPhrase == namePhrase) return 1000;
        if (namePhrase.StartsWith(queryPhrase + " ", StringComparison.Ordinal)) return 950;
        if (namePhrase.Contains(queryPhrase, StringComparison.Ordinal)) return 920;
        // All query tokens in the exercise name, irrespective of their order.
        // A complete word is stronger than a prefix, which is stronger than fuzz.
        var exact = q.Count(t => n.Contains(t, StringComparer.Ordinal));
        var prefix = q.Count(t => n.Any(v => v.StartsWith(t, StringComparison.Ordinal) && t.Length >= 3));
        if (exact == q.Length) return 900 - Math.Min(50, Math.Max(0, n.Length - q.Length) * 5);
        if (prefix == q.Length) return 820 - Math.Min(50, Math.Max(0, n.Length - q.Length) * 5);
        if (q.Length == 1 && n.Any(t => t.Contains(q[0], StringComparison.Ordinal) && q[0].Length >= 4))
            return 650;
        // Some words missing: retain partial name matches below full matches.
        if (q.Length > 1 && exact > 0) return 200 + (exact * 250 / q.Length);
        return 0;
    }

    private static string[] Tokens(string? value)
        => FuzzySearch.Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t switch
            {
                "db" or "dbs" or "dumbbells" => "dumbbell",
                "cables" => "cable",
                "raises" => "raise",
                "presses" => "press",
                "squats" => "squat",
                "rows" => "row",
                _ => t
            })
            .ToArray();
}
