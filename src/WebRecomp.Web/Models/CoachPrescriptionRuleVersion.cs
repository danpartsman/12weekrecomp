namespace WebRecomp.Web.Models;
public sealed class CoachPrescriptionRuleVersion
{
    public Guid CoachPrescriptionRuleVersionId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid CoachUserId { get; set; }
    public DateOnly EffectiveWeek { get; set; }
    public bool IsEnabled { get; set; }
    public required string RulesJson { get; set; }
    public required string Reason { get; set; }
    public required string PreviewJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
