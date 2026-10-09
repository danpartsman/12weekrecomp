using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/calorie-audit")]
public sealed class AdminCalorieAuditController(
    RecompDbContext db,
    Engine30DailyRuntimeService runtime,
    Engine30PrescriptionService prescriptions,
    Engine30GoalEnergyPolicyService policies,
    Engine30RecompEnergyPolicyAllocator allocator) : Controller
{
    public sealed record AuditProfile(string Id, string Description, string SexCode,
        int AgeYears, decimal WeightLb, decimal HeightIn, decimal WorkHours,
        string WorkActivityCode, string LifestyleActivityCode);
    public sealed record AuditWorkout(string Code, decimal DurationMin, decimal Rpe);

    public static IReadOnlyList<AuditProfile> Profiles() => new AuditProfile[]
    {
        new("P01", "F45 missing-activity reference", "FEMALE", 45, 146, 61, 0, "TYPICAL_MIXED", "MINIMAL"),
        new("P02", "F45 on feet 40h, moderate outside", "FEMALE", 45, 146, 61, 40, "ACTIVE_LIGHT", "MODERATE"),
        new("P03", "F45 active physical 40h, very active outside", "FEMALE", 45, 146, 61, 40, "VERY_ACTIVE", "VERY_ACTIVE"),
        new("P04", "F45 heavy manual 40h, very active outside", "FEMALE", 45, 146, 61, 40, "HEAVY_PHYSICAL", "VERY_ACTIVE"),
        new("P05", "F45 heavy manual 60h, very active outside", "FEMALE", 45, 146, 61, 60, "HEAVY_PHYSICAL", "VERY_ACTIVE"),
        new("P06", "M45 sex comparison to P04", "MALE", 45, 146, 61, 40, "HEAVY_PHYSICAL", "VERY_ACTIVE"),
        new("P07", "F25 light outside, no work", "FEMALE", 25, 120, 64, 0, "TYPICAL_MIXED", "LIGHT"),
        new("P08", "M25 light outside, no work", "MALE", 25, 150, 70, 0, "TYPICAL_MIXED", "LIGHT"),
        new("P09", "F25 desk 40h, minimal outside", "FEMALE", 25, 180, 64, 40, "HIGHLY_SEDENTARY", "MINIMAL"),
        new("P10", "M25 desk 40h, minimal outside", "MALE", 25, 220, 70, 40, "HIGHLY_SEDENTARY", "MINIMAL"),
        new("P11", "F45 mixed work 40h, moderate outside", "FEMALE", 45, 160, 64, 40, "TYPICAL_MIXED", "MODERATE"),
        new("P12", "M45 mixed work 40h, moderate outside", "MALE", 45, 200, 70, 40, "TYPICAL_MIXED", "MODERATE"),
        new("P13", "F65 light outside, no work", "FEMALE", 65, 140, 64, 0, "TYPICAL_MIXED", "LIGHT"),
        new("P14", "M65 light outside, no work", "MALE", 65, 180, 70, 0, "TYPICAL_MIXED", "LIGHT"),
        new("P15", "F65 on feet 30h, moderate outside", "FEMALE", 65, 180, 64, 30, "ACTIVE_LIGHT", "MODERATE"),
        new("P16", "M65 on feet 30h, moderate outside", "MALE", 65, 240, 70, 30, "ACTIVE_LIGHT", "MODERATE"),
        new("P17", "F80 minimal outside, no work", "FEMALE", 80, 120, 62, 0, "TYPICAL_MIXED", "MINIMAL"),
        new("P18", "M80 minimal outside, no work", "MALE", 80, 160, 68, 0, "TYPICAL_MIXED", "MINIMAL"),
        new("P19", "F35 heavy manual 50h, very active outside", "FEMALE", 35, 200, 66, 50, "HEAVY_PHYSICAL", "VERY_ACTIVE"),
        new("P20", "M63 seated/walking 25h, moderate outside", "MALE", 63, 210, 73, 25, "SEDENTARY_MIXED", "MODERATE")
    };

    public static IReadOnlyList<AuditWorkout> Workouts() => new AuditWorkout[]
    {
        new("REST", 0, 0), new("MAJOR_PUSH", 60, 8), new("MAJOR_PULL", 60, 8),
        new("MAJOR_LEGS", 60, 8), new("ACCESSORY", 45, 7),
        new("ZONE2_CARDIO", 45, 3), new("STAIR_CLIMBER", 30, 6), new("ORANGETHEORY", 60, 8)
    };

    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(bool download = false, CancellationToken cancellationToken = default)
    {
        var actionDate = new DateOnly(2026, 10, 8);
        var weekStart = new DateOnly(2026, 10, 5);
        var schedule = new[] { "MAJOR_LEGS", "ACCESSORY", "ZONE2_CARDIO", "MAJOR_PULL", "REST", "MAJOR_PUSH", "REST" };
        var config = await db.ConfigVersions.AsNoTracking().SingleAsync(
            x => x.VersionCode == Engine30DashboardProjectionService.ConfigVersionCode, cancellationToken);
        var definitions = await db.WorkoutTypeDefinitions.AsNoTracking()
            .Where(x => x.ConfigVersionId == config.ConfigVersionId && x.IsActive)
            .ToDictionaryAsync(x => x.WorkoutTypeCode, cancellationToken);
        var energyPolicies = await policies.LoadAllAsync(cancellationToken);
        var rows = new List<object>();
        var summary = new StringBuilder();
        foreach (var profile in Profiles())
        {
            var roles = profile.WorkHours == 0 ? Array.Empty<Engine30OccupationalRoleInput>() : new[]
            {
                new Engine30OccupationalRoleInput("Synthetic audit work", profile.WorkActivityCode,
                    profile.WorkHours, Engine30ActivityProfileCatalog.ResolveOccupation(profile.WorkActivityCode).EffectiveOccupationalActivityFactor)
            };
            var results = new Dictionary<string, Engine30DailyRuntimeResult>();
            foreach (var workout in Workouts())
            {
                results[workout.Code] = await runtime.CalculateAsync(new Engine30DailyRuntimeRequest(
                    actionDate, new DateOnly(actionDate.Year - profile.AgeYears, 1, 1), profile.SexCode,
                    profile.HeightIn, profile.WeightLb, null, roles, profile.LifestyleActivityCode,
                    workout.Code == "REST" ? null : new Engine30WorkoutSessionInput(
                        WorkoutTypeCode: workout.Code, PlannedDurationMinutes: workout.DurationMin,
                        PlannedRpeOverride: workout.Rpe)), cancellationToken);
            }
            var rest = results["REST"];
            var inputs = schedule.Select((code, i) => new Engine30PrescriptionDayInput(
                weekStart.AddDays(i), results[code].Energy.MaintenanceEnergyKcal,
                definitions[code].WorkoutCategoryCode,
                results[code].Glycogen.CarbohydrateReloadRequirementG,
                new Engine30BackgroundCarbohydrateCalculator().Calculate(results[code].HabitualActivity)
                    .TotalBackgroundCarbohydrateRequirementG)).ToArray();
            var weeks = energyPolicies.Select(policy => allocator.Apply(
                prescriptions.Calculate(policy.PlanCode, profile.WeightLb, profile.HeightIn, inputs), policy)).ToArray();
            rows.Add(new
            {
                Profile = profile, rest.BaseLiving, rest.HabitualActivity, rest.BodyComposition,
                RestMaintenanceKcal = rest.Energy.MaintenanceEnergyKcal,
                WorkoutResults = Workouts().Select(w => new
                {
                    w.Code, w.DurationMin, w.Rpe,
                    CategoryCode = definitions[w.Code].WorkoutCategoryCode,
                    Energy = results[w.Code].Energy,
                    GrossWorkoutKcal = results[w.Code].Workout?.Energy.GrossWorkoutEnergyKcal ?? 0,
                    GlycogenDemandG = results[w.Code].Glycogen.EstimatedGlycogenDepletedG,
                    GlycogenReloadG = results[w.Code].Glycogen.CarbohydrateReloadRequirementG,
                    BackgroundCarbsG = new Engine30BackgroundCarbohydrateCalculator()
                        .Calculate(results[w.Code].HabitualActivity).TotalBackgroundCarbohydrateRequirementG,
                    Provenance = results[w.Code].Workout?.ProfileProvenance
                }).ToArray(),
                WeeklyPrescriptions = weeks
            });
            summary.Append("<tr><td>").Append(profile.Id).Append("</td><td>")
                .Append(System.Net.WebUtility.HtmlEncode(profile.Description)).Append("</td><td>")
                .Append(rest.Energy.MaintenanceEnergyKcal).Append("</td><td>")
                .Append(results["MAJOR_PUSH"].Energy.NetWorkoutActivityEnergyKcal).Append("</td><td>")
                .Append(weeks.Single(w => w.PlanCode == "PLAN_3").AverageDailyPrescribedEnergyKcal)
                .Append("</td></tr>");
        }
        var report = new
        {
            AuditVersion = "LIVE_CALORIE_AUDIT_1", GeneratedAtUtc = DateTime.UtcNow,
            EngineVersion = Engine30DashboardProjectionService.EngineVersionCode,
            ConfigVersion = Engine30DashboardProjectionService.ConfigVersionCode,
            ActionDate = actionDate, WeekStart = weekStart, WeeklySchedule = schedule,
            Note = "Synthetic read-only planned-session audit. No accounts, measurements, workouts, or recommendations are saved. No nutrition carryover, objective workload, detailed exercise sets, or actual resistance rest inputs. Body composition is estimated. Repeated REST days preserve recovery effects in the weekly prescription.",
            OccupationOptions = Engine30ActivityProfileCatalog.Occupations,
            LifestyleOptions = Engine30ActivityProfileCatalog.Lifestyles,
            EnergyPolicies = energyPolicies, Results = rows
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (download) return File(Encoding.UTF8.GetBytes(json), "application/json", "12WR_LIVE_Calorie_Audit.json");
        return Content("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>LIVE calorie audit</title></head><body><main>"
            + "<h1>LIVE calorie audit</h1><p>20 synthetic profiles, 8 workout conditions, 5 goals. Read-only production engine results.</p>"
            + "<a href=\"/admin/calorie-audit?download=true\">Download audit JSON</a> <a href=\"/admin/users\">User Management</a>"
            + "<table border=\"1\"><thead><tr><th>Profile</th><th>Inputs</th><th>Rest maintenance kcal/day</th><th>Push net kcal</th><th>PLAN_3 average target kcal/day</th></tr></thead><tbody>"
            + summary + "</tbody></table></main></body></html>", "text/html; charset=utf-8");
    }
}
