using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30BodyCompositionEstimatorTests
{
    private readonly Engine30BodyCompositionEstimator _estimator = new();
    private readonly ITestOutputHelper _output;

    public Engine30BodyCompositionEstimatorTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TenPersonPopulation_PrintsFatFreeMassTable()
    {
        var cases = new[]
        {
            new TestCase(
                1, 25, "MALE", 68m, 155m, null,
                122.64m, 55.63m, 0.7912m,
                "ANTHROPOMETRIC_ESTIMATE", "MEDIUM"),

            new TestCase(
                2, 35, "FEMALE", 64m, 135m, null,
                88.73m, 40.24m, 0.6572m,
                "ANTHROPOMETRIC_ESTIMATE", "MEDIUM"),

            new TestCase(
                3, 45, "MALE", 70m, 190m, 0.22m,
                148.20m, 67.22m, 0.7800m,
                "BODY_FAT_DERIVED", "MEDIUM"),

            new TestCase(
                4, 55, "FEMALE", 66m, 175m, 0.32m,
                119.00m, 53.98m, 0.6800m,
                "BODY_FAT_DERIVED", "MEDIUM"),

            new TestCase(
                5, 65, "MALE", 73m, 210m, 0.25m,
                157.50m, 71.44m, 0.7500m,
                "BODY_FAT_DERIVED", "MEDIUM"),

            new TestCase(
                6, 30, "FEMALE", 69m, 155m, null,
                109.95m, 49.87m, 0.7094m,
                "ANTHROPOMETRIC_ESTIMATE", "MEDIUM"),

            new TestCase(
                7, 40, "MALE", 76m, 250m, 0.30m,
                175.00m, 79.38m, 0.7000m,
                "BODY_FAT_DERIVED", "MEDIUM"),

            new TestCase(
                8, 60, "FEMALE", 62m, 190m, 0.38m,
                117.80m, 53.43m, 0.6200m,
                "BODY_FAT_DERIVED", "MEDIUM"),

            new TestCase(
                9, 50, "MALE", 66m, 165m, null,
                120.70m, 54.75m, 0.7315m,
                "ANTHROPOMETRIC_ESTIMATE", "MEDIUM"),

            new TestCase(
                10, 70, "FEMALE", 67m, 145m, null,
                103.42m, 46.91m, 0.7132m,
                "ANTHROPOMETRIC_EXTRAPOLATED", "LOW")
        };

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 — FAT-FREE MASS PLAUSIBILITY TABLE");

        _output.WriteLine(
            "Body-fat-derived FFM is used when BF% exists; otherwise anthropometric fallback.");

        _output.WriteLine("");

        _output.WriteLine(
            new string('-', 142));

        _output.WriteLine(
            $"{"#",2} " +
            $"{"Person",-8} " +
            $"{"Height",8} " +
            $"{"Weight",9} " +
            $"{"BF%",7} " +
            $"{"FFM lb",9} " +
            $"{"FFM kg",9} " +
            $"{"FFM%",8} " +
            $"{"Source",-29} " +
            $"{"Confidence",-10}");

        _output.WriteLine(
            new string('-', 142));

        foreach (var test in cases)
        {
            var result =
                _estimator.Calculate(
                    new Engine30BodyCompositionInput(
                        AgeYears:
                            test.Age,

                        SexCode:
                            test.Sex,

                        HeightIn:
                            test.HeightIn,

                        WeightLb:
                            test.WeightLb,

                        BodyFatFraction:
                            test.BodyFatFraction));

            var sex =
                test.Sex == "MALE"
                    ? "M"
                    : "F";

            var bfDisplay =
                result.BodyFatPercent.HasValue
                    ? $"{result.BodyFatPercent.Value:0.0}"
                    : "N/A";

            _output.WriteLine(
                $"{test.Number,2} " +
                $"{test.Age + sex,-8} " +
                $"{HeightDisplay(test.HeightIn),8} " +
                $"{test.WeightLb,7:0}lb " +
                $"{bfDisplay,7} " +
                $"{result.FatFreeMassLb,9:0.00} " +
                $"{result.FatFreeMassKg,9:0.00} " +
                $"{result.FatFreeMassFraction * 100m,7:0.0}% " +
                $"{result.BodyCompositionSourceCode,-29} " +
                $"{result.ConfidenceCode,-10}");

            Assert.Equal(
                test.ExpectedFfmLb,
                result.FatFreeMassLb);

            Assert.Equal(
                test.ExpectedFfmKg,
                result.FatFreeMassKg);

            Assert.Equal(
                test.ExpectedFfmFraction,
                result.FatFreeMassFraction);

            Assert.Equal(
                test.ExpectedSource,
                result.BodyCompositionSourceCode);

            Assert.Equal(
                test.ExpectedConfidence,
                result.ConfidenceCode);
        }

        _output.WriteLine(
            new string('-', 142));

        _output.WriteLine("");
        _output.WriteLine(
            "PASS: all 10 Engine 3.0 body-composition calculations match.");
    }

    [Fact]
    public void AnthropometricFallback_UsesSexWhenBodyFatIsUnavailable()
    {
        /*
            Same age, height and body weight.

            This intentionally isolates sex within the published
            anthropometric FFM equation.
        */

        var male =
            _estimator.Calculate(
                new Engine30BodyCompositionInput(
                    AgeYears: 40,
                    SexCode: "MALE",
                    HeightIn: 68m,
                    WeightLb: 170m,
                    BodyFatFraction: null));

        var female =
            _estimator.Calculate(
                new Engine30BodyCompositionInput(
                    AgeYears: 40,
                    SexCode: "FEMALE",
                    HeightIn: 68m,
                    WeightLb: 170m,
                    BodyFatFraction: null));

        _output.WriteLine("");
        _output.WriteLine(
            "MATCHED MALE/FEMALE ANTHROPOMETRIC FALLBACK");

        _output.WriteLine(
            "Age 40 | 5'8\" | 170 lb | body fat unavailable");

        _output.WriteLine("");

        _output.WriteLine(
            $"Male estimated FFM:   {male.FatFreeMassLb:0.00} lb");

        _output.WriteLine(
            $"Female estimated FFM: {female.FatFreeMassLb:0.00} lb");

        _output.WriteLine(
            $"Difference:           {(male.FatFreeMassLb - female.FatFreeMassLb):0.00} lb");

        Assert.Equal(
            127.83m,
            male.FatFreeMassLb);

        Assert.Equal(
            111.62m,
            female.FatFreeMassLb);

        Assert.True(
            male.FatFreeMassLb >
            female.FatFreeMassLb);
    }

    [Fact]
    public void BodyFatDerivedFfm_DoesNotApplyAdditionalSexMultiplier()
    {
        /*
            Same body weight and same BF%.

            Once body composition has been supplied, FFM is simply
            total body mass minus fat mass.

            Sex must NOT be applied again afterward.
        */

        var male =
            _estimator.Calculate(
                new Engine30BodyCompositionInput(
                    AgeYears: 40,
                    SexCode: "MALE",
                    HeightIn: 68m,
                    WeightLb: 170m,
                    BodyFatFraction: 0.20m));

        var female =
            _estimator.Calculate(
                new Engine30BodyCompositionInput(
                    AgeYears: 40,
                    SexCode: "FEMALE",
                    HeightIn: 68m,
                    WeightLb: 170m,
                    BodyFatFraction: 0.20m));

        Assert.Equal(
            male.FatFreeMassLb,
            female.FatFreeMassLb);

        Assert.Equal(
            136.00m,
            male.FatFreeMassLb);
    }

    [Fact]
    public void AgeAbovePublishedValidationRange_IsExplicitlyFlagged()
    {
        var result =
            _estimator.Calculate(
                new Engine30BodyCompositionInput(
                    AgeYears: 70,
                    SexCode: "FEMALE",
                    HeightIn: 67m,
                    WeightLb: 145m));

        Assert.Equal(
            "ANTHROPOMETRIC_EXTRAPOLATED",
            result.BodyCompositionSourceCode);

        Assert.Equal(
            "LOW",
            result.ConfidenceCode);
    }

    private static string HeightDisplay(
        decimal heightIn)
    {
        var inches =
            (int)Math.Round(
                heightIn,
                0,
                MidpointRounding.AwayFromZero);

        return $"{inches / 12}'{inches % 12}\"";
    }

    private sealed record TestCase(
        int Number,
        int Age,
        string Sex,
        decimal HeightIn,
        decimal WeightLb,
        decimal? BodyFatFraction,
        decimal ExpectedFfmLb,
        decimal ExpectedFfmKg,
        decimal ExpectedFfmFraction,
        string ExpectedSource,
        string ExpectedConfidence);
}
