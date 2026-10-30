using System.Text.Json;
namespace WebRecomp.Web.Services.Coaching;

public sealed record AccountabilityCharge(string Key, DateOnly Date, string Kind, string Description, decimal Amount, decimal RawAmount, bool Ready = true, bool Settle = true, string TermsKey = "");
public static class AccountabilityAssessment
{
    public static string RuleTermsKey(AccountabilityState state,AccountabilityRule rule) => AccountabilityDocument.HashTerms(JsonSerializer.Serialize(new{rule,state.Agreement},AccountabilityDocument.Json));
    public static (DateOnly Start,DateOnly End) Period(DateOnly d,string frequency) => frequency switch {
        "daily"=>(d,d),"weekly"=>(d.AddDays(-(((int)d.DayOfWeek+6)%7)),d.AddDays(6-(((int)d.DayOfWeek+6)%7))),
        _=>(new(d.Year,d.Month,1),new(d.Year,d.Month,DateTime.DaysInMonth(d.Year,d.Month))) };
    public static decimal Cents(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static DateOnly? End(AccountabilityState s) => s.Agreement.Start == "" ? null : s.Agreement.Mode == "weeks"
        ? AccountabilityDocument.Date(s.Agreement.Start).AddDays(s.Agreement.Weeks * 7 - 1) : s.Agreement.EndedOn is { } end ? AccountabilityDocument.Date(end) : null;
    public static bool Active(AccountabilityState s, DateOnly date) => s.Agreement.Start != "" && date >= AccountabilityDocument.Date(s.Agreement.Start) && (End(s) is not { } end || date <= end);
    public static bool Weight(AccountabilityExercise e, decimal? weight) => weight >= e.Weight && (!e.MaxWeight.HasValue || weight <= e.MaxWeight);
    public static IReadOnlyList<AccountabilityCharge> Snap(AccountabilityState s, DateOnly today)
    {
        var rows = new List<AccountabilityCharge>();
        foreach (var w in s.SnapWorkouts)
        {
            var date = AccountabilityDocument.Date(w.Date); var totals = new Dictionary<string,decimal>{{"fine",0},{"incentive",0}};
            s.SnapResults.TryGetValue(w.Id, out var result);
            var eligible = Active(s,date) && !s.Excused.GetValueOrDefault(w.Date);
            var ready = date <= today && result is { Finalized:true };
            var onTime = !w.DeadlineUtc.HasValue || (result?.CompletedAtUtc is { } completed && completed <= w.DeadlineUtc);
            void Add(string key, string kind, string description, decimal raw)
            {
                raw = Cents(eligible && ready ? raw : 0); var amount = Cents(Math.Min(raw, Math.Max(0,w.Cap-totals[kind]))); totals[kind] += amount;
                rows.Add(new(key,date,kind,description,amount,raw,ready || !eligible));
            }
            if (w.CompletionReward.GetValueOrDefault()>0)
                Add($"snap:{w.Id}:completion","incentive",$"{w.Name} · Whole-workout completion",onTime && result is not null && SnapWorkoutLibrary.Completed(w,result) ? w.CompletionReward!.Value : 0);
            foreach (var e in w.Exercises)
            {
                var sets = result?.Sets.GetValueOrDefault(e.Id) ?? [];
                var totalReps = sets.Where(x=>Weight(e,x.Weight)).Sum(x=>x.Reps??0);
                var totalSets = sets.Count(x=>x.Reps>=e.Reps && Weight(e,x.Weight));
                var reps = Math.Min(e.Sets*e.Reps,totalReps); var count = Math.Min(e.Sets,totalSets);
                var units = e.Basis switch {"completed-reps"=>reps,"completed-sets"=>count,"incomplete-reps"=>e.Sets*e.Reps-reps,_=>e.Sets-count};
                Add($"snap:{w.Id}:{e.Id}:base",e.Type,$"{w.Name} · {e.Name} · {units} {e.Basis} × ${e.Rate:0.00}",e.Type=="incentive"&&!onTime?0:units*e.Rate);
                var extra = count == e.Sets ? Math.Max(0,totalReps-e.Sets*e.Reps):0;
                if(e.CompletionReward.GetValueOrDefault()>0 || e.ExtraRepRate.GetValueOrDefault()>0)
                    Add($"snap:{w.Id}:{e.Id}:reward","incentive",$"{w.Name} · {e.Name} · Exercise completion ${e.CompletionReward.GetValueOrDefault():0.00} + {extra} extra reps × ${e.ExtraRepRate.GetValueOrDefault():0.00}",
                        onTime && count==e.Sets ? e.CompletionReward.GetValueOrDefault()+extra*e.ExtraRepRate.GetValueOrDefault():0);
            }
        }
        return rows;
    }
    public static IReadOnlyList<AccountabilityCharge> Rules(AccountabilityState s, IReadOnlyDictionary<DateOnly,JsonElement> days, DateOnly today)
    {
        var rows = new List<AccountabilityCharge>();
        if (!s.Enabled || s.Agreement.Start=="") return rows;
        foreach(var r in s.Rules.Where(r=>r.Assigned))
        {
            foreach(var bounds in days.Keys.Select(d=>Period(d,r.Frequency)).Distinct().OrderBy(x=>x.Start))
            {
                var start = AccountabilityDocument.Date(s.Agreement.Start); var from = bounds.Start<start?start:bounds.Start;
                var end=End(s); var until=end.HasValue&&end<bounds.End?end.Value:bounds.End;
                if(from>until || until>=today) continue;
                var included = new List<JsonElement>(); var complete=true;
                for(var d=from;d<=until;d=d.AddDays(1))
                {
                    if(!days.TryGetValue(d,out var item)||!item.TryGetProperty("completed",out var done)||!done.GetBoolean()){complete=false;break;}
                    if(!s.Excused.GetValueOrDefault(d.ToString("yyyy-MM-dd")))included.Add(item);
                }
                if(!complete)continue;
                decimal? value=0,target=0; var macro=Array.IndexOf(AccountabilityDocument.RuleIds,r.Id)<4;
                foreach(var day in included)
                {
                    if(macro)
                    {
                        decimal? Metric(string parent) => day.TryGetProperty(parent,out var p)&&p.ValueKind==JsonValueKind.Object&&p.TryGetProperty(r.Id,out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetDecimal(out var v)?v:null;
                        var actual=Metric("actual");var prescribed=Metric("target");
                        if(!actual.HasValue||!prescribed.HasValue||prescribed<=0){value=null;break;}value+=actual;target+=prescribed;
                    }
                    else
                    {
                        var date=day.GetProperty("date").GetString()!;
                        if(!s.Observations.TryGetValue(date,out var counts)||!counts.TryGetValue(r.Id,out var n)){value=null;break;}value+=n;
                    }
                }
                if(!value.HasValue)continue;
                var lower=macro?target*(1+r.Lower/100):r.Lower;var upper=macro?target*(1+r.Upper/100):r.Upper;
                var outside=value<lower||value>upper;
                var applies=included.Count>0&&(r.Id=="cheat-foods"?value>0:r.Type=="fine"?outside:!outside);
                var raw=applies?r.Amount*(r.Id=="cheat-foods"?value.Value:1):0;var amount=Cents(Math.Min(raw,r.Max));
                rows.Add(new($"rule:{r.Id}:{bounds.Start:yyyy-MM-dd}:{bounds.End:yyyy-MM-dd}",until,r.Type,$"{r.Id} · {from:MMM d}–{until:MMM d, yyyy}",amount,Cents(raw),true,amount>=r.Min,RuleTermsKey(s,r)));
            }
        }
        return rows;
    }
}
