using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Coaching;

public sealed record AccountabilityApproval(Guid UserId, Guid ClientId, string TermsHash, string SignedBy, DateTimeOffset ApprovedAt, DateTimeOffset ExpiresAt);
public sealed class AccountabilityApprovalReceipt(IDataProtectionProvider protection, TimeProvider? clock = null)
{
    private readonly IDataProtector protector = protection.CreateProtector("12WR.TEST.AccountabilityPreview.Approval.v1");
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public static string Hash(string terms) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(terms)));
    public string Issue(Guid userId, Guid clientId, string terms, string signedBy)
    {
        var now = time.GetUtcNow();
        return protector.Protect(JsonSerializer.Serialize(new AccountabilityApproval(userId, clientId, Hash(terms), signedBy, now, now.AddDays(30))));
    }
    public AccountabilityApproval? Read(string token, Guid clientId, string terms)
    {
        if (token.Length > 8192 || terms.Length > 32768) return null;
        try
        {
            var receipt = JsonSerializer.Deserialize<AccountabilityApproval>(protector.Unprotect(token));
            return receipt is not null && receipt.ClientId == clientId && receipt.TermsHash == Hash(terms)
                && receipt.ApprovedAt <= time.GetUtcNow() && receipt.ExpiresAt > time.GetUtcNow() ? receipt : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { return null; }
    }
}

/// <summary>Read-only coached-client access and evidence for the TEST preview. No financial ledger is stored.</summary>
public sealed class CoachAccountabilityPreviewService(RecompDbContext db, CoachCenterService coaches, ICoachSummaryPrescriptionSource targets)
{
    public async Task<bool> CanApproveAsync(Guid userId, Guid clientId, CancellationToken ct) =>
        await db.Clients.AnyAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct)
        && await db.UserClientAccess.AnyAsync(x => x.UserId == userId && x.ClientId == clientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null, ct)
        && await db.CoachClientRelationships.AnyAsync(x => x.ClientId == clientId && x.StatusCode == "ACTIVE" && x.RevokedAtUtc == null, ct);
    public async Task<bool> CanViewAsync(Guid userId, Guid clientId, bool coachRole, CancellationToken ct) =>
        await CanApproveAsync(userId, clientId, ct) || (coachRole && await coaches.CanAccessAsync(userId, clientId, ct));
    public Task<bool> IsOwnerAsync(Guid userId, Guid clientId, CancellationToken ct) => db.UserClientAccess.AnyAsync(x => x.UserId == userId && x.ClientId == clientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null, ct);
    public async Task<object?> EvidenceAsync(UserAccount user, Guid clientId, DateOnly? selected, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct);
        if (client is null) return null;
        var today = WebRecomp.Web.Services.AppUserContextService.GetClientToday(client);
        var date = selected ?? today;
        if (date < today.AddDays(-84) || date > today) throw new ArgumentOutOfRangeException(nameof(selected));
        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        // The prescription source returns reference-7 through reference+1.
        var prescribed = await targets.LoadAsync(user.UserId, client, monday.AddDays(7), ct);
        var days = await db.NutritionDays.AsNoTracking().Where(x => x.ClientId == clientId && x.NutritionDate >= monday && x.NutritionDate <= monday.AddDays(6)).ToListAsync(ct);
        return new { today, weekStart = monday, notice = prescribed.Notice,
            days = Enumerable.Range(0, 7).Select(i => {
                var d = monday.AddDays(i); var intake = days.Where(x => x.NutritionDate == d).OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
                var p = prescribed.Days.GetValueOrDefault(d);
                return new { date = d, completed = d < today,
                    actual = new { calories = intake?.ActualCalories, protein = intake?.ActualProteinG, carbs = intake?.ActualCarbsG, fat = intake?.ActualFatG },
                    target = p is null ? null : new { calories = p.Calories, protein = p.ProteinG, carbs = p.CarbsG, fat = p.FatG }, source = p?.Source };
            }).ToArray() };
    }
}
