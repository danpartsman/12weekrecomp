using WebRecomp.Web.Services.Search;

namespace WebRecomp.Web.Tests;

public sealed class FuzzySearchTests
{
    [Theory]
    [InlineData("chiken breast", "Chicken breast, rotisserie, skin not eaten")]
    [InlineData("rotesserie chicken", "Chicken breast rotisserie")]
    [InlineData("lat pulldwn", "Lat Pulldown")]
    [InlineData("rear delt", "Dumbbell Rear Deltoid Fly")]
    [InlineData("barbel curl", "Barbell Curl")]
    public void Typo_and_token_searches_match(
        string query,
        string candidate)
    {
        Assert.True(
            FuzzySearch.Score(query, candidate) >= 48,
            $"Expected '{query}' to fuzzy-match '{candidate}'.");
    }

    [Fact]
    public void Exact_phrase_ranks_above_unrelated_candidate()
    {
        var exact =
            FuzzySearch.Score(
                "chicken breast",
                "Chicken breast, rotisserie");

        var unrelated =
            FuzzySearch.Score(
                "chicken breast",
                "Chocolate protein shake");

        Assert.True(exact > unrelated);
    }

    [Fact]
    public void Query_variants_include_useful_subphrases()
    {
        var variants =
            FuzzySearch.BuildQueryVariants(
                "rotesserie chicken breast",
                maxVariants: 8);

        Assert.Contains("chicken breast", variants);
        Assert.Contains("chicken", variants);
        Assert.Contains("breast", variants);
    }
}
