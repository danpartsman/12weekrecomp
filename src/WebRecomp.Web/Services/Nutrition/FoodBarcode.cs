namespace WebRecomp.Web.Services.Nutrition;
public static class FoodBarcode
{
    public static (decimal? Size,string Unit) Serving(string? quantity,string? unit,string? description)
    {
        unit=unit?.Trim().ToLowerInvariant();
        if(decimal.TryParse(quantity,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var value)&&value>0&&value<=1000&&(unit=="g"||unit=="ml"))return(value,unit);
        var match=System.Text.RegularExpressions.Regex.Match(description??"",@"(?<![\d.])(\d+(?:\.\d+)?)\s*(g|ml)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if(match.Success&&decimal.TryParse(match.Groups[1].Value,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out value)&&value>0&&value<=1000)return(value,match.Groups[2].Value.ToLowerInvariant());
        return(null,"");
    }
    public static string? Normalize(string? value)
    {
        var digits=value?.Trim();
        if(digits is null||!(digits.Length is 8 or 12 or 13 or 14)||digits.Any(c=>c<'0'||c>'9')||digits.All(c=>c=='0'))return null;
        var sum=0;var weight=3;
        for(var i=digits.Length-2;i>=0;i--){sum+=(digits[i]-'0')*weight;weight=weight==3?1:3;}
        return (10-sum%10)%10==digits[^1]-'0'?digits.PadLeft(14,'0'):null;
    }
}
