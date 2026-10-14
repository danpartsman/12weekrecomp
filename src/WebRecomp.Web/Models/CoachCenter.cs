namespace WebRecomp.Web.Models;

public sealed class CoachProfile
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Bio { get; set; }
    public string? Specialties { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CoachClientRelationship
{
    public Guid CoachClientRelationshipId { get; set; }
    public Guid CoachUserId { get; set; }
    public Guid? ClientId { get; set; }
    public string StatusCode { get; set; } = "INVITED";
    public string? InviteTokenHash { get; set; }
    public DateTime InviteExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
