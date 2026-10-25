namespace WebRecomp.Web.Models;
public sealed class CoachAccountabilityAgreement
{
    public Guid ClientId { get; set; }
    public Guid CoachUserId { get; set; }
    public string StateJson { get; set; } = "{}";
    public string TermsJson { get; set; } = "{}";
    public string TermsHash { get; set; } = "";
    public int Revision { get; set; }
    public int TermsVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
public sealed class CoachAccountabilityApproval
{
    public Guid ApprovalId { get; set; }
    public Guid ClientId { get; set; }
    public Guid CoachUserId { get; set; }
    public Guid OwnerUserId { get; set; }
    public int TermsVersion { get; set; }
    public string TermsHash { get; set; } = "";
    public string TermsJson { get; set; } = "{}";
    public string SignedBy { get; set; } = "";
    public DateTime ApprovedAtUtc { get; set; }
}
public sealed class CoachAccountabilityEvent
{
    public Guid EventId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Kind { get; set; } = "";
    public int Revision { get; set; }
    public int TermsVersion { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
}
