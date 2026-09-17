using System.Globalization;
using System.Text;

namespace WebRecomp.Web.Services.Search;

public static class FuzzySearch
{
    private const int DefaultMinimumScore = 48;

    public static bool IsMatch(
        string? query,
        IEnumerable<string?> values,
        int minimumScore = DefaultMinimumScore)
        => Score(query, values) >= minimumScore;

    public static int Score(
        string? query,
        params string?[] values)
        => Score(query, (IEnumerable<string?>)values);

    public static int Score(
        string? query,
        IEnumerable<string?> values)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
            return 100;

        var candidate = Normalize(
            string.Join(
                ' ',
                values.Where(x => !string.IsNullOrWhiteSpace(x))));

        if (candidate.Length == 0)
            return 0;

        if (candidate.Equals(normalizedQuery, StringComparison.Ordinal))
            return 100;

        var score = 0;

        if (candidate.StartsWith(normalizedQuery, StringComparison.Ordinal))
            score = Math.Max(score, 97);
        else if (candidate.Contains(normalizedQuery, StringComparison.Ordinal))
            score = Math.Max(score, 93);

        var queryTokens = Tokens(normalizedQuery);
        var candidateTokens = Tokens(candidate);

        if (queryTokens.Length == 0 || candidateTokens.Length == 0)
            return score;

        var tokenScores =
            queryTokens
                .Select(queryToken =>
                    candidateTokens
                        .Select(candidateToken =>
                            TokenScore(queryToken, candidateToken))
                        .DefaultIfEmpty(0)
                        .Max())
                .ToArray();

        var averageTokenScore =
            (int)Math.Round(tokenScores.Average());

        score = Math.Max(score, averageTokenScore);

        var strongMatches =
            tokenScores.Count(x => x >= 82);

        if (strongMatches == queryTokens.Length)
            score = Math.Max(score, 90);
        else if (
            queryTokens.Length > 1
            && strongMatches >= queryTokens.Length - 1)
        {
            score = Math.Max(score, 78);
        }

        var compactQuery = normalizedQuery.Replace(" ", "", StringComparison.Ordinal);
        var compactCandidate = candidate.Replace(" ", "", StringComparison.Ordinal);

        if (compactCandidate.Contains(compactQuery, StringComparison.Ordinal))
            score = Math.Max(score, 94);

        return Math.Clamp(score, 0, 100);
    }

    public static IReadOnlyList<string> BuildQueryVariants(
        string? query,
        int maxVariants = 6)
    {
        maxVariants = Math.Clamp(maxVariants, 1, 12);

        var normalized = Normalize(query);
        if (normalized.Length == 0)
            return Array.Empty<string>();

        var variants = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            var clean = Normalize(value);
            if (clean.Length < 2 || !seen.Add(clean))
                return;

            variants.Add(clean);
        }

        Add(normalized);

        var tokens = Tokens(normalized);

        if (tokens.Length > 1)
        {
            for (var index = 0; index < tokens.Length - 1; index++)
                Add(tokens[index] + " " + tokens[index + 1]);

            foreach (var token in
                     tokens
                         .OrderByDescending(x => x.Length)
                         .ThenBy(x => x, StringComparer.Ordinal))
            {
                Add(token);
            }
        }

        return variants
            .Take(maxVariants)
            .ToArray();
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed =
            value
                .Trim()
                .ToLowerInvariant()
                .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder
            .ToString()
            .Trim();
    }

    private static string[] Tokens(string normalized)
        => normalized
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);

    private static int TokenScore(
        string queryToken,
        string candidateToken)
    {
        if (queryToken.Equals(candidateToken, StringComparison.Ordinal))
            return 100;

        if (candidateToken.StartsWith(queryToken, StringComparison.Ordinal)
            || queryToken.StartsWith(candidateToken, StringComparison.Ordinal))
        {
            var shorter = Math.Min(queryToken.Length, candidateToken.Length);
            var longer = Math.Max(queryToken.Length, candidateToken.Length);
            var ratio = longer == 0 ? 0d : (double)shorter / longer;

            return ratio >= 0.6d
                ? 92
                : 76;
        }

        if (candidateToken.Contains(queryToken, StringComparison.Ordinal)
            || queryToken.Contains(candidateToken, StringComparison.Ordinal))
        {
            return 84;
        }

        var distance =
            DamerauLevenshteinDistance(
                queryToken,
                candidateToken);

        var length =
            Math.Max(
                queryToken.Length,
                candidateToken.Length);

        if (length == 0)
            return 100;

        var similarity =
            1d - ((double)distance / length);

        return (int)Math.Round(
            Math.Clamp(similarity, 0d, 1d) * 100d);
    }

    private static int DamerauLevenshteinDistance(
        string left,
        string right)
    {
        if (left.Length == 0)
            return right.Length;

        if (right.Length == 0)
            return left.Length;

        var matrix =
            new int[left.Length + 1, right.Length + 1];

        for (var i = 0; i <= left.Length; i++)
            matrix[i, 0] = i;

        for (var j = 0; j <= right.Length; j++)
            matrix[0, j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost =
                    left[i - 1] == right[j - 1]
                        ? 0
                        : 1;

                var deletion =
                    matrix[i - 1, j] + 1;

                var insertion =
                    matrix[i, j - 1] + 1;

                var substitution =
                    matrix[i - 1, j - 1] + cost;

                var value =
                    Math.Min(
                        Math.Min(deletion, insertion),
                        substitution);

                if (
                    i > 1
                    && j > 1
                    && left[i - 1] == right[j - 2]
                    && left[i - 2] == right[j - 1])
                {
                    value =
                        Math.Min(
                            value,
                            matrix[i - 2, j - 2] + cost);
                }

                matrix[i, j] = value;
            }
        }

        return matrix[left.Length, right.Length];
    }
}
