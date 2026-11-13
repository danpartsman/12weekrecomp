using WebRecomp.Web.Services.Nutrition;
namespace WebRecomp.Web.Tests;
public sealed class FoodSearchRankingTests
{
    [Theory]
    [InlineData("nutricost whey protein vanilla","Whey Protein Vanilla","Nutricost","Whey Protein Vanilla","Other Brand")]
    [InlineData("nutricost whey protein vanilla","Whey Protein Vanilla","Nutricost","Whey Protein Chocolate","Nutricost")]
    [InlineData("chicken breast","Chicken breast",null,"Chicken breast sandwich with cheese",null)]
    [InlineData("chicken breast sandwich","Chicken breast sandwich",null,"Chicken breast",null)]
    [InlineData("93 lean ground beef","Ground beef 93% lean",null,"Ground beef 97% lean",null)]
    [InlineData("raw chicken breast","Chicken breast raw",null,"Chicken breast cooked",null)]
    [InlineData("unsweetened almond milk","Almond milk unsweetened",null,"Almond milk sweetened",null)]
    public void Requested_product_details_rank_above_alternatives(string query,string good,string? goodBrand,string bad,string? badBrand)
        =>Assert.True(FoodSearchRanking.Score(query,good,goodBrand)>FoodSearchRanking.Score(query,bad,badBrand));
    [Fact]
    public void Word_order_and_punctuation_do_not_lose_complete_product_matches()
        =>Assert.True(FoodSearchRanking.Score("nutricost vanilla whey protein","Whey Protein, Vanilla","Nutricost")>=1000);
    [Fact]
    public void Typo_match_remains_a_candidate()
        =>Assert.True(FoodSearchRanking.Score("chiken breast","Chicken breast")>=500);
    [Fact]
    public void Unrelated_food_cannot_beat_product_match()
        =>Assert.True(FoodSearchRanking.Score("nutricost whey protein vanilla","Whey Protein Vanilla","Nutricost")>FoodSearchRanking.Score("nutricost whey protein vanilla","Vanilla ice cream"));

    private sealed record Food(string Name,string? Brand,int Uses);
    private static Food[] Personal(string query,params Food[] foods)
        =>FoodSearchRanking.OrderByPersonalUse(query,foods,x=>x.Name,x=>x.Brand,x=>x.Uses).ToArray();

    [Fact]
    public void MostUsedMatchingFoodComesBeforeNewOrLessUsedMatches()
    {
        var regular=new Food("Chicken breast boneless skinless",null,40);
        var occasional=new Food("Chicken breast",null,2);
        var unused=new Food("Chicken breast",null,0);
        Assert.True(FoodSearchRanking.Score("chicken breast",unused.Name)>FoodSearchRanking.Score("chicken breast",regular.Name));
        Assert.Equal(new[]{regular,occasional,unused},Personal("chicken breast",unused,occasional,regular));
    }

    [Theory]
    [InlineData("nutricost vanilla whey protein","Whey Protein Vanilla","Nutricost","Whey Protein Vanilla","Other Brand")]
    [InlineData("vanilla whey protein","Whey Protein Vanilla","Nutricost","Whey Protein Chocolate","Nutricost")]
    [InlineData("93 lean ground beef","Ground beef 93% lean",null,"Ground beef 97% lean",null)]
    [InlineData("raw chicken breast","Chicken breast raw",null,"Chicken breast cooked",null)]
    public void ExplicitQueryWordsStayAheadOfMoreFrequentAlternatives(string query,string name,string? brand,string wrongName,string? wrongBrand)
    {
        var requested=new Food(name,brand,0);var frequentAlternative=new Food(wrongName,wrongBrand,100);
        Assert.Same(requested,Personal(query,frequentAlternative,requested)[0]);
    }

    [Fact]
    public void EqualUsageKeepsTheExistingWordBasedRanking()
    {
        var exact=new Food("Chicken breast",null,5);var longer=new Food("Chicken breast sandwich with cheese",null,5);
        Assert.Same(exact,Personal("chicken breast",longer,exact)[0]);
        Assert.Same(exact,Personal("breast chicken",longer,exact)[0]);
    }

    [Fact]
    public void TypoMatchesCanStillBeOrderedByPersonalUsage()
    {
        var regular=new Food("Chicken breast raw",null,30);var occasional=new Food("Chicken breast",null,1);
        Assert.True(FoodSearchRanking.IsRelevant("chiken breast",regular.Name));
        Assert.Same(regular,Personal("chiken breast",occasional,regular)[0]);
    }

    [Fact]
    public void TheOrderingUsesTheCurrentClientsCountsRatherThanGlobalPopularity()
    {
        var a=new Food("Almond milk unsweetened","Brand A",20);var b=new Food("Almond milk unsweetened","Brand B",2);
        Assert.Equal("Brand A",Personal("almond milk unsweetened",a,b)[0].Brand);
        Assert.Equal("Brand B",Personal("almond milk unsweetened",a with {Uses=0},b with {Uses=15})[0].Brand);
    }
}
