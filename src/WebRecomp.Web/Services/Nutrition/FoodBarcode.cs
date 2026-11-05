namespace WebRecomp.Web.Services.Nutrition;
public static class FoodBarcode
{
    public static string? Normalize(string? value)
    {
        var digits=value?.Trim();
        if(digits is null||!(digits.Length is 8 or 12 or 13 or 14)||digits.Any(c=>c<'0'||c>'9')||digits.All(c=>c=='0'))return null;
        var sum=0;var weight=3;
        for(var i=digits.Length-2;i>=0;i--){sum+=(digits[i]-'0')*weight;weight=weight==3?1:3;}
        return (10-sum%10)%10==digits[^1]-'0'?digits.PadLeft(14,'0'):null;
    }
}
