using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;
public sealed record CoachRuleEvaluation(IReadOnlyList<Engine30DashboardDayProjection> Days, IReadOnlyList<string> Errors)
{
    public decimal WeeklyCalories => Days.Sum(x => x.PrescribedEnergyKcal);
}
public static class CoachPrescriptionAllocator
{
    public static IReadOnlyList<string> Validate(CoachPrescriptionRules rules)
    {
        var errors = new List<string>();
        if(rules.Enabled && !Bounds(rules).Any(x=>x.Item2.HasValue || x.Item3.HasValue) && !HasTargets(rules)) errors.Add("Enter at least one limit, or turn off coach rules.");
        foreach (var (label, min, max) in Bounds(rules))
            if (min < 0 || max < 0 || (min.HasValue && max.HasValue && min > max)) errors.Add(label + ": minimum must be nonnegative and no higher than maximum.");
        if (rules.AdjustCarbsThenFat && (!rules.CarbsMin.HasValue || !rules.CarbsMax.HasValue || !rules.FatMin.HasValue || !rules.FatMax.HasValue))
            errors.Add("Enter both minimum and maximum carbs and fat before allowing calorie adjustments.");
        if (rules.DailyCaloriesMin.HasValue && rules.WeeklyCaloriesMax.HasValue && rules.DailyCaloriesMin * 7 > rules.WeeklyCaloriesMax)
            errors.Add("Seven daily calorie minimums exceed the weekly maximum.");
        if (rules.DailyCaloriesMax.HasValue && rules.WeeklyCaloriesMin.HasValue && rules.DailyCaloriesMax * 7 < rules.WeeklyCaloriesMin)
            errors.Add("Seven daily calorie maximums cannot reach the weekly minimum.");
        if (rules.DailyProteinTarget is <=0 or >1000 || rules.WeeklyCarbsTarget is <0 or >14000 || rules.WeeklyFatTarget is <0 or >3500)
            errors.Add("Macro goals are outside the supported range.");
        if (HasTargets(rules) && rules.AdjustCarbsThenFat)
            errors.Add("Turn off calorie-driven adjustment when assigning explicit macro goals. Resolve calorie conflicts by changing the macro goals or limits.");
        return errors;
    }
    private static bool HasTargets(CoachPrescriptionRules r) => r.DailyProteinTarget.HasValue || r.WeeklyCarbsTarget.HasValue || r.WeeklyFatTarget.HasValue;
    // Bounded proportional allocation retains the engine's training/recovery carb pattern.
    public static decimal[] AllocateWeekly(decimal total, IReadOnlyList<decimal> weights, decimal? min, decimal? max)
    {
        var lower=min ?? 0;var upper=max ?? decimal.MaxValue/100;
        if(weights.Count!=7 || total<lower*7 || total>upper*7) throw new ArgumentException("Weekly macro goal cannot fit the daily macro limits.");
        var values=Enumerable.Repeat(lower,7).ToArray();var pending=Enumerable.Range(0,7).ToList();var remaining=total-lower*7;
        while(pending.Count>0)
        {
            var sum=pending.Sum(i=>Math.Max(0,weights[i]));
            var proposed=pending.ToDictionary(i=>i,i=>values[i]+(sum>0?remaining*Math.Max(0,weights[i])/sum:remaining/pending.Count));
            var fixedIndices=pending.Where(i=>proposed[i]>upper).ToArray();
            if(fixedIndices.Length==0) {foreach(var i in pending) values[i]=proposed[i];break;}
            foreach(var i in fixedIndices) {remaining-=upper-values[i];values[i]=upper;pending.Remove(i);}
        }
        // Remove decimal division residue without changing the seven-day goal.
        var residue=total-values.Sum();
        foreach(var i in Enumerable.Range(0,7)) {var next=Clamp(values[i]+residue,lower,upper);residue-=next-values[i];values[i]=next;if(residue==0) break;}
        return values;
    }
    private static IEnumerable<(string,decimal?,decimal?)> Bounds(CoachPrescriptionRules r) => new[] {
        ("Protein",r.ProteinMin,r.ProteinMax),("Carbs",r.CarbsMin,r.CarbsMax),("Fat",r.FatMin,r.FatMax),
        ("Daily calories",r.DailyCaloriesMin,r.DailyCaloriesMax),("Weekly calories",r.WeeklyCaloriesMin,r.WeeklyCaloriesMax)};
    private static decimal Clamp(decimal value, decimal? min, decimal? max) => Math.Min(max ?? decimal.MaxValue, Math.Max(min ?? 0, value));
    private static decimal Energy(Engine30DashboardDayProjection d) => d.ProteinG * 4 + d.RecommendedCarbsG * 4 + d.FatG * 9;
    private static Engine30DashboardDayProjection Recount(Engine30DashboardDayProjection d) => d with { PrescribedEnergyKcal = Energy(d) };
    private static Engine30DashboardDayProjection Adjust(Engine30DashboardDayProjection d, decimal delta, CoachPrescriptionRules r)
    {
        var carbs = Clamp(d.RecommendedCarbsG + delta / 4, r.CarbsMin, r.CarbsMax);
        delta -= (carbs - d.RecommendedCarbsG) * 4;
        var fat = Clamp(d.FatG + delta / 9, r.FatMin, r.FatMax);
        return Recount(d with { RecommendedCarbsG = carbs, FatG = fat });
    }
    public static CoachRuleEvaluation Evaluate(IReadOnlyList<Engine30DashboardDayProjection> source, CoachPrescriptionRules r)
    {
        var errors = Validate(r).ToList();
        if (source.Count != 7) errors.Add("A complete seven-day engine prescription is required.");
        if (errors.Count > 0 || !r.Enabled) return new(source, errors);
        var days = source.OrderBy(x => x.Date).Select(d => Recount(d with {
            ProteinG = Clamp(d.ProteinG,r.ProteinMin,r.ProteinMax), RecommendedCarbsG = Clamp(d.RecommendedCarbsG,r.CarbsMin,r.CarbsMax), FatG = Clamp(d.FatG,r.FatMin,r.FatMax)
        })).ToArray();
        try
        {
            var carbs=r.WeeklyCarbsTarget.HasValue?AllocateWeekly(r.WeeklyCarbsTarget.Value,days.Select(d=>d.RecommendedCarbsG).ToArray(),r.CarbsMin,r.CarbsMax):null;
            var fats=r.WeeklyFatTarget.HasValue?AllocateWeekly(r.WeeklyFatTarget.Value,Enumerable.Repeat(1m,7).ToArray(),r.FatMin,r.FatMax):null;
            if(r.DailyProteinTarget.HasValue && Clamp(r.DailyProteinTarget.Value,r.ProteinMin,r.ProteinMax)!=r.DailyProteinTarget.Value)
                errors.Add("Daily protein goal conflicts with its limits.");
            for(var i=0;i<7;i++) days[i]=Recount(days[i] with {ProteinG=r.DailyProteinTarget ?? days[i].ProteinG,RecommendedCarbsG=carbs?[i] ?? days[i].RecommendedCarbsG,FatG=fats?[i] ?? days[i].FatG});
        }
        catch(ArgumentException ex) {errors.Add(ex.Message);}
        for (var i = 0; i < days.Length; i++)
        {
            if(Energy(days[i])<=0) errors.Add("Macro targets must provide positive energy.");
            var goal = Clamp(Energy(days[i]),r.DailyCaloriesMin,r.DailyCaloriesMax);
            if (r.AdjustCarbsThenFat) days[i] = Adjust(days[i],goal-Energy(days[i]),r);
            if (Math.Abs(goal-Energy(days[i])) > 0.01m) errors.Add($"{days[i].Date:MMM d}: macro targets cannot meet the daily calorie limits with these rules.");
        }
        var total = days.Sum(Energy);
        var weeklyGoal = Clamp(total,r.WeeklyCaloriesMin,r.WeeklyCaloriesMax);
        if (r.AdjustCarbsThenFat && errors.Count == 0)
        {
            // Distribute within daily and macro headroom, preserving protein and workload variation.
            for (var attempt=0; attempt<16 && Math.Abs(weeklyGoal-days.Sum(Energy))>0.01m; attempt++)
            {
                var delta=weeklyGoal-days.Sum(Energy);
                var capacity=days.Select(d => {
                    var macroEdge=d.ProteinG*4 + (delta>0 ? r.CarbsMax!.Value : r.CarbsMin!.Value)*4 + (delta>0 ? r.FatMax!.Value : r.FatMin!.Value)*9;
                    var edge=delta>0 ? Math.Min(macroEdge,r.DailyCaloriesMax ?? macroEdge) : Math.Max(macroEdge,r.DailyCaloriesMin ?? macroEdge);
                    return Math.Max(0, delta>0 ? edge-Energy(d) : Energy(d)-edge);
                }).ToArray();
                var eligible=capacity.Count(x=>x>0.001m);
                if(eligible==0) break;
                var share=Math.Abs(delta)/eligible;
                for(var i=0;i<days.Length;i++) if(capacity[i]>0.001m) days[i]=Adjust(days[i],Math.Sign(delta)*Math.Min(share,capacity[i]),r);
            }
        }
        if(Math.Abs(weeklyGoal-days.Sum(Energy))>0.01m) errors.Add("Macro targets and daily calorie limits cannot meet the weekly calorie limits with these rules.");
        return new(days,errors);
    }
}
