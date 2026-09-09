namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Centralized DRAFT physiological defaults used only where the daily runtime
/// has not yet received configuration-owned values.
///
/// They are deliberately explicit here so future CFG-3000.x configuration can
/// replace them without hunting through calculators.
/// </summary>
public static class Engine30DailyRuntimeDefaults
{
    public const decimal MaleSkeletalMuscleFractionOfFfm = 0.528m;
    public const decimal FemaleSkeletalMuscleFractionOfFfm = 0.473m;
    public const decimal BaselineGlycogenGPerKgMuscle = 23m;
}

/// <summary>
/// One complete Engine 3.0 daily runtime request.
///
/// Energy architecture:
///   Bucket 1 = Base Living
///   Bucket 2 = Habitual Activity
///   Bucket 3 = NET Workout Activity above rest
///
/// Glycogen reload energy is a diagnostic and is NEVER an expenditure bucket.
/// </summary>
public sealed record Engine30DailyRuntimeRequest(
    DateOnly ActionDate,
    DateOnly DateOfBirth,
    string SexCode,
    decimal HeightIn,
    decimal WeightLb,
    decimal? BodyFatFraction,
    IReadOnlyCollection<Engine30OccupationalRoleInput> OccupationalRoles,
    string LifestyleActivityCode,
    Engine30WorkoutSessionInput? WorkoutSession = null,
    decimal HomeActivityFactor = Engine30HabitualActivityCalculator.DefaultHomeActivityFactor,
    decimal? SkeletalMuscleFractionOfFfmOverride = null,
    decimal BaselineGlycogenGPerKgMuscle = Engine30DailyRuntimeDefaults.BaselineGlycogenGPerKgMuscle,
    string ResistanceTrainingStatusCode = Engine30ResistanceGlycogenTrainingStatusCodes.Trained);

public sealed record Engine30DailyEnergyComposition(
    decimal BaseLivingEnergyKcal,
    decimal HabitualActivityEnergyKcal,
    decimal NetWorkoutActivityEnergyKcal,
    decimal MaintenanceEnergyKcal,
    string FormulaCode);

public sealed record Engine30DailyGlycogenSummary(
    bool HasWorkout,
    decimal EstimatedGlycogenDepletedG,
    decimal TargetGlycogenRestorationG,
    decimal CarbohydrateReloadRequirementG,
    decimal CarbohydrateReloadEnergyKcalDiagnostic,
    bool ReloadEnergyIncludedInMaintenance);

public sealed record Engine30DailyRuntimeProvenance(
    string BaseLivingFormulaCode,
    string BodyCompositionSourceCode,
    string BodyCompositionConfidenceCode,
    string HabitualActivityModelCode,
    string SkeletalMuscleFractionSourceCode,
    decimal SkeletalMuscleFractionOfFfm,
    decimal BaselineGlycogenGPerKgMuscle,
    Engine30WorkoutRuntimeProfileProvenance? WorkoutProfile);

public sealed record Engine30DailyRuntimeResult(
    DateOnly ActionDate,
    Engine30BaseLivingResult BaseLiving,
    Engine30BodyCompositionResult BodyComposition,
    Engine30HabitualActivityResult HabitualActivity,
    Engine30WorkoutRuntimeResult? Workout,
    Engine30DailyEnergyComposition Energy,
    Engine30DailyGlycogenSummary Glycogen,
    Engine30DailyRuntimeProvenance Provenance);

/// <summary>
/// Engine 3.0 daily orchestration pipeline.
///
/// This service composes the already-tested Engine 3.0 calculators into one
/// daily physiological result. It does not prescribe a calorie deficit yet;
/// prescription policy remains a separate next-stage concern.
///
/// Invariants:
/// - Base Living contains no activity multiplier.
/// - Habitual Activity is one above-rest bucket.
/// - Workout Activity adds only net exercise energy above resting cost.
/// - Glycogen reload kcal never enter Maintenance Energy.
/// - Workout profile provenance survives into the daily result.
/// - Raw/actual workout observations are resolved by the existing workout
///   runtime service; this orchestrator does not rewrite them.
/// </summary>
public sealed class Engine30DailyRuntimeService(
    Engine30WorkoutRuntimeService workoutRuntimeService)
{
    private readonly Engine30BaseLivingCalculator _baseLiving = new();
    private readonly Engine30BodyCompositionEstimator _bodyComposition = new();
    private readonly Engine30HabitualActivityCalculator _habitual = new();

    public async Task<Engine30DailyRuntimeResult> CalculateAsync(
        Engine30DailyRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OccupationalRoles);

        ValidateRequest(request);

        var baseLiving =
            _baseLiving.Calculate(
                new Engine30BaseLivingInput(
                    ActionDate: request.ActionDate,
                    DateOfBirth: request.DateOfBirth,
                    SexCode: request.SexCode,
                    HeightIn: request.HeightIn,
                    WeightLb: request.WeightLb));

        var bodyComposition =
            _bodyComposition.Calculate(
                new Engine30BodyCompositionInput(
                    AgeYears: baseLiving.AgeYears,
                    SexCode: baseLiving.SexCode,
                    HeightIn: request.HeightIn,
                    WeightLb: request.WeightLb,
                    BodyFatFraction: request.BodyFatFraction));

        var habitual =
            _habitual.Calculate(
                baseLivingEnergyKcal: baseLiving.BaseLivingEnergyKcal,
                roles: request.OccupationalRoles,
                lifestyleActivityCode: request.LifestyleActivityCode,
                homeActivityFactor: request.HomeActivityFactor);

        var (
            skeletalMuscleFraction,
            skeletalMuscleSource)
            = ResolveSkeletalMuscleFraction(
                baseLiving.SexCode,
                request.SkeletalMuscleFractionOfFfmOverride);

        Engine30WorkoutRuntimeResult? workout = null;

        if (request.WorkoutSession is not null)
        {
            workout =
                await workoutRuntimeService.CalculateAsync(
                    new Engine30WorkoutRuntimeRequest(
                        Session: request.WorkoutSession,
                        BaseLivingEnergyKcal: baseLiving.BaseLivingEnergyKcal,
                        WeightLb: request.WeightLb,
                        FatFreeMassLb: bodyComposition.FatFreeMassLb,
                        SkeletalMuscleFractionOfFfm: skeletalMuscleFraction,
                        BaselineGlycogenGPerKgMuscle: request.BaselineGlycogenGPerKgMuscle,
                        ResistanceTrainingStatusCode: request.ResistanceTrainingStatusCode),
                    cancellationToken);
        }

        var netWorkoutEnergy =
            workout?.Energy.EngineNetWorkoutEnergyKcal
            ?? 0m;

        var maintenance =
            baseLiving.BaseLivingEnergyKcal
            + habitual.AverageDailyHabitualActivityEnergyKcal
            + netWorkoutEnergy;

        var energy =
            new Engine30DailyEnergyComposition(
                BaseLivingEnergyKcal:
                    Round(baseLiving.BaseLivingEnergyKcal),

                HabitualActivityEnergyKcal:
                    Round(habitual.AverageDailyHabitualActivityEnergyKcal),

                NetWorkoutActivityEnergyKcal:
                    Round(netWorkoutEnergy),

                MaintenanceEnergyKcal:
                    Round(maintenance),

                FormulaCode:
                    "BASE_LIVING_PLUS_HABITUAL_PLUS_NET_WORKOUT");

        var glycogen =
            workout is null
                ? new Engine30DailyGlycogenSummary(
                    HasWorkout: false,
                    EstimatedGlycogenDepletedG: 0m,
                    TargetGlycogenRestorationG: 0m,
                    CarbohydrateReloadRequirementG: 0m,
                    CarbohydrateReloadEnergyKcalDiagnostic: 0m,
                    ReloadEnergyIncludedInMaintenance: false)
                : new Engine30DailyGlycogenSummary(
                    HasWorkout: true,
                    EstimatedGlycogenDepletedG:
                        workout.Glycogen.EstimatedGlycogenDepletedG,
                    TargetGlycogenRestorationG:
                        workout.Glycogen.TargetGlycogenRestorationG,
                    CarbohydrateReloadRequirementG:
                        workout.Glycogen.CarbohydrateReloadRequirementG,
                    CarbohydrateReloadEnergyKcalDiagnostic:
                        workout.Glycogen.CarbohydrateReloadEnergyKcal,
                    ReloadEnergyIncludedInMaintenance: false);

        var provenance =
            new Engine30DailyRuntimeProvenance(
                BaseLivingFormulaCode:
                    baseLiving.FormulaCode,

                BodyCompositionSourceCode:
                    bodyComposition.BodyCompositionSourceCode,

                BodyCompositionConfidenceCode:
                    bodyComposition.ConfidenceCode,

                HabitualActivityModelCode:
                    "40H_WORK_HOME_PLUS_LIFESTYLE_PREMIUM",

                SkeletalMuscleFractionSourceCode:
                    skeletalMuscleSource,

                SkeletalMuscleFractionOfFfm:
                    skeletalMuscleFraction,

                BaselineGlycogenGPerKgMuscle:
                    request.BaselineGlycogenGPerKgMuscle,

                WorkoutProfile:
                    workout?.ProfileProvenance);

        ValidateEnergyInvariant(
            energy,
            glycogen);

        return new Engine30DailyRuntimeResult(
            ActionDate: request.ActionDate,
            BaseLiving: baseLiving,
            BodyComposition: bodyComposition,
            HabitualActivity: habitual,
            Workout: workout,
            Energy: energy,
            Glycogen: glycogen,
            Provenance: provenance);
    }

    private static (
        decimal Fraction,
        string SourceCode)
        ResolveSkeletalMuscleFraction(
            string normalizedSexCode,
            decimal? suppliedOverride)
    {
        if (suppliedOverride.HasValue)
        {
            var value = suppliedOverride.Value;

            if (value <= 0m || value > 1m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(suppliedOverride),
                    "Skeletal muscle fraction of FFM must be greater than 0 and no more than 1.");
            }

            return (
                value,
                "REQUEST_OVERRIDE");
        }

        return normalizedSexCode switch
        {
            "MALE" =>
                (
                    Engine30DailyRuntimeDefaults.MaleSkeletalMuscleFractionOfFfm,
                    "ENGINE30_SEX_DEFAULT"),

            "FEMALE" =>
                (
                    Engine30DailyRuntimeDefaults.FemaleSkeletalMuscleFractionOfFfm,
                    "ENGINE30_SEX_DEFAULT"),

            _ =>
                throw new InvalidOperationException(
                    $"Unexpected normalized sex code '{normalizedSexCode}'.")
        };
    }

    private static void ValidateRequest(
        Engine30DailyRuntimeRequest request)
    {
        if (request.ActionDate < request.DateOfBirth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.ActionDate),
                "Action date cannot be before date of birth.");
        }

        if (request.HeightIn <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.HeightIn));
        }

        if (request.WeightLb <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.WeightLb));
        }

        if (request.HomeActivityFactor < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.HomeActivityFactor));
        }

        if (request.BaselineGlycogenGPerKgMuscle <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.BaselineGlycogenGPerKgMuscle));
        }
    }

    private static void ValidateEnergyInvariant(
        Engine30DailyEnergyComposition energy,
        Engine30DailyGlycogenSummary glycogen)
    {
        var recomposed =
            Round(
                energy.BaseLivingEnergyKcal
                + energy.HabitualActivityEnergyKcal
                + energy.NetWorkoutActivityEnergyKcal);

        if (recomposed != energy.MaintenanceEnergyKcal)
        {
            throw new InvalidOperationException(
                "Engine 3.0 daily energy invariant failed: maintenance must equal Base Living + Habitual Activity + Net Workout Activity.");
        }

        if (glycogen.ReloadEnergyIncludedInMaintenance)
        {
            throw new InvalidOperationException(
                "Engine 3.0 invariant failed: glycogen reload energy may not be included in maintenance.");
        }
    }

    private static decimal Round(decimal value)
        => Math.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
}
