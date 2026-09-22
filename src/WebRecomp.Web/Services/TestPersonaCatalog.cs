namespace WebRecomp.Web.Services;

public sealed record TestPersonaSpec(
    string Email,
    string DisplayName,
    DateOnly DateOfBirth,
    string SexCode,
    decimal HeightIn,
    decimal StartingWeightLb,
    decimal StartingBodyFatFraction,
    decimal StartingSkeletalMuscleLb,
    decimal StartingWaistIn,
    decimal StartingVisceralFat,
    string GoalCode,
    string PlanCode,
    byte AvailableTrainingDaysWeek,
    string TrainingExperienceCode,
    string LifestyleActivityCode,
    int BaselineStepsDay,
    decimal BaselineSleepHours,
    decimal TargetWeeklyWeightFraction,
    decimal TargetWeeklyMuscleGainLb,
    decimal GainFatFraction,
    decimal TargetWeightLowLb,
    decimal TargetWeightHighLb,
    decimal TargetWaistIn);

/// <summary>
/// Synthetic accounts used only by the recomp-test Azure application.
/// They intentionally use non-real test.com addresses and contain no real
/// customer data. The TEST-only session service enforces the shared password.
/// </summary>
public static class TestPersonaCatalog
{
    public const string SharedPassword = "test";

    public static IReadOnlyList<TestPersonaSpec> All { get; } =
    [
        new(
            Email: "testf120@test.com",
            DisplayName: "Test F120 - Muscle Recomp",
            DateOfBirth: new DateOnly(1996, 6, 15),
            SexCode: "FEMALE",
            HeightIn: 65m,
            StartingWeightLb: 120m,
            StartingBodyFatFraction: 0.240m,
            StartingSkeletalMuscleLb: 48m,
            StartingWaistIn: 28.5m,
            StartingVisceralFat: 5m,
            GoalCode: "MUSCLE_BUILD",
            PlanCode: "PLAN_3",
            AvailableTrainingDaysWeek: 4,
            TrainingExperienceCode: "INTERMEDIATE",
            LifestyleActivityCode: "MODERATE",
            BaselineStepsDay: 8000,
            BaselineSleepHours: 7.4m,
            TargetWeeklyWeightFraction: 0.0015m,
            TargetWeeklyMuscleGainLb: 0.10m,
            GainFatFraction: 0.25m,
            TargetWeightLowLb: 123m,
            TargetWeightHighLb: 127m,
            TargetWaistIn: 28m),

        new(
            Email: "f160@test.com",
            DisplayName: "Test F160 - Fat Loss Recomp",
            DateOfBirth: new DateOnly(1976, 5, 10),
            SexCode: "FEMALE",
            HeightIn: 64m,
            StartingWeightLb: 160m,
            StartingBodyFatFraction: 0.340m,
            StartingSkeletalMuscleLb: 52m,
            StartingWaistIn: 35m,
            StartingVisceralFat: 9m,
            GoalCode: "FAT_LOSS_RECOMP",
            PlanCode: "PLAN_2",
            AvailableTrainingDaysWeek: 4,
            TrainingExperienceCode: "INTERMEDIATE",
            LifestyleActivityCode: "LIGHT",
            BaselineStepsDay: 6500,
            BaselineSleepHours: 7.0m,
            TargetWeeklyWeightFraction: -0.0050m,
            TargetWeeklyMuscleGainLb: 0.02m,
            GainFatFraction: 0.15m,
            TargetWeightLowLb: 145m,
            TargetWeightHighLb: 150m,
            TargetWaistIn: 31m),

        new(
            Email: "testf140@test.com",
            DisplayName: "Test F140 - Athlete",
            DateOfBirth: new DateOnly(2001, 3, 20),
            SexCode: "FEMALE",
            HeightIn: 66m,
            StartingWeightLb: 140m,
            StartingBodyFatFraction: 0.200m,
            StartingSkeletalMuscleLb: 56m,
            StartingWaistIn: 27m,
            StartingVisceralFat: 4m,
            GoalCode: "ATHLETE_PERFORMANCE",
            PlanCode: "PLAN_4",
            AvailableTrainingDaysWeek: 5,
            TrainingExperienceCode: "ADVANCED",
            LifestyleActivityCode: "VERY_ACTIVE",
            BaselineStepsDay: 11000,
            BaselineSleepHours: 7.8m,
            TargetWeeklyWeightFraction: 0.0000m,
            TargetWeeklyMuscleGainLb: 0.08m,
            GainFatFraction: 0.20m,
            TargetWeightLowLb: 138m,
            TargetWeightHighLb: 143m,
            TargetWaistIn: 26.5m),

        new(
            Email: "testm200@test.com",
            DisplayName: "Test M200 - Muscle Recomp",
            DateOfBirth: new DateOnly(1976, 2, 14),
            SexCode: "MALE",
            HeightIn: 71m,
            StartingWeightLb: 200m,
            StartingBodyFatFraction: 0.250m,
            StartingSkeletalMuscleLb: 82m,
            StartingWaistIn: 38m,
            StartingVisceralFat: 10m,
            GoalCode: "MUSCLE_BUILD",
            PlanCode: "PLAN_3",
            AvailableTrainingDaysWeek: 5,
            TrainingExperienceCode: "INTERMEDIATE",
            LifestyleActivityCode: "MODERATE",
            BaselineStepsDay: 7500,
            BaselineSleepHours: 7.1m,
            TargetWeeklyWeightFraction: 0.0020m,
            TargetWeeklyMuscleGainLb: 0.15m,
            GainFatFraction: 0.30m,
            TargetWeightLowLb: 204m,
            TargetWeightHighLb: 210m,
            TargetWaistIn: 37m)
    ];

    public static bool TryGet(string? email, out TestPersonaSpec? spec)
    {
        spec = null;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalized = email.Trim().ToLowerInvariant();
        spec = All.FirstOrDefault(x =>
            string.Equals(x.Email, normalized, StringComparison.OrdinalIgnoreCase));
        return spec is not null;
    }

    public static string ExternalSubject(string email)
        => $"test-persona:{email.Trim().ToLowerInvariant()}";
}
