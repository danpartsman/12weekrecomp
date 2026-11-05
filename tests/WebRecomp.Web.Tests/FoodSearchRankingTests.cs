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
}
