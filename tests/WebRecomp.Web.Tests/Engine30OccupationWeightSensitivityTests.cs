using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30OccupationWeightSensitivityTests
{
    private readonly Engine30BaseLivingCalculator _baseLiving = new();
    private readonly Engine30HabitualActivityCalculator _activity = new();
    private readonly ITestOutputHelper _output;

    public Engine30OccupationWeightSensitivityTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ConstructionVsPartTimeOffice_AcrossBodySizes_PrintsTable()
    {
        var actionDate =
            new DateOnly(2026, 9, 2);

        /*
            Five matched physical profiles.

            Each profile is evaluated twice:
              1. Construction: 40 hr/week, EOAF 1.15
              2. Part-time office: 15 hr/week, EOAF 0.35

            This isolates occupation exposure from body-size effects.
        */
        var people = new[]
        {
            new Person(
                Pair: "A",
                Age: 35,
                Sex: "FEMALE",
                HeightIn: 64m,
                WeightLb: 120m),

            new Person(
                Pair: "B",
                Age: 30,
                Sex: "MALE",
                HeightIn: 68m,
                WeightLb: 150m),

            new Person(
                Pair: "C",
                Age: 50,
                Sex: "FEMALE",
                HeightIn: 66m,
                WeightLb: 180m),

            new Person(
                Pair: "D",
                Age: 65,
                Sex: "MALE",
                HeightIn: 73m,
                WeightLb: 210m),

            new Person(
                Pair: "E",
                Age: 40,
                Sex: "MALE",
                HeightIn: 76m,
                WeightLb: 250m)
        };

        var occupations = new[]
        {
            new OccupationScenario(
                Name: "Construction",
                Code: "HEAVY_PHYSICAL",
                HoursPerWeek: 40m,
                Eoaf: 1.15m),

            new OccupationScenario(
                Name: "Part-Time Office",
                Code: "SEDENTARY_LIGHT",
                HoursPerWeek: 15m,
                Eoaf: 0.35m)
        };

        var results =
            new List<Row>();

        foreach (var person in people)
        {
            var dateOfBirth =
                new DateOnly(
                    actionDate.Year - person.Age,
                    1,
                    1);

            var baseResult =
                _baseLiving.Calculate(
                    new Engine30BaseLivingInput(
                        ActionDate:
                            actionDate,

                        DateOfBirth:
                            dateOfBirth,

                        SexCode:
                            person.Sex,

                        HeightIn:
                            person.HeightIn,

                        WeightLb:
                            person.WeightLb));

            foreach (var occupation in occupations)
            {
                var activityResult =
                    _activity.Calculate(
                        baseResult.BaseLivingEnergyKcal,
                        new[]
                        {
                            new Engine30OccupationalRoleInput(
                                OccupationName:
                                    occupation.Name,

                                OccupationActivityCode:
                                    occupation.Code,

                                TypicalHoursPerWeek:
                                    occupation.HoursPerWeek,

                                EffectiveOccupationalActivityFactor:
                                    occupation.Eoaf)
                        });

                results.Add(
                    new Row(
                        Pair:
                            person.Pair,

                        Person:
                            PersonDisplay(person),

                        WeightLb:
                            person.WeightLb,

                        Occupation:
                            occupation.Name,

                        HoursPerWeek:
                            occupation.HoursPerWeek,

                        Eoaf:
                            occupation.Eoaf,

                        BaseLiving:
                            baseResult.BaseLivingEnergyKcal,

                        RestingPerHour:
                            activityResult.RestingEnergyPerHourKcal,

                        OccupationalPerWeek:
                            activityResult.WeeklyHabitualActivityEnergyKcal,

                        OccupationalPerDay:
                            activityResult.AverageDailyHabitualActivityEnergyKcal,

                        BasePlusOccupation:
                            activityResult.BasePlusHabitualEnergyKcal));
            }
        }

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 — OCCUPATION × BODY SIZE SENSITIVITY TEST");

        _output.WriteLine(
            "Five matched people: Construction 40h vs Part-Time Office 15h");

        _output.WriteLine(
            "This test evaluates OCCUPATIONAL activity only.");

        _output.WriteLine(
            "Non-work/home activity is deliberately not included yet.");

        _output.WriteLine("");

        _output.WriteLine(
            new string('-', 137));

        _output.WriteLine(
            $"{"Pair",-5}" +
            $"{"Person",-13}" +
            $"{"Wt",7}" +
            $"{"Occupation",-19}" +
            $"{"Hr/Wk",8}" +
            $"{"EOAF",7}" +
            $"{"Base",11}" +
            $"{"Rest/Hr",10}" +
            $"{"Work/Wk",11}" +
            $"{"Work/Day",11}" +
            $"{"Base+Work",12}");

        _output.WriteLine(
            new string('-', 137));

        foreach (var row in results)
        {
            _output.WriteLine(
                $"{row.Pair,-5}" +
                $"{row.Person,-13}" +
                $"{row.WeightLb,5:0}lb " +
                $"{row.Occupation,-19}" +
                $"{row.HoursPerWeek,8:0.0}" +
                $"{row.Eoaf,7:0.00}" +
                $"{row.BaseLiving,11:0.00}" +
                $"{row.RestingPerHour,10:0.00}" +
                $"{row.OccupationalPerWeek,11:0.00}" +
                $"{row.OccupationalPerDay,11:0.00}" +
                $"{row.BasePlusOccupation,12:0.00}");
        }

        _output.WriteLine(
            new string('-', 137));

        _output.WriteLine("");
        _output.WriteLine(
            "MATCHED-PAIR DIFFERENCES");

        _output.WriteLine(
            new string('-', 94));

        _output.WriteLine(
            $"{"Pair",-5}" +
            $"{"Person",-13}" +
            $"{"Wt",7}" +
            $"{"Construction/day",19}" +
            $"{"Office/day",15}" +
            $"{"Difference",15}");

        _output.WriteLine(
            new string('-', 94));

        foreach (var person in people)
        {
            var construction =
                results.Single(
                    x =>
                        x.Pair == person.Pair
                        && x.Occupation == "Construction");

            var office =
                results.Single(
                    x =>
                        x.Pair == person.Pair
                        && x.Occupation == "Part-Time Office");

            var difference =
                construction.OccupationalPerDay
                - office.OccupationalPerDay;

            _output.WriteLine(
                $"{person.Pair,-5}" +
                $"{PersonDisplay(person),-13}" +
                $"{person.WeightLb,5:0}lb " +
                $"{construction.OccupationalPerDay,19:0.00}" +
                $"{office.OccupationalPerDay,15:0.00}" +
                $"{difference,15:0.00}");

            Assert.True(
                construction.OccupationalPerDay >
                office.OccupationalPerDay);

            Assert.True(
                construction.BasePlusOccupation >
                office.BasePlusOccupation);
        }

        _output.WriteLine(
            new string('-', 94));

        /*
            Scaling invariant:
            the same occupation exposure should generate more
            absolute activity energy as resting expenditure rises.
        */
        var constructionRows =
            results
                .Where(x => x.Occupation == "Construction")
                .OrderBy(x => x.BaseLiving)
                .ToArray();

        for (var i = 1; i < constructionRows.Length; i++)
        {
            Assert.True(
                constructionRows[i].OccupationalPerDay >
                constructionRows[i - 1].OccupationalPerDay);
        }

        _output.WriteLine("");
        _output.WriteLine(
            "PASS: occupation/body-size sensitivity calculations completed.");
    }

    private static string PersonDisplay(
        Person person)
    {
        var sex =
            person.Sex == "MALE"
                ? "M"
                : "F";

        return $"{person.Age}{sex} {HeightDisplay(person.HeightIn)}";
    }

    private static string HeightDisplay(
        decimal heightIn)
    {
        var totalInches =
            (int)Math.Round(
                heightIn,
                0,
                MidpointRounding.AwayFromZero);

        return
            $"{totalInches / 12}'{totalInches % 12}\"";
    }

    private sealed record Person(
        string Pair,
        int Age,
        string Sex,
        decimal HeightIn,
        decimal WeightLb);

    private sealed record OccupationScenario(
        string Name,
        string Code,
        decimal HoursPerWeek,
        decimal Eoaf);

    private sealed record Row(
        string Pair,
        string Person,
        decimal WeightLb,
        string Occupation,
        decimal HoursPerWeek,
        decimal Eoaf,
        decimal BaseLiving,
        decimal RestingPerHour,
        decimal OccupationalPerWeek,
        decimal OccupationalPerDay,
        decimal BasePlusOccupation);
}
