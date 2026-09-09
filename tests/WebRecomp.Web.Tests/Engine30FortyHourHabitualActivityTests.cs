using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30FortyHourHabitualActivityTests
{
    private readonly Engine30BaseLivingCalculator _baseLiving = new();
    private readonly ITestOutputHelper _output;

    private const decimal StandardHabitualHoursPerWeek = 40m;

    // DRAFT candidate only.
    // Represents a blended home/non-work activity equivalent:
    // cooking, cleaning, errands, standing, household movement, etc.
    private const decimal HomeLivingEoaf = 0.60m;

    public Engine30FortyHourHabitualActivityTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FortyHourActivityWindow_PrintsSensitivityTable()
    {
        var actionDate =
            new DateOnly(2026, 9, 2);

        var people = new[]
        {
            new Person("A", 35, "FEMALE", 64m, 120m),
            new Person("B", 30, "MALE",   68m, 150m),
            new Person("C", 50, "FEMALE", 66m, 180m),
            new Person("D", 65, "MALE",   73m, 210m),
            new Person("E", 40, "MALE",   76m, 250m)
        };

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 — 40-HOUR HABITUAL ACTIVITY MODEL");

        _output.WriteLine(
            "Home factor = 0.60 DRAFT");

        _output.WriteLine(
            "Construction = 40 work hours @ 1.15 EOAF");

        _output.WriteLine(
            "Part-time office = 15 work hours @ 0.35 EOAF + 25 home-equivalent hours");

        _output.WriteLine(
            "No work = 40 home-equivalent hours");

        _output.WriteLine("");

        _output.WriteLine(
            new string('-', 142));

        _output.WriteLine(
            $"{"Pair",-5}" +
            $"{"Person",-13}" +
            $"{"Wt",7}" +
            $"{"Base",10}" +
            $"{"Constr/Day",13}" +
            $"{"Office/Day",12}" +
            $"{"Home25/Day",13}" +
            $"{"Office+Home",14}" +
            $"{"NoWork Home",14}" +
            $"{"Office Total",14}");

        _output.WriteLine(
            new string('-', 142));

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

            var restingPerHour =
                baseResult.BaseLivingEnergyKcal / 24m;

            // ----------------------------------------------
            // Scenario 1:
            // Construction, 40 hours/week
            // ----------------------------------------------

            var constructionWeekly =
                restingPerHour
                * 1.15m
                * 40m;

            var constructionDaily =
                constructionWeekly / 7m;

            // ----------------------------------------------
            // Scenario 2:
            // Part-time office, 15 hours/week
            // ----------------------------------------------

            const decimal officeHours = 15m;
            const decimal officeEoaf = 0.35m;

            var homeHoursForOffice =
                Math.Max(
                    0m,
                    StandardHabitualHoursPerWeek
                    - officeHours);

            var officeWeekly =
                restingPerHour
                * officeEoaf
                * officeHours;

            var officeDaily =
                officeWeekly / 7m;

            var homeForOfficeWeekly =
                restingPerHour
                * HomeLivingEoaf
                * homeHoursForOffice;

            var homeForOfficeDaily =
                homeForOfficeWeekly / 7m;

            var officePlusHomeDaily =
                officeDaily
                + homeForOfficeDaily;

            // ----------------------------------------------
            // Scenario 3:
            // No current employment
            // ----------------------------------------------

            var noWorkHomeWeekly =
                restingPerHour
                * HomeLivingEoaf
                * StandardHabitualHoursPerWeek;

            var noWorkHomeDaily =
                noWorkHomeWeekly / 7m;

            var officeTotal =
                baseResult.BaseLivingEnergyKcal
                + officePlusHomeDaily;

            _output.WriteLine(
                $"{person.Pair,-5}" +
                $"{PersonDisplay(person),-13}" +
                $"{person.WeightLb,5:0}lb " +
                $"{baseResult.BaseLivingEnergyKcal,10:0.00}" +
                $"{constructionDaily,13:0.00}" +
                $"{officeDaily,12:0.00}" +
                $"{homeForOfficeDaily,13:0.00}" +
                $"{officePlusHomeDaily,14:0.00}" +
                $"{noWorkHomeDaily,14:0.00}" +
                $"{officeTotal,14:0.00}");

            Assert.True(constructionDaily > 0m);
            Assert.True(officePlusHomeDaily > officeDaily);
            Assert.True(noWorkHomeDaily > 0m);

            // Since office work replaces some home-equivalent hours
            // with a LOWER factor (.35 vs .60), this particular
            // scenario should have slightly less habitual energy
            // than the no-work/home scenario.
            Assert.True(
                noWorkHomeDaily >
                officePlusHomeDaily);
        }

        _output.WriteLine(
            new string('-', 142));

        _output.WriteLine("");
        _output.WriteLine(
            "CONSTRUCTION FACTOR SENSITIVITY");

        _output.WriteLine(
            new string('-', 94));

        _output.WriteLine(
            $"{"Person",-13}" +
            $"{"Wt",7}" +
            $"{"EOAF .90",13}" +
            $"{"EOAF 1.00",13}" +
            $"{"EOAF 1.15",13}" +
            $"{"NoWork/Home",15}");

        _output.WriteLine(
            new string('-', 94));

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
                        actionDate,
                        dateOfBirth,
                        person.Sex,
                        person.HeightIn,
                        person.WeightLb));

            var restingPerHour =
                baseResult.BaseLivingEnergyKcal / 24m;

            decimal ActivityPerDay(
                decimal factor,
                decimal hours)
                =>
                    restingPerHour
                    * factor
                    * hours
                    / 7m;

            var construction90 =
                ActivityPerDay(0.90m, 40m);

            var construction100 =
                ActivityPerDay(1.00m, 40m);

            var construction115 =
                ActivityPerDay(1.15m, 40m);

            var noWorkHome =
                ActivityPerDay(
                    HomeLivingEoaf,
                    40m);

            _output.WriteLine(
                $"{PersonDisplay(person),-13}" +
                $"{person.WeightLb,5:0}lb " +
                $"{construction90,13:0.00}" +
                $"{construction100,13:0.00}" +
                $"{construction115,13:0.00}" +
                $"{noWorkHome,15:0.00}");
        }

        _output.WriteLine(
            new string('-', 94));

        _output.WriteLine("");
        _output.WriteLine(
            "TEST ONLY — no Engine 3.0 production factors changed.");
    }

    private static string PersonDisplay(
        Person person)
    {
        var sex =
            person.Sex == "MALE"
                ? "M"
                : "F";

        var inches =
            (int)person.HeightIn;

        return
            $"{person.Age}{sex} " +
            $"{inches / 12}'{inches % 12}\"";
    }

    private sealed record Person(
        string Pair,
        int Age,
        string Sex,
        decimal HeightIn,
        decimal WeightLb);
}
