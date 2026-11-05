using WebRecomp.Web.Services.Nutrition;
namespace WebRecomp.Web.Tests;
public sealed class FoodBarcodeTests
{
    [Theory]
    [InlineData("15","g","1 tablespoon (15 g)",15,"g")]
    [InlineData("240","","1 cup (240 ml)",240,"ml")]
    [InlineData("","","1 scoop (33 g)",33,"g")]
    public void Listed_serving_mass_is_used_without_assuming_100g(string quantity,string unit,string description,int expected,string expectedUnit)
    {
        var serving=FoodBarcode.Serving(quantity,unit,description);Assert.Equal((decimal)expected,serving.Size);Assert.Equal(expectedUnit,serving.Unit);
    }
    [Fact]
    public void Unknown_serving_is_left_for_user_input()=>Assert.Null(FoodBarcode.Serving(null,null,"1 scoop").Size);
    [Theory]
    [InlineData("012345678905","00012345678905")]
    [InlineData("0012345678905","00012345678905")]
    [InlineData("00012345678905","00012345678905")]
    [InlineData("96385074","00000096385074")]
    public void Valid_gtins_have_one_canonical_identity(string code,string expected)=>Assert.Equal(expected,FoodBarcode.Normalize(code));
    [Theory]
    [InlineData("012345678906")][InlineData("123")][InlineData("000000000000")][InlineData("01234567abcd")][InlineData("https://example.com")]
    public void Invalid_or_non_product_codes_are_rejected(string code)=>Assert.Null(FoodBarcode.Normalize(code));
}
