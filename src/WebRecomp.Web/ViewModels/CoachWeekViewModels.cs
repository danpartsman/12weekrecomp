using System.ComponentModel.DataAnnotations;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
namespace WebRecomp.Web.ViewModels;
public sealed class CoachWeekInput
{
    public CoachPrescriptionRules Rules { get; set; } = new();
    public List<string> Workouts { get; set; } = [];
    public string? PreviewToken { get; set; }
}
public sealed record CoachWeekWorkoutOption(string Code,string Name,string Group);
public sealed class CoachWeekPage
{
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = "";
    public CoachWeekInput Input { get; init; } = new();
    public IReadOnlyList<CoachWeekWorkoutOption> Options { get; init; } = [];
    public IReadOnlyList<Engine30DashboardDayProjection> Engine { get; init; } = [];
    public IReadOnlyList<Engine30DashboardDayProjection> Assigned { get; init; } = [];
    public IReadOnlyList<Engine30DashboardDayProjection>? Preview { get; set; }
    public IReadOnlyList<PlannedWorkout> Proposed { get; set; } = [];
    public string Fingerprint { get; set; } = "";
    public string? PreviewToken { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}
