namespace WebRecomp.Web.Services;

/// <summary>
/// Builds projected fuel-ledger days and rolls the Engine 2.1
/// four-day fuel window forward one calendar day at a time.
///
/// This service is pure/in-memory. It does not read or write the database.
/// </summary>
public sealed record Engine21ProjectedFuelDayInput(
    DateOnly Date,
    decimal DemandG,
    decimal RecommendedCarbsG,
    decimal BaseRestCarbsG,
    decimal FuelScore);

public sealed class Engine21ProjectedFuelWindowService
{
    public Engine21FuelLedgerDay BuildProjectedDay(
        Engine21ProjectedFuelDayInput input,
        IReadOnlyDictionary<string, decimal> settings)
    {
        Validate(input);

        var backgroundPct =
            ConfigurationService.Setting(
                settings,
                "BACKGROUND_BASE_CARB_CREDIT_PCT",
                0.20m);

        var carbRounding =
            ConfigurationService.Setting(
                settings,
                "CARB_ROUNDING_G",
                5m);

        if (backgroundPct < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "BACKGROUND_BASE_CARB_CREDIT_PCT cannot be negative.");
        }

        if (carbRounding <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "CARB_ROUNDING_G must be greater than zero.");
        }

        decimal restorationCredit =
            0m;

        if (input.DemandG > 0m)
        {
            //
            // Preserve the same restoration-credit equation used
            // by the real historical Engine 2.0/2.1 fuel ledger:
            //
            // - carbohydrate above BaseRestCarbs gets full credit
            // - configured background credit applies to base carbs
            // - restoration cannot exceed modeled demand
            //
            var rawCredit =
                Math.Min(
                    input.DemandG,

                    Math.Max(
                        0m,
                        input.RecommendedCarbsG -
                        input.BaseRestCarbsG)

                    + Math.Min(
                        input.RecommendedCarbsG,
                        input.BaseRestCarbsG)
                      * backgroundPct);

            restorationCredit =
                WorkoutMathService.RoundToIncrement(
                    rawCredit,
                    carbRounding);
        }

        return new Engine21FuelLedgerDay(
            Date:
                input.Date,

            DemandG:
                input.DemandG,

            RestorationCreditG:
                restorationCredit,

            MaxFuelScore:
                input.FuelScore,

            ModerateHard:
                input.FuelScore >= 2m,

            Hard:
                input.FuelScore >= 3m,

            // This is projected prescription, not observed nutrition.
            HasCarbohydrateEvidence:
                false);
    }

    public Engine21FuelWindow RollForward(
        Engine21FuelWindow current,
        Engine21FuelLedgerDay projectedDay)
    {
        if (current.Days.Count != 4)
        {
            throw new InvalidOperationException(
                $"Projected fuel rolling requires exactly four prior days; " +
                $"received {current.Days.Count}.");
        }

        var ordered =
            current.Days
                .OrderBy(x => x.Date)
                .ToList();

        var expectedDate =
            ordered[^1].Date.AddDays(1);

        if (projectedDay.Date != expectedDate)
        {
            throw new InvalidOperationException(
                $"Projected fuel day must be {expectedDate:yyyy-MM-dd}; " +
                $"received {projectedDay.Date:yyyy-MM-dd}.");
        }

        //
        // Drop the oldest day and append the newly projected day.
        //
        var days =
            ordered
                .Skip(1)
                .Append(projectedDay)
                .ToList();

        var totalDemand =
            days.Sum(
                x => x.DemandG);

        var totalCredit =
            days.Sum(
                x => x.RestorationCreditG);

        var moderateHardDays =
            checked(
                (short)days.Count(
                    x => x.ModerateHard));

        var hardDays =
            checked(
                (short)days.Count(
                    x => x.Hard));

        var ratio =
            totalDemand <= 0m
                ? 1m
                : Math.Min(
                    1m,
                    totalCredit / totalDemand);

        return new Engine21FuelWindow(
            DemandG:
                totalDemand,

            RestorationCreditG:
                totalCredit,

            AbsoluteRestorationRatio:
                ratio,

            ModerateHardDays:
                moderateHardDays,

            HardDays:
                hardDays,

            Days:
                days);
    }

    private static void Validate(
        Engine21ProjectedFuelDayInput input)
    {
        if (input.DemandG < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.DemandG));
        }

        if (input.RecommendedCarbsG < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.RecommendedCarbsG));
        }

        if (input.BaseRestCarbsG < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.BaseRestCarbsG));
        }

        if (input.FuelScore < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.FuelScore));
        }
    }
}
