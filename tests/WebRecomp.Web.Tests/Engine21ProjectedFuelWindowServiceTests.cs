using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21ProjectedFuelWindowServiceTests
{
    private readonly Engine21ProjectedFuelWindowService _service =
        new();

    private static readonly IReadOnlyDictionary<string, decimal> Settings =
        new Dictionary<string, decimal>
        {
            ["BACKGROUND_BASE_CARB_CREDIT_PCT"] = 0.20m,
            ["CARB_ROUNDING_G"] = 5m
        };

    [Fact]
    public void Projected_day_uses_same_restoration_credit_equation()
    {
        var result =
            _service.BuildProjectedDay(
                new Engine21ProjectedFuelDayInput(
                    Date:
                        new DateOnly(2026, 9, 1),

                    DemandG:
                        80m,

                    RecommendedCarbsG:
                        190m,

                    BaseRestCarbsG:
                        130m,

                    FuelScore:
                        3m),
                Settings);

        //
        // 190 - 130 = 60 g full restoration credit.
        // 130 * 20% = 26 g background credit.
        //
        // Raw credit = 86 g, but restoration cannot exceed
        // the day's modeled 80 g demand.
        //
        Assert.Equal(
            80m,
            result.RestorationCreditG);

        Assert.True(
            result.ModerateHard);

        Assert.True(
            result.Hard);

        Assert.False(
            result.HasCarbohydrateEvidence);
    }

    [Fact]
    public void Projected_low_carbohydrate_day_receives_only_background_credit()
    {
        var result =
            _service.BuildProjectedDay(
                new Engine21ProjectedFuelDayInput(
                    Date:
                        new DateOnly(2026, 9, 1),

                    DemandG:
                        80m,

                    RecommendedCarbsG:
                        120m,

                    BaseRestCarbsG:
                        130m,

                    FuelScore:
                        1m),
                Settings);

        //
        // No carbs exist above BaseRestCarbs.
        //
        // 120 * 20% = 24 g
        // rounded to nearest 5 g = 25 g.
        //
        Assert.Equal(
            25m,
            result.RestorationCreditG);

        Assert.False(
            result.ModerateHard);

        Assert.False(
            result.Hard);
    }

    [Fact]
    public void Roll_forward_drops_oldest_day_and_recalculates_window()
    {
        var current =
            new Engine21FuelWindow(
                DemandG:
                    100m,

                RestorationCreditG:
                    55m,

                AbsoluteRestorationRatio:
                    0.55m,

                ModerateHardDays:
                    3,

                HardDays:
                    2,

                Days:
                    new[]
                    {
                        Day(
                            new DateOnly(2026, 8, 28),
                            demand: 10m,
                            credit: 10m,
                            fuelScore: 1m),

                        Day(
                            new DateOnly(2026, 8, 29),
                            demand: 20m,
                            credit: 10m,
                            fuelScore: 2m),

                        Day(
                            new DateOnly(2026, 8, 30),
                            demand: 30m,
                            credit: 15m,
                            fuelScore: 3m),

                        Day(
                            new DateOnly(2026, 8, 31),
                            demand: 40m,
                            credit: 20m,
                            fuelScore: 4m)
                    });

        var projected =
            Day(
                new DateOnly(2026, 9, 1),
                demand: 50m,
                credit: 25m,
                fuelScore: 2.5m);

        var result =
            _service.RollForward(
                current,
                projected);

        Assert.Equal(
            4,
            result.Days.Count);

        Assert.DoesNotContain(
            result.Days,
            x => x.Date == new DateOnly(2026, 8, 28));

        Assert.Equal(
            new DateOnly(2026, 8, 29),
            result.Days[0].Date);

        Assert.Equal(
            new DateOnly(2026, 9, 1),
            result.Days[^1].Date);

        //
        // Remaining demand:
        // 20 + 30 + 40 + 50 = 140
        //
        Assert.Equal(
            140m,
            result.DemandG);

        //
        // Remaining restoration:
        // 10 + 15 + 20 + 25 = 70
        //
        Assert.Equal(
            70m,
            result.RestorationCreditG);

        Assert.Equal(
            0.50m,
            result.AbsoluteRestorationRatio);

        Assert.Equal(
            4,
            result.ModerateHardDays);

        Assert.Equal(
            2,
            result.HardDays);
    }

    private static Engine21FuelLedgerDay Day(
        DateOnly date,
        decimal demand,
        decimal credit,
        decimal fuelScore)
    {
        return new Engine21FuelLedgerDay(
            Date:
                date,

            DemandG:
                demand,

            RestorationCreditG:
                credit,

            MaxFuelScore:
                fuelScore,

            ModerateHard:
                fuelScore >= 2m,

            Hard:
                fuelScore >= 3m,

            HasCarbohydrateEvidence:
                false);
    }
}
