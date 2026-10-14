using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;
public sealed class CoachPrescriptionRuleStore(RecompDbContext db)
{
    public async Task<IReadOnlyDictionary<DateOnly,Engine30DashboardDayProjection>> ApplyAsync(Guid clientId, Guid programId, DateOnly week, IReadOnlyDictionary<DateOnly,Engine30DashboardDayProjection> baseline, CancellationToken ct)
    {
        var activeCoaches=db.CoachClientRelationships.Where(x=>x.ClientId==clientId && x.StatusCode=="ACTIVE" && x.RevokedAtUtc==null).Select(x=>x.CoachUserId);
        var version=await db.CoachPrescriptionRuleVersions.AsNoTracking().Where(x=>x.ClientId==clientId && x.ProgramId==programId && x.EffectiveWeek<=week && activeCoaches.Contains(x.CoachUserId))
            .OrderByDescending(x=>x.EffectiveWeek).ThenByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.CoachPrescriptionRuleVersionId).FirstOrDefaultAsync(ct);
        if(version is null || !version.IsEnabled) return baseline;
        var rules=JsonSerializer.Deserialize<CoachPrescriptionRules>(version.RulesJson)!;
        var result=CoachPrescriptionAllocator.Evaluate(baseline.Values.ToArray(),rules);
        var notice=result.Errors.Count==0 ? "Your trainer’s prescription rules are applied." : "Your trainer’s limits need review. The engine prescription is shown: "+string.Join(" ",result.Errors);
        var days=result.Errors.Count==0 ? result.Days : baseline.Values.ToArray();
        return days.ToDictionary(x=>x.Date,x=>x with { CoachPrescriptionNotice=notice, CoachPrescriptionVersionId=version.CoachPrescriptionRuleVersionId });
    }
}
