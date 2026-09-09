using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30BaseLivingCalculatorTests
{
    private readonly Engine30BaseLivingCalculator _calculator = new();
    private readonly ITestOutputHelper _output;

    public Engine30BaseLivingCalculatorTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void SyntheticPopulation_BaseLiving_IsCorrect_AndPrintsTable()
    {
        var actionDate =
            new DateOnly(2026, 9, 2);

        var cases = new[]
        {
            new TestCase(
                Number: 1,
                Age: 25,
                Sex: "MALE",
                HeightIn: 68m,
                WeightLb: 155m,
                ExpectedKcal: 1662.57m),

            new TestCase(
                Number: 2,
                Age: 35,
                Sex: "FEMALE",
                HeightIn: 64m,
                WeightLb: 135m,
                ExpectedKcal: 1292.35m),

            new TestCase(
                Number: 3,
                Age: 45,
                Sex: "MALE",
                HeightIn: 70m,
                WeightLb: 190m,
                ExpectedKcal: 1753.08m),

            new TestCase(
                Number: 4,
                Age: 55,
                Sex: "FEMALE",
                HeightIn: 66m,
                WeightLb: 175m,
                ExpectedKcal: 1405.54m),

            new TestCase(
                Number: 5,
                Age: 65,
                Sex: "MALE",
                HeightIn: 73m,
                WeightLb: 210m,
                ExpectedKcal: 1791.42m),

            new TestCase(
                Number: 6,
                Age: 30,
                Sex: "FEMALE",
                HeightIn: 69m,
                WeightLb: 155m,
                ExpectedKcal: 1487.44m),

            new TestCase(
                Number: 7,
                Age: 40,
                Sex: "MALE",
                HeightIn: 76m,
                WeightLb: 250m,
                ExpectedKcal: 2145.48m),

            new TestCase(
                Number: 8,
                Age: 60,
                Sex: "FEMALE",
                HeightIn: 62m,
                WeightLb: 190m,
                ExpectedKcal: 1385.08m),

            new TestCase(
                Number: 9,
                Age: 50,
                Sex: "MALE",
                HeightIn: 66m,
                WeightLb: 165m,
                ExpectedKcal: 1551.18m),

            new TestCase(
                Number: 10,
                Age: 70,
                Sex: "FEMALE",
                HeightIn: 67m,
                WeightLb: 145m,
                ExpectedKcal: 1210.33m)
        };

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 — BUCKET #1 BASE LIVING PLAUSIBILITY TABLE");

        _output.WriteLine(
            new string('-', 92));

        _output.WriteLine(
            $"{"#",3} " +
            $"{"Person",-18} " +
            $"{"Height",8} " +
            $"{"Weight",9} " +
            $"{"WeightKg",10} " +
            $"{"Mifflin",12} " +
            $"{"Expected",12} " +
            $"{"Delta",9}");

        _output.WriteLine(
            new string('-', 92));

        foreach (var test in cases)
        {
            var dateOfBirth =
                new DateOnly(
                    actionDate.Year - test.Age,
                    1,
                    1);

            var result =
                _calculator.Calculate(
                    new Engine30BaseLivingInput(
                        ActionDate:
                            actionDate,

                        DateOfBirth:
                            dateOfBirth,

                        SexCode:
                            test.Sex,

                        HeightIn:
                            test.HeightIn,

                        WeightLb:
                            test.WeightLb));

            var delta =
                result.BaseLivingEnergyKcal
                - test.ExpectedKcal;

            var sexDisplay =
                test.Sex == "MALE"
                    ? "M"
                    : "F";

            var heightDisplay =
                HeightDisplay(test.HeightIn);

            var personDisplay =
                $"{test.Age} {sexDisplay}";

            _output.WriteLine(
                $"{test.Number,3} " +
                $"{personDisplay,-18} " +
                $"{heightDisplay,8} " +
                $"{test.WeightLb,7:0} lb " +
                $"{result.WeightKg,10:0.00} " +
                $"{result.BaseLivingEnergyKcal,12:0.00} " +
                $"{test.ExpectedKcal,12:0.00} " +
                $"{delta,9:0.00}");

            Assert.Equal(
                test.ExpectedKcal,
                result.BaseLivingEnergyKcal);

            Assert.Equal(
                test.Age,
                result.AgeYears);

            Assert.Equal(
                "MIFFLIN_ST_JEOR_1990",
                result.FormulaCode);
        }

        _output.WriteLine(
            new string('-', 92));

        _output.WriteLine(
            "PASS: all 10 Engine 3.0 Base Living values match the reference population.");
    }

    [Fact]
    public void BaseLivingInput_ContainsNoActivityFields()
    {
        var propertyNames =
            typeof(Engine30BaseLivingInput)
                .GetProperties()
                .Select(x => x.Name)
                .ToArray();

        var prohibitedTerms =
            new[]
            {
                "Activity",
                "Step",
                "Workout",
                "Occupation",
                "MET",
                "WorkHours"
            };

        foreach (var prohibited in prohibitedTerms)
        {
            Assert.DoesNotContain(
                propertyNames,
                name =>
                    name.Contains(
                        prohibited,
                        StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Calculate_RejectsUnsupportedSexCode()
    {
        var input =
            new Engine30BaseLivingInput(
                ActionDate:
                    new DateOnly(2026, 9, 2),

                DateOfBirth:
                    new DateOnly(1980, 1, 1),

                SexCode:
                    "UNKNOWN",

                HeightIn:
                    70m,

                WeightLb:
                    180m);

        Assert.Throws<ArgumentException>(
            () => _calculator.Calculate(input));
    }

    private static string HeightDisplay(
        decimal heightIn)
    {
        var totalInches =
            (int)Math.Round(
                heightIn,
                0,
                MidpointRounding.AwayFromZero);

        var feet =
            totalInches / 12;

        var inches =
            totalInches % 12;

        return $"{feet}'{inches}\"";
    }

    private sealed record TestCase(
        int Number,
        int Age,
        string Sex,
        decimal HeightIn,
        decimal WeightLb,
        decimal ExpectedKcal);
}
