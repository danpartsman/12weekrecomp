using System.ComponentModel.DataAnnotations;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.ViewModels;

public sealed class CoachProfileInput
{
    [Required, StringLength(100)] public string DisplayName { get; set; } = "";
    [StringLength(2000)] public string? Bio { get; set; }
    [StringLength(500)] public string? Specialties { get; set; }
}
public sealed record CoachRosterClient(Guid ClientId, string Name, string? Goal, DateOnly? LastWorkout);
public sealed record CoachCenterViewModel(CoachProfileInput Profile, IReadOnlyList<CoachRosterClient> Clients, int PendingInvites);
public sealed record CoachClientDetailViewModel(Client Client, ProgramRecord? Program, Measurement? Measurement, IReadOnlyList<WorkoutSession> Workouts)
{
    public CoachClientPlan? Plan { get; init; }
}
public sealed class CoachConnectInput
{
    [Required, StringLength(64, MinimumLength = 6)] public string Code { get; set; } = "";
}
public sealed record MyCoachesViewModel(IReadOnlyList<CoachConnection> Coaches, CoachConnectInput Input);
public sealed record CoachConnection(Guid RelationshipId, string Name, string? Bio, string? Specialties);
