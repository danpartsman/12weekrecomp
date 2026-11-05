using WebRecomp.Web.Services.Search;
namespace WebRecomp.Web.Services.Nutrition;

/// <summary>Food identity and requested attributes outrank source and personal usage.</summary>
public static class FoodSearchRanking
{
    public static string[] Words(string? text)=>FuzzySearch.Normalize(text).Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
    public static int Score(string query,string? name,string? brand=null)
    {
        var wanted=Words(query);if(wanted.Length==0)return 0;
        var candidate=Words(name+" "+brand);
        if(candidate.Length==0)return 0;
        var hits=wanted.Count(candidate.Contains);
        var fuzzy=FuzzySearch.Score(query,name,brand);
        // All requested words (including brand/flavor/percentage) form the top tier.
        var score=hits==wanted.Length?1000:300+300*hits/wanted.Length;
        score+=fuzzy;
        if(FuzzySearch.Normalize(name)==FuzzySearch.Normalize(query))score+=120;
        score-=Math.Min(100,Math.Max(0,candidate.Length-wanted.Length)*6);
        foreach(var pair in new[]{new[]{"raw","cooked"},new[]{"vanilla","chocolate"},new[]{"sweetened","unsweetened"},new[]{"salted","unsalted"}})
            if((wanted.Contains(pair[0])&&candidate.Contains(pair[1])&&!candidate.Contains(pair[0]))||(wanted.Contains(pair[1])&&candidate.Contains(pair[0])&&!candidate.Contains(pair[1])))score-=250;
        // A requested percentage/number must not be treated as a fuzzy substitute.
        foreach(var number in wanted.Where(w=>w.All(char.IsDigit)))if(!candidate.Contains(number))score-=250;
        return score;
    }
    public static bool IsRelevant(string query,string? name,string? brand=null)
    {
        var wanted=Words(query);var candidate=Words(name+" "+brand);
        return wanted.Length>0&&(wanted.All(candidate.Contains)||FuzzySearch.Score(query,name,brand)>=65);
    }
}
