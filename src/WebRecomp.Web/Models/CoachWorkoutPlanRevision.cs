namespace WebRecomp.Web.Models;
public sealed class CoachWorkoutPlanRevision
{
    public Guid CoachWorkoutPlanRevisionId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid CoachUserId { get; set; }
    public DateOnly WorkoutDate { get; set; }
    public required string Reason { get; set; }
    public required string BeforeJson { get; set; }
    public required string AfterJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
