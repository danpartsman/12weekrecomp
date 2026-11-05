using WebRecomp.Web.Services.Nutrition;
namespace WebRecomp.Web.Tests;
public sealed class FoodBarcodeTests
{
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
