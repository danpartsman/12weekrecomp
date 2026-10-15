using System.ComponentModel.DataAnnotations;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Engine30;
namespace WebRecomp.Web.ViewModels;
public sealed class CoachPrescriptionRules
{
    [Range(1,1000)] public decimal? DailyProteinTarget { get; set; }
    [Range(0,14000)] public decimal? WeeklyCarbsTarget { get; set; }
    [Range(0,3500)] public decimal? WeeklyFatTarget { get; set; }
    [Range(0,1000)] public decimal? ProteinMin { get; set; }
    [Range(0,1000)] public decimal? ProteinMax { get; set; }
    [Range(0,2000)] public decimal? CarbsMin { get; set; }
    [Range(0,2000)] public decimal? CarbsMax { get; set; }
    [Range(0,500)] public decimal? FatMin { get; set; }
    [Range(0,500)] public decimal? FatMax { get; set; }
    [Range(0,30000)] public decimal? DailyCaloriesMin { get; set; }
    [Range(0,30000)] public decimal? DailyCaloriesMax { get; set; }
    [Range(0,210000)] public decimal? WeeklyCaloriesMin { get; set; }
    [Range(0,210000)] public decimal? WeeklyCaloriesMax { get; set; }
    public bool AdjustCarbsThenFat { get; set; }
    public bool Enabled { get; set; } = true;
    public DateOnly EffectiveWeek { get; set; }
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
    public Guid? ExpectedVersionId { get; set; }
    public string? PreviewToken { get; set; }
}
public sealed class CoachPrescriptionPage
{
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = "";
    public CoachPrescriptionRules Rules { get; init; } = new();
    public IReadOnlyList<CoachPrescriptionRuleVersion> History { get; init; } = [];
    public IReadOnlyDictionary<Guid,string> Authors { get; init; } = new Dictionary<Guid,string>();
    public IReadOnlyList<Engine30DashboardDayProjection> EngineDays { get; init; } = [];
    public CoachRuleEvaluation? Preview { get; init; }
    public string? PreviewToken { get; set; }
}
