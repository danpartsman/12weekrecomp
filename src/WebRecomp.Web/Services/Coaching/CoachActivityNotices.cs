using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.Notifications;
namespace WebRecomp.Web.Services.Coaching;

public sealed record CoachActivityNotice(string Id, string Title, string Url, DateTime CreatedUtc);
public static class CoachActivityNotices
{
    public static string ReceiptKey(Guid coach) => $"users/coach-activity-{coach:N}";
    public static async Task<IReadOnlyList<CoachActivityNotice>> ReadAsync(RecompDbContext db, NotificationFiles files, Guid coach, CancellationToken ct)
    {
        var clients = await db.CoachClientRelationships.AsNoTracking().Where(x => x.CoachUserId == coach && x.StatusCode == "ACTIVE" && x.RevokedAtUtc == null)
            .Select(x => x.ClientId).Distinct().ToListAsync(ct);
        var seen = files.Read<List<string>>(ReceiptKey(coach)) ?? [];
        var notices = new List<CoachActivityNotice>();
        var since = DateTime.UtcNow.AddDays(-30);
        foreach(var client in clients)
        {
            if(!await db.Clients.AnyAsync(x=>x.ClientId==client && x.ClientStatusCode=="ACTIVE",ct)) continue;
            var owners = await db.UserClientAccess.AsNoTracking().Where(x=>x.ClientId==client && x.AccessRoleCode=="OWNER" && x.RevokedAtUtc==null).Select(x=>x.UserId).Distinct().Take(2).ToListAsync(ct);
            if(owners.Count!=1) continue;
            var owner=await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==owners[0] && x.AccountStatusCode=="ACTIVE",ct);
            if(owner is null) continue;
            var name=owner.PreferredDisplayName ?? owner.DisplayName ?? "Your client";
            var agreement=files.Read<CoachFinancialAgreement>($"financial-agreements/{client:N}-{coach:N}");
            if(agreement is not null && agreement.UpdatedBy==owner.UserId && agreement.UpdatedUtc>=since && agreement.Status is "accepted" or "changes-requested" or "declined")
            {
                var action=agreement.Status=="accepted"?"accepted the agreement":agreement.Status=="declined"?"declined the agreement":"requested agreement changes";
                notices.Add(new($"agreement-{client:N}-{agreement.Revision}",$"{name} {action}",$"/coach-workspace/{client:D}/{coach:D}#financial-agreement",agreement.UpdatedUtc));
            }
            var events=await db.CoachAccountabilityEvents.AsNoTracking().Where(x=>x.ClientId==client && (x.Kind=="CONFIG" || (x.Kind=="RESULT" && x.ActorUserId==owner.UserId && x.CreatedAtUtc>=since))).OrderBy(x=>x.Revision).ToListAsync(ct);
            var assigned=new Dictionary<string,(Guid Coach,string Name)>();
            var completed=new HashSet<string>();
            foreach(var evt in events)
            {
                try
                {
                    if(evt.Kind=="CONFIG")
                    {
                        foreach(var workout in AccountabilityDocument.Parse(evt.PayloadJson).SnapWorkouts)
                            assigned.TryAdd(workout.Id,(evt.ActorUserId,workout.Name));
                    }
                    else
                    {
                        using var json=JsonDocument.Parse(evt.PayloadJson);
                        if(!json.RootElement.TryGetProperty("workoutId",out var id) || !json.RootElement.TryGetProperty("result",out var result)) continue;
                        var workoutId=id.GetString();
                        var outcome=result.Deserialize<AccountabilityResult>(AccountabilityDocument.Json);
                        if(workoutId is null || outcome?.Finalized!=true || !assigned.TryGetValue(workoutId,out var workout) || workout.Coach!=coach || !completed.Add(workoutId)) continue;
                        notices.Add(new($"snap-{client:N}-{workoutId}",$"{name} completed {workout.Name}",$"/clients/{client:D}/snap?assignment={Uri.EscapeDataString(workoutId)}#snap-{Uri.EscapeDataString(workoutId)}",evt.CreatedAtUtc));
                    }
                }
                catch(Exception e) when(e is JsonException or ArgumentException) { /* Ignore invalid historical entries. */ }
            }
        }
        return notices.Where(x=>!seen.Contains(x.Id)).OrderByDescending(x=>x.CreatedUtc).Take(50).ToArray();
    }
}
