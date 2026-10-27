using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
namespace WebRecomp.Web.Services.Coaching;

public sealed class AccountabilityDefaults
{
    public string Revision { get; set; } = "";
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

// Private App Service storage survives deployments. The runtime SQL role intentionally
// cannot write engine configuration; coach defaults do not require expanding that role.
public sealed class AccountabilityDefaultsStore
{
    private readonly string root;
    private readonly IDataProtector protector;
    public AccountabilityDefaultsStore(IWebHostEnvironment environment, IConfiguration config, IDataProtectionProvider protection)
    {
        root = config["CoachDefaults:DataPath"] ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? environment.ContentRootPath, "data", "coach-defaults");
        Directory.CreateDirectory(root);
        protector = protection.CreateProtector("12WR.CoachDefaults.v1");
    }
    public async Task<AccountabilityDefaults> ReadAsync(CancellationToken ct)
    {
        var path = Path.Combine(root, "defaults.json");
        if (!File.Exists(path)) return new();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var encrypted = await reader.ReadToEndAsync(ct);
        var result = JsonSerializer.Deserialize<AccountabilityDefaults>(protector.Unprotect(encrypted))
            ?? throw new InvalidOperationException("Coach defaults are unavailable.");
        result.Validate(); return result;
    }
    public async Task SaveAsync(AccountabilityDefaults d, CancellationToken ct)
    {
        d.Validate();
        await using var lease = await LeaseAsync(ct);
        var saved = await ReadAsync(ct);
        if (d.Revision != saved.Revision) throw new ArgumentException("Defaults changed in another window. Reload this page before saving again.");
        var revision = Guid.NewGuid().ToString("N");
        var previousRevision = d.Revision; d.Revision = revision;
        var temporary = Path.Combine(root, revision + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, protector.Protect(JsonSerializer.Serialize(d)), ct);
            File.Move(temporary, Path.Combine(root, "defaults.json"), true);
        }
        catch { d.Revision = previousRevision; throw; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<FileStream> LeaseAsync(CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(root, "defaults.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 50) { await Task.Delay(100, ct); }
        }
    }
}
