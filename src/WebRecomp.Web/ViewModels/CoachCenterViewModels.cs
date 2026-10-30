using System.ComponentModel.DataAnnotations;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.ViewModels;

public sealed class CoachProfileInput
{
    [Required, StringLength(100)] public string DisplayName { get; set; } = "";
    [StringLength(2000)] public string? Bio { get; set; }
    [StringLength(500)] public string? Specialties { get; set; }
}
public sealed record CoachSnapStatus(int Assigned=0,int Submitted=0,int Completed=0,string? LatestWorkoutId=null,string? LatestName=null,string? Status=null);
public sealed record CoachRosterClient(Guid ClientId, string Name, string? Goal, DateOnly? LastWorkout) { public CoachSnapStatus Snap {get;init;}=new(); }
public sealed record CoachCenterViewModel(CoachProfileInput Profile, IReadOnlyList<CoachRosterClient> Clients, int PendingInvites);
public sealed record CoachClientDetailViewModel(Client Client, ProgramRecord? Program, Measurement? Measurement, IReadOnlyList<WorkoutSession> Workouts)
{
    public CoachClientPlan? Plan { get; init; }
}
public sealed class CoachConnectInput
{
    [Required, StringLength(64, MinimumLength = 6)] public string Code { get; set; } = "";
}
public sealed record MyCoachesViewModel(IReadOnlyList<CoachConnection> Coaches, CoachConnectInput Input) { public Guid? ClientId { get; init; } }
public sealed record CoachConnection(Guid RelationshipId, string Name, string? Bio, string? Specialties);
