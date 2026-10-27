using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.Coaching;

public sealed class AccountabilityDefaults
{
    public List<AccountabilityRuleDefault> Rules { get; set; } = AccountabilityDocument.RuleIds.Select((id, i) => new AccountabilityRuleDefault
        { Id = id, Amount = new decimal[] { 2,2,2,2,10,5,2,5,2,5 }[i] }).ToList();
    public decimal PaymentAmount { get; set; }
    public decimal LateValue { get; set; }
    public decimal LateCap { get; set; } = 100;
    public decimal SnapCap { get; set; } = 100;
    public decimal SnapRate { get; set; } = .1m;
    public decimal CompletionReward { get; set; }
    public decimal ExtraRepRate { get; set; }
    public void Validate()
    {
        if (Rules is null || !Rules.Select(x => x.Id).SequenceEqual(AccountabilityDocument.RuleIds)
            || Rules.Any(x => !AccountabilityDocument.Money(x.Amount) || !AccountabilityDocument.Money(x.Min)
                || !AccountabilityDocument.Money(x.Max) || x.Min > x.Max || (x.Id == "cheat-foods" && x.Amount <= 0))
            || !AccountabilityDocument.Money(PaymentAmount) || !AccountabilityDocument.Money(LateValue)
            || !AccountabilityDocument.Money(LateCap) || !AccountabilityDocument.Money(SnapCap)
            || !AccountabilityDocument.Money(SnapRate, 100) || !AccountabilityDocument.Money(CompletionReward)
            || !AccountabilityDocument.Money(ExtraRepRate, 100))
            throw new ArgumentException("Enter valid dollar amounts, with each minimum no greater than its maximum. Cheat-food rates must be positive. Snap per-unit rates cannot exceed $100.");
    }
}
public sealed class AccountabilityRuleDefault
{
    public string Id { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Min { get; set; }
    public decimal Max { get; set; } = 100;
}

// Uses the existing versioned settings table; defaults never rewrite signed client terms.
public sealed class AccountabilityDefaultsStore(RecompDbContext db)
{
    private const string Prefix = "COACH_DEFAULT_";
    private async Task<Guid> ConfigAsync(CancellationToken ct) => await db.ConfigVersions.AsNoTracking()
        .Where(x => x.VersionCode == "CFG-3000.0").Select(x => x.ConfigVersionId).SingleAsync(ct);
    private static Dictionary<string, decimal> Values(AccountabilityDefaults d)
    {
        var values = new Dictionary<string, decimal>();
        foreach (var r in d.Rules) { values[r.Id + "_amount"] = r.Amount; values[r.Id + "_min"] = r.Min; values[r.Id + "_max"] = r.Max; }
        values["payment"] = d.PaymentAmount; values["late_value"] = d.LateValue; values["late_cap"] = d.LateCap;
        values["snap_cap"] = d.SnapCap; values["snap_rate"] = d.SnapRate; values["completion_reward"] = d.CompletionReward; values["extra_rep_rate"] = d.ExtraRepRate;
        return values;
    }
    public async Task<AccountabilityDefaults> ReadAsync(CancellationToken ct)
    {
        var d = new AccountabilityDefaults();
        var config = await db.ConfigVersions.AsNoTracking().Where(x => x.VersionCode == "CFG-3000.0").Select(x => (Guid?)x.ConfigVersionId).SingleOrDefaultAsync(ct);
        if (config is null) return d;
        var rows = await db.GlobalSettings.AsNoTracking().Where(x => x.ConfigVersionId == config && x.SettingCode.StartsWith(Prefix)).ToListAsync(ct);
        var saved = rows.ToDictionary(x => x.SettingCode, x => x.DecimalValue);
        decimal Get(string key, decimal fallback) => saved.GetValueOrDefault(Prefix + key) ?? fallback;
        foreach (var r in d.Rules) { r.Amount = Get(r.Id + "_amount", r.Amount); r.Min = Get(r.Id + "_min", r.Min); r.Max = Get(r.Id + "_max", r.Max); }
        d.PaymentAmount = Get("payment", d.PaymentAmount); d.LateValue = Get("late_value", d.LateValue); d.LateCap = Get("late_cap", d.LateCap);
        d.SnapCap = Get("snap_cap", d.SnapCap); d.SnapRate = Get("snap_rate", d.SnapRate); d.CompletionReward = Get("completion_reward", d.CompletionReward); d.ExtraRepRate = Get("extra_rep_rate", d.ExtraRepRate);
        d.Validate(); return d;
    }
    public async Task SaveAsync(AccountabilityDefaults d, CancellationToken ct)
    {
        d.Validate();
        var config = await ConfigAsync(ct);
        var rows = await db.GlobalSettings.Where(x => x.ConfigVersionId == config && x.SettingCode.StartsWith(Prefix)).ToListAsync(ct);
        foreach (var value in Values(d))
        {
            var code = Prefix + value.Key;
            var row = rows.SingleOrDefault(x => x.SettingCode == code);
            if (row is null) { row = new GlobalSetting { GlobalSettingId = Guid.NewGuid(), ConfigVersionId = config, SettingCode = code, Description = "Coach agreement default: " + value.Key, UnitCode = "USD" }; db.GlobalSettings.Add(row); }
            row.DecimalValue = value.Value;
        }
        await db.SaveChangesAsync(ct);
    }
}
