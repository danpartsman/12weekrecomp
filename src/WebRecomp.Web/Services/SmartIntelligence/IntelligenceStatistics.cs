using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace WebRecomp.Web.Services.SmartIntelligence;

public static class IntelligenceStatistics
{
    public const string Version = "SI-1.0-local-linear-huber";
    public static string NormalizeSensitivity(string? value) => value is "steady" or "responsive" ? value : "balanced";
    public static IntelligenceReport Build(IReadOnlyList<IntelligenceDay> history, DateOnly start, DateOnly end,
        string sensitivity, string muscleMetric)
    {
        sensitivity = NormalizeSensitivity(sensitivity);
        var ordered = history.OrderBy(x => x.Date).ToArray();
        var states = new[] {
            RobustStateEstimator.Fit("fat", "Estimated fat mass", ordered.Select(x => (x.Date,x.Fat)).ToArray(), .8, sensitivity),
            RobustStateEstimator.Fit("muscle", muscleMetric, ordered.Select(x => (x.Date,x.Muscle)).ToArray(), .5, sensitivity),
            RobustStateEstimator.Fit("weight", "Body weight", ordered.Select(x => (x.Date,x.Weight)).ToArray(), .8, sensitivity),
            RobustStateEstimator.Fit("water", "Estimated body water", ordered.Select(x => (x.Date,x.Water)).ToArray(), .6, sensitivity)
        }.Select(s => s with { Points = s.Points.Where(x => x.Date >= start && x.Date <= end).ToArray() }).ToArray();
        var days = ordered.Where(x => x.Date >= start && x.Date <= end).ToArray();
        var correlations = Correlations(days);
        var findings = new List<IntelligenceFinding>();
        foreach (var series in states.Take(2))
        {
            var first = series.Points.FirstOrDefault(x => x.Estimate.HasValue && x.Readings >= 5);
            var last = series.Points.LastOrDefault(x => x.Estimate.HasValue);
            if (first is null || last is null) { findings.Add(new(series.Label, "Fewer than five comparable readings.", "A direction cannot yet be assessed.")); continue; }
            var change = last.Estimate!.Value - first.Estimate!.Value;
            var band = Math.Sqrt(Math.Pow((last.Upper!.Value-last.Lower!.Value)/2,2)+Math.Pow((first.Upper!.Value-first.Lower!.Value)/2,2));
            var direction = last.DaysSinceReading > 7 ? "Recent readings are too sparse to judge direction."
                : Math.Abs(change) <= band ? "Direction is uncertain within the model's uncertainty."
                : change > 0 ? "The modeled direction is upward; this does not establish exact tissue gain." : "The modeled direction is downward; this does not establish exact tissue loss.";
            findings.Add(new(series.Label, $"{first.Date:MMM d}–{last.Date:MMM d}: modeled change {change:+0.00;-0.00;0.00} lb; {last.Readings} readings including initialization history.", direction));
        }
        var completed = days.Where(x => x.CompletedDay).ToArray();
        var food = completed.Where(x => x.Calories.HasValue && !x.PossiblePartialFoodDay).ToArray();
        findings.Add(new("Ordinary gaps are tolerated", $"{completed.Count(x=>!x.Calories.HasValue)} completed days lack calorie totals; {completed.Count(x=>x.PossiblePartialFoodDay)} low-total days are flagged as possibly partial.", "Missing values are unknown, never zero. Low totals are a completeness heuristic, not proof of under-eating or underlogging. No backfilling is required."));
        var comparable = food.Where(x => x.TargetCalories.HasValue).ToArray();
        if (comparable.Length > 0) findings.Add(new("Logged intake versus saved prescription", $"{comparable.Length} comparable days: {comparable.Average(x=>x.Calories!.Value):0} logged versus {comparable.Average(x=>x.TargetCalories!.Value):0} prescribed calories/day.", "Prescription difference is not energy balance versus maintenance. Unknown food, portion error and expenditure error remain unresolved; do not automatically blame the log."));
        var duplicates = days.Where(x => x.Workouts.GroupBy(w => new {w.Name,w.Type,w.Minutes,w.Rpe}).Any(g=>g.Count()>1)).ToArray();
        if (duplicates.Length > 0) findings.Add(new("Possible repeated workout records", string.Join(", ", duplicates.Select(x=>$"{x.Date:MMM d}")), "Identical name/type/duration/effort records may be duplicates or separate real sessions. All remain visible; none are silently deleted or treated as verified additional training."));
        var water = correlations.FirstOrDefault(x=>x.Input=="Water" && x.Outcome=="Muscle" && x.Transform=="Same-day levels");
        if (water?.R is double r && Math.Abs(r)>.85) findings.Add(new("Water and muscle share information", $"Same-day correlation r={r:0.00} across {water.Pairs} paired days.", "This may reflect shared scale equations. Water is not independent verification of muscle and is not subtracted from it."));
        var weeks = new List<IntelligenceFinding>();
        for(var d=start;d<=end;d=d.AddDays(7))
        {
            var finish=d.AddDays(6)>end?end:d.AddDays(6);
            var w=completed.Where(x=>x.Date>=d&&x.Date<=finish).ToArray();
            var f=w.Where(x=>x.Calories.HasValue&&!x.PossiblePartialFoodDay).ToArray();
            var c=f.Where(x=>x.TargetCalories.HasValue).ToArray();
            var text=f.Length==0?"No usable food totals.":$"{f.Length}/{w.Length} completed days with usable food totals; average {f.Average(x=>x.Calories!.Value):0} kcal, protein {Mean(f.Select(x=>x.Protein)):0} g, carbs {Mean(f.Select(x=>x.Carbs)):0} g, fat {Mean(f.Select(x=>x.DietaryFat)):0} g.";
            if(c.Length>0)text+=$" Matched-day difference from saved targets: {c.Average(x=>x.Calories!.Value-x.TargetCalories!.Value):+0;-0;0} kcal/day.";
            weeks.Add(new($"{d:MMM d}–{finish:MMM d}",text,$"Recorded completed training: {w.Sum(x=>x.TrainingMinutes??0):0} minutes on {w.Count(x=>x.TrainingMinutes>0)} days. Missing sessions are unknown, not assumed rest."));
        }
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Version,start,end,sensitivity,muscleMetric,days,states}))));
        return new(Version,fingerprint,start,end,sensitivity,muscleMetric,days,states,correlations,findings,weeks);
    }
    public static double? Mean(IEnumerable<double?> x) { var a=x.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();return a.Length==0?null:a.Average(); }
    public static double? Pearson(IReadOnlyList<(double X,double Y)> pairs)
    {
        if(pairs.Count<8)return null;var mx=pairs.Average(x=>x.X);var my=pairs.Average(x=>x.Y);
        var xx=pairs.Sum(x=>Math.Pow(x.X-mx,2));var yy=pairs.Sum(x=>Math.Pow(x.Y-my,2));
        return xx<1e-12||yy<1e-12?null:Math.Clamp(pairs.Sum(x=>(x.X-mx)*(x.Y-my))/Math.Sqrt(xx*yy),-1,1);
    }
    public static IReadOnlyList<CorrelationResult> Correlations(IReadOnlyList<IntelligenceDay> days)
    {
        var by=days.ToDictionary(x=>x.Date);var results=new List<CorrelationResult>();
        void Add(string input,string output,int lag,string transform,Func<IntelligenceDay,double?> a,Func<IntelligenceDay,double?> b)
        {
            var pairs=new List<(double,double)>();
            foreach(var day in days) { if(!by.TryGetValue(day.Date.AddDays(lag),out var next))continue;var x=a(day);var y=b(next);if(x.HasValue&&y.HasValue)pairs.Add((x.Value,y.Value)); }
            var r=Pearson(pairs);results.Add(new(input,output,lag,transform,pairs.Count,r,pairs.Count<8?"Too few paired days":r is null?"Not enough variation":pairs.Count<20?"Exploratory · small sample":"Exploratory association"));
        }
        double? Delta(IntelligenceDay x,Func<IntelligenceDay,double?> value)=>by.TryGetValue(x.Date.AddDays(-1),out var prior)?value(x)-value(prior):null;
        Add("Water","Muscle",0,"Same-day levels",x=>x.Water,x=>x.Muscle);
        Add("Water change","Muscle change",0,"Daily differences",x=>Delta(x,d=>d.Water),x=>Delta(x,d=>d.Muscle));
        Add("Water change","Fat change",0,"Daily differences",x=>Delta(x,d=>d.Water),x=>Delta(x,d=>d.Fat));
        Add("Recorded flexible items","Calories minus prescription",0,"Same-day saved count",x=>x.CompletedDay&&!x.PossiblePartialFoodDay?x.FlexibleItems:null,x=>x.CompletedDay&&!x.PossiblePartialFoodDay?x.Calories-x.TargetCalories:null);
        foreach(var lag in new[]{1,2,3})
        {
            Add("Carbs","Weight change",lag,"Intake → subsequent daily difference",x=>x.CompletedDay&&!x.PossiblePartialFoodDay?x.Carbs:null,x=>Delta(x,d=>d.Weight));
            Add("Carbs","Water change",lag,"Intake → subsequent daily difference",x=>x.CompletedDay&&!x.PossiblePartialFoodDay?x.Carbs:null,x=>Delta(x,d=>d.Water));
            Add("Calories minus prescription","Weight change",lag,"Target deviation → subsequent daily difference",x=>x.CompletedDay&&!x.PossiblePartialFoodDay?x.Calories-x.TargetCalories:null,x=>Delta(x,d=>d.Weight));
            Add("Fat minus prescription","Energy score",lag,"Target deviation → later check-in",x=>x.CompletedDay&&!x.PossiblePartialFoodDay?x.DietaryFat-x.TargetFat:null,x=>x.Energy);
            Add("Training load","Weight change",lag,"Duration × RPE → subsequent daily difference",x=>x.CompletedDay?x.TrainingLoad:null,x=>Delta(x,d=>d.Weight));
            Add("Sleep","Energy score",lag,"Sleep → later check-in",x=>x.Sleep,x=>x.Energy);
        }
        return results;
    }
}
