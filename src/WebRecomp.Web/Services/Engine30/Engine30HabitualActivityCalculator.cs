namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// One normal occupational role contributing to Engine 3.0
/// Bucket #2 habitual activity.
/// </summary>
public sealed record Engine30OccupationalRoleInput(
    string OccupationName,
    string OccupationActivityCode,
    decimal TypicalHoursPerWeek,
    decimal EffectiveOccupationalActivityFactor);

public sealed record Engine30OccupationalRoleResult(
    string OccupationName,
    string OccupationActivityCode,
    decimal TypicalHoursPerWeek,
    decimal EffectiveOccupationalActivityFactor,
    decimal WeeklyActivityEnergyKcal);

/// <summary>
/// Engine 3.0 Bucket #2.
///
/// Habitual activity contains:
///
/// 1. Occupational activity
/// 2. Home-equivalent activity filling unused portions of the
///    standard 40-hour habitual activity window
/// 3. A recurring lifestyle activity premium for activity outside
///    work and outside separately reported workouts
///
/// All three remain components of ONE habitual-activity bucket.
/// </summary>
public sealed record Engine30HabitualActivityResult(
    decimal BaseLivingEnergyKcal,
    decimal RestingEnergyPerHourKcal,

    decimal TotalOccupationalHoursPerWeek,
    decimal HomeEquivalentHoursPerWeek,

    string LifestyleActivityCode,
    decimal LifestyleEquivalentHoursPerWeek,

    decimal WeeklyOccupationalActivityEnergyKcal,
    decimal WeeklyHomeActivityEnergyKcal,
    decimal WeeklyLifestyleActivityEnergyKcal,
    decimal WeeklyHabitualActivityEnergyKcal,

    decimal AverageDailyOccupationalActivityEnergyKcal,
    decimal AverageDailyHomeActivityEnergyKcal,
    decimal AverageDailyLifestyleActivityEnergyKcal,
    decimal AverageDailyHabitualActivityEnergyKcal,

    decimal BasePlusHabitualEnergyKcal,

    decimal StandardHabitualHoursPerWeek,
    decimal HomeActivityFactor,

    IReadOnlyList<Engine30OccupationalRoleResult> Roles);

public sealed class Engine30HabitualActivityCalculator
{
    public const decimal StandardHabitualHoursPerWeek = 40m;

    /*
        DRAFT Engine 3.0 engineering coefficient.

        Represents blended normal home/non-work activity such as
        cooking, cleaning, errands, standing, household movement,
        gardening, and similar ordinary activity.

        It does NOT mean continuous housecleaning.
    */
    public const decimal DefaultHomeActivityFactor = 0.60m;

    private const decimal HoursPerDay = 24m;
    private const decimal DaysPerWeek = 7m;

    /// <summary>
    /// Backward-compatible calculation for tests/code that have not
    /// supplied the new lifestyle classification yet.
    ///
    /// This behaves as MINIMAL lifestyle activity.
    /// Engine 3.0 production wiring should use the overload that
    /// explicitly supplies LifestyleActivityCode.
    /// </summary>
    public Engine30HabitualActivityResult Calculate(
        decimal baseLivingEnergyKcal,
        IReadOnlyCollection<Engine30OccupationalRoleInput> roles,
        decimal homeActivityFactor = DefaultHomeActivityFactor)
    {
        return CalculateCore(
            baseLivingEnergyKcal,
            roles,
            lifestyleActivityCode: "MINIMAL",
            lifestyleEquivalentHoursPerWeek: 0m,
            homeActivityFactor);
    }

    /// <summary>
    /// Engine 3.0 habitual activity calculation using the client's
    /// recurring lifestyle activity classification.
    ///
    /// Candidate Engine 3.0 equivalent-hour policy:
    ///
    /// MINIMAL      = 0 hours/week
    /// LIGHT        = 4 hours/week
    /// MODERATE     = 8 hours/week
    /// VERY_ACTIVE  = 16 hours/week
    ///
    /// These are Engine coefficients, not literal claims that every
    /// user performed exactly that number of activity hours.
    /// They will ultimately be configuration owned by CFG-3000.0.
    /// </summary>
    public Engine30HabitualActivityResult Calculate(
        decimal baseLivingEnergyKcal,
        IReadOnlyCollection<Engine30OccupationalRoleInput> roles,
        string lifestyleActivityCode,
        decimal homeActivityFactor = DefaultHomeActivityFactor)
    {
        var lifestyle =
            ResolveLifestyleActivity(
                lifestyleActivityCode);

        return CalculateCore(
            baseLivingEnergyKcal,
            roles,
            lifestyle.Code,
            lifestyle.EquivalentHoursPerWeek,
            homeActivityFactor);
    }

    private static Engine30HabitualActivityResult CalculateCore(
        decimal baseLivingEnergyKcal,
        IReadOnlyCollection<Engine30OccupationalRoleInput> roles,
        string lifestyleActivityCode,
        decimal lifestyleEquivalentHoursPerWeek,
        decimal homeActivityFactor)
    {
        if (baseLivingEnergyKcal <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseLivingEnergyKcal),
                "Base living energy must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(roles);

        if (homeActivityFactor < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(homeActivityFactor),
                "Home activity factor cannot be negative.");
        }

        if (lifestyleEquivalentHoursPerWeek < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifestyleEquivalentHoursPerWeek),
                "Lifestyle equivalent hours cannot be negative.");
        }

        var restingEnergyPerHour =
            baseLivingEnergyKcal / HoursPerDay;

        var roleResults =
            new List<Engine30OccupationalRoleResult>();

        decimal totalWorkHours = 0m;
        decimal weeklyOccupationalActivity = 0m;

        foreach (var role in roles)
        {
            ValidateRole(role);

            var weeklyRoleActivity =
                restingEnergyPerHour
                * role.EffectiveOccupationalActivityFactor
                * role.TypicalHoursPerWeek;

            totalWorkHours +=
                role.TypicalHoursPerWeek;

            weeklyOccupationalActivity +=
                weeklyRoleActivity;

            roleResults.Add(
                new Engine30OccupationalRoleResult(
                    OccupationName:
                        role.OccupationName.Trim(),

                    OccupationActivityCode:
                        role.OccupationActivityCode
                            .Trim()
                            .ToUpperInvariant(),

                    TypicalHoursPerWeek:
                        Round(role.TypicalHoursPerWeek),

                    EffectiveOccupationalActivityFactor:
                        role.EffectiveOccupationalActivityFactor,

                    WeeklyActivityEnergyKcal:
                        Round(weeklyRoleActivity)));
        }

        if (totalWorkHours > 168m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roles),
                "Combined occupational hours cannot exceed 168 hours per week.");
        }

        /*
            Actual work fills the standard 40-hour habitual window.

            If work is less than 40 hours, the remaining portion is
            modeled as ordinary home/non-work activity.

            If work exceeds 40 hours, all actual work is still counted
            and home-equivalent hours become zero.
        */
        var homeEquivalentHours =
            Math.Max(
                0m,
                StandardHabitualHoursPerWeek
                - totalWorkHours);

        var weeklyHomeActivity =
            restingEnergyPerHour
            * homeActivityFactor
            * homeEquivalentHours;

        /*
            Lifestyle premium represents recurring activity ABOVE the
            baseline work/home model and outside separately reported
            workouts.

            Examples:
            - extra walking
            - gardening
            - yardwork
            - active household projects
            - recreational activity not logged as a workout

            It is deliberately NOT taken away from the 40-hour window.
            It represents additional recurring activity.
        */
        var weeklyLifestyleActivity =
            restingEnergyPerHour
            * homeActivityFactor
            * lifestyleEquivalentHoursPerWeek;

        var weeklyHabitualActivity =
            weeklyOccupationalActivity
            + weeklyHomeActivity
            + weeklyLifestyleActivity;

        var dailyOccupationalActivity =
            weeklyOccupationalActivity
            / DaysPerWeek;

        var dailyHomeActivity =
            weeklyHomeActivity
            / DaysPerWeek;

        var dailyLifestyleActivity =
            weeklyLifestyleActivity
            / DaysPerWeek;

        var dailyHabitualActivity =
            weeklyHabitualActivity
            / DaysPerWeek;

        var basePlusHabitual =
            baseLivingEnergyKcal
            + dailyHabitualActivity;

        return new Engine30HabitualActivityResult(
            BaseLivingEnergyKcal:
                Round(baseLivingEnergyKcal),

            RestingEnergyPerHourKcal:
                Round(restingEnergyPerHour),

            TotalOccupationalHoursPerWeek:
                Round(totalWorkHours),

            HomeEquivalentHoursPerWeek:
                Round(homeEquivalentHours),

            LifestyleActivityCode:
                lifestyleActivityCode,

            LifestyleEquivalentHoursPerWeek:
                Round(lifestyleEquivalentHoursPerWeek),

            WeeklyOccupationalActivityEnergyKcal:
                Round(weeklyOccupationalActivity),

            WeeklyHomeActivityEnergyKcal:
                Round(weeklyHomeActivity),

            WeeklyLifestyleActivityEnergyKcal:
                Round(weeklyLifestyleActivity),

            WeeklyHabitualActivityEnergyKcal:
                Round(weeklyHabitualActivity),

            AverageDailyOccupationalActivityEnergyKcal:
                Round(dailyOccupationalActivity),

            AverageDailyHomeActivityEnergyKcal:
                Round(dailyHomeActivity),

            AverageDailyLifestyleActivityEnergyKcal:
                Round(dailyLifestyleActivity),

            AverageDailyHabitualActivityEnergyKcal:
                Round(dailyHabitualActivity),

            BasePlusHabitualEnergyKcal:
                Round(basePlusHabitual),

            StandardHabitualHoursPerWeek:
                StandardHabitualHoursPerWeek,

            HomeActivityFactor:
                homeActivityFactor,

            Roles:
                roleResults);
    }

    private static (
        string Code,
        decimal EquivalentHoursPerWeek)
        ResolveLifestyleActivity(
            string lifestyleActivityCode)
    {
        if (string.IsNullOrWhiteSpace(
            lifestyleActivityCode))
        {
            throw new ArgumentException(
                "Lifestyle activity classification is required.",
                nameof(lifestyleActivityCode));
        }

        var normalized =
            lifestyleActivityCode
                .Trim()
                .ToUpperInvariant();

        return normalized switch
        {
            "MINIMAL" =>
                ("MINIMAL", 0m),

            "LIGHT" =>
                ("LIGHT", 4m),

            "MODERATE" =>
                ("MODERATE", 8m),

            "VERY_ACTIVE" =>
                ("VERY_ACTIVE", 16m),

            _ =>
                throw new ArgumentException(
                    $"Unsupported lifestyle activity code " +
                    $"'{lifestyleActivityCode}'.",
                    nameof(lifestyleActivityCode))
        };
    }

    private static void ValidateRole(
        Engine30OccupationalRoleInput role)
    {
        if (string.IsNullOrWhiteSpace(
            role.OccupationName))
        {
            throw new ArgumentException(
                "Occupation name is required.");
        }

        if (string.IsNullOrWhiteSpace(
            role.OccupationActivityCode))
        {
            throw new ArgumentException(
                "Occupation activity code is required.");
        }

        if (role.TypicalHoursPerWeek < 0m ||
            role.TypicalHoursPerWeek > 168m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(role.TypicalHoursPerWeek),
                "Occupational hours must be between 0 and 168.");
        }

        if (role.EffectiveOccupationalActivityFactor < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(role.EffectiveOccupationalActivityFactor),
                "Occupational activity factor cannot be negative.");
        }
    }

    private static decimal Round(
        decimal value)
        => Math.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
}
