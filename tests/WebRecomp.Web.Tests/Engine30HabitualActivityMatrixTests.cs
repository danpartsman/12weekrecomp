using System.Globalization;
using System.Text;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// Permanent Engine 3.0 Bucket #2 age x work-hours matrix.
///
/// The matrix intentionally holds Base Living constant at 1680 kcal/day
/// (70 kcal/hour) so the independent age effect inside Habitual Activity
/// can be inspected without mixing in the separate age effect already
/// present in Base Living Energy.
///
/// Test matrix:
/// ages      = 40, 50, 60, 70, 80
/// work hrs  = 0, 20, 40, 60, 80 per week
/// work EOAF = .20 sedentary-office reference
/// home EOAF = calculator default .60
/// lifestyle = MINIMAL, with LIGHT checked independently
/// </summary>
public sealed class Engine30HabitualActivityMatrixTests
{
    private const decimal BaseLivingKcal = 1680m;
    private const decimal RestingKcalPerHour = 70m;
    private const decimal OfficeActivityFactor = 0.20m;
    private const decimal HomeActivityFactor = 0.60m;

    private static readonly int[] Ages =
        [40, 50, 60, 70, 80];

    private static readonly decimal[] WorkHours =
        [0m, 20m, 40m, 60m, 80m];

    [Fact]
    public void AgeWorkMatrix_StructuralInvariants()
    {
        var calculator =
            new Engine30HabitualActivityCalculator();

        var rows =
            BuildRows(calculator);

        Assert.Equal(
            Ages.Length * WorkHours.Length,
            rows.Count);

        foreach (var row in rows)
        {
            var expectedWindow =
                ExpectedWindow(row.AgeYears);

            var expectedHome =
                Math.Max(
                    0m,
                    expectedWindow - row.WorkHoursPerWeek);

            var expectedWorkDaily =
                Round(
                    RestingKcalPerHour
                    * OfficeActivityFactor
                    * row.WorkHoursPerWeek
                    / 7m);

            var expectedHomeDaily =
                Round(
                    RestingKcalPerHour
                    * HomeActivityFactor
                    * expectedHome
                    / 7m);

            Assert.Equal(
                expectedWindow,
                row.WindowHoursPerWeek);

            Assert.Equal(
                row.WorkHoursPerWeek,
                row.TotalOccupationalHoursPerWeek);

            Assert.Equal(
                expectedHome,
                row.HomeEquivalentHoursPerWeek);

            Assert.True(
                row.HomeEquivalentHoursPerWeek >= 0m);

            Assert.Equal(
                expectedWorkDaily,
                row.AverageDailyOccupationalKcal);

            Assert.Equal(
                expectedHomeDaily,
                row.AverageDailyHomeKcal);

            Assert.Equal(
                Round(expectedWorkDaily + expectedHomeDaily),
                row.AverageDailyHabitualKcal);

            // Existing LIGHT lifestyle remains a separate +4 equivalent hours.
            Assert.Equal(
                24m,
                row.LightPremiumDailyKcal);
        }

        // At fixed reported work, increasing age may reduce inferred home activity
        // but must never increase it due to the age-window policy.
        foreach (var workHours in WorkHours)
        {
            var ordered =
                rows
                    .Where(x => x.WorkHoursPerWeek == workHours)
                    .OrderBy(x => x.AgeYears)
                    .ToArray();

            for (var i = 1; i < ordered.Length; i++)
            {
                Assert.True(
                    ordered[i].AverageDailyHabitualKcal
                    <= ordered[i - 1].AverageDailyHabitualKcal + 0.01m,
                    $"Habitual kcal increased with age at {workHours:0} work h/wk: " +
                    $"{ordered[i - 1].AgeYears} -> {ordered[i].AgeYears}.");
            }
        }

        // Actual reported work must remain fully represented even when it exceeds
        // the age-adjusted background window.
        foreach (var age in Ages)
        {
            var ageRows =
                rows.Where(x => x.AgeYears == age);

            foreach (var row in ageRows.Where(
                         x => x.WorkHoursPerWeek > x.WindowHoursPerWeek))
            {
                Assert.Equal(
                    row.WorkHoursPerWeek,
                    row.TotalOccupationalHoursPerWeek);

                Assert.Equal(
                    0m,
                    row.HomeEquivalentHoursPerWeek);
            }
        }
    }

    [Fact]
    public void AgeWorkMatrix_WriteReport_WhenRequested()
    {
        var outputDirectory =
            Environment.GetEnvironmentVariable(
                "ENGINE30_HABITUAL_MATRIX_OUTPUT_DIR");

        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(
            outputDirectory);

        var calculator =
            new Engine30HabitualActivityCalculator();

        var rows =
            BuildRows(calculator);

        WriteReport(
            outputDirectory,
            rows);

        WriteCsv(
            outputDirectory,
            rows);
    }

    private static List<MatrixRow> BuildRows(
        Engine30HabitualActivityCalculator calculator)
    {
        var rows =
            new List<MatrixRow>();

        foreach (var age in Ages)
        {
            var window =
                Engine30HabitualActivityWindowPolicy.Resolve(
                    age);

            foreach (var workHours in WorkHours)
            {
                var roles =
                    workHours <= 0m
                        ? Array.Empty<Engine30OccupationalRoleInput>()
                        : new[]
                        {
                            new Engine30OccupationalRoleInput(
                                OccupationName: "Office",
                                OccupationActivityCode: "SEDENTARY",
                                TypicalHoursPerWeek: workHours,
                                EffectiveOccupationalActivityFactor:
                                    OfficeActivityFactor)
                        };

                var minimal =
                    calculator.Calculate(
                        baseLivingEnergyKcal:
                            BaseLivingKcal,
                        roles:
                            roles,
                        lifestyleActivityCode:
                            "MINIMAL",
                        homeActivityFactor:
                            HomeActivityFactor,
                        habitualWindowHoursPerWeek:
                            window.HoursPerWeek);

                var light =
                    calculator.Calculate(
                        baseLivingEnergyKcal:
                            BaseLivingKcal,
                        roles:
                            roles,
                        lifestyleActivityCode:
                            "LIGHT",
                        homeActivityFactor:
                            HomeActivityFactor,
                        habitualWindowHoursPerWeek:
                            window.HoursPerWeek);

                rows.Add(
                    new MatrixRow(
                        AgeYears:
                            age,
                        WindowHoursPerWeek:
                            window.HoursPerWeek,
                        WorkHoursPerWeek:
                            workHours,
                        TotalOccupationalHoursPerWeek:
                            minimal.TotalOccupationalHoursPerWeek,
                        HomeEquivalentHoursPerWeek:
                            minimal.HomeEquivalentHoursPerWeek,
                        AverageDailyOccupationalKcal:
                            minimal.AverageDailyOccupationalActivityEnergyKcal,
                        AverageDailyHomeKcal:
                            minimal.AverageDailyHomeActivityEnergyKcal,
                        AverageDailyHabitualKcal:
                            minimal.AverageDailyHabitualActivityEnergyKcal,
                        LightPremiumDailyKcal:
                            Round(
                                light.AverageDailyHabitualActivityEnergyKcal
                                - minimal.AverageDailyHabitualActivityEnergyKcal)));
            }
        }

        return rows;
    }

    private static void WriteReport(
        string outputDirectory,
        IReadOnlyList<MatrixRow> rows)
    {
        var report =
            new StringBuilder();

        report.AppendLine(
            "12 WEEK RECOMP - ENGINE 3.0 HABITUAL ACTIVITY AGE x WORK-HOURS MATRIX");
        report.AppendLine(
            "==============================================================================================");
        report.AppendLine(
            $"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(
            "Purpose: isolate Bucket #2 age-window behavior using a fixed Base Living value.");
        report.AppendLine(
            "Fixed Base Living: 1680 kcal/day = 70 kcal/hour");
        report.AppendLine(
            "Occupation reference: sedentary office EOAF 0.20");
        report.AppendLine(
            "Home activity factor: 0.60");
        report.AppendLine(
            "Lifestyle: MINIMAL; LIGHT is checked as a separate premium");
        report.AppendLine();
        report.AppendLine(
            "AGE WINDOW WORK HOME_EQ WORK_KCAL HOME_KCAL HAB_KCAL LIGHT_BONUS");
        report.AppendLine(
            "--------------------------------------------------------------------------");

        foreach (var row in rows)
        {
            report.AppendLine(
                $"{row.AgeYears,3} " +
                $"{row.WindowHoursPerWeek,6:0} " +
                $"{row.WorkHoursPerWeek,4:0} " +
                $"{row.HomeEquivalentHoursPerWeek,7:0} " +
                $"{row.AverageDailyOccupationalKcal,9:0.##} " +
                $"{row.AverageDailyHomeKcal,9:0.##} " +
                $"{row.AverageDailyHabitualKcal,8:0.##} " +
                $"{row.LightPremiumDailyKcal,11:0.##}");
        }

        report.AppendLine();
        report.AppendLine(
            "STRUCTURAL CHECKS");
        report.AppendLine(
            "-----------------");
        report.AppendLine(
            "GREEN  Age-window schedule = 60,60,50,40,30 h/week at ages 40,50,60,70,80.");
        report.AppendLine(
            "GREEN  Home-equivalent hours = max(0, age window - actual work hours).");
        report.AppendLine(
            "GREEN  Actual reported work hours are never capped by the age window.");
        report.AppendLine(
            "GREEN  Habitual activity is non-increasing with age when work hours are held constant.");
        report.AppendLine(
            "GREEN  LIGHT lifestyle premium remains orthogonal to work/home window (+24 kcal/day in this fixed-base test).");
        report.AppendLine();
        report.AppendLine(
            "IMPORTANT: This matrix validates Engine behavior and internal policy consistency.");
        report.AppendLine(
            "It does NOT by itself prove that the draft 60-to-30-hour age policy is scientifically final.");

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Habitual_AgeWork_Matrix_Report.txt"),
            report.ToString());
    }

    private static void WriteCsv(
        string outputDirectory,
        IReadOnlyList<MatrixRow> rows)
    {
        var csv =
            new StringBuilder();

        csv.AppendLine(
            "AgeYears,WindowHoursPerWeek,WorkHoursPerWeek,HomeEquivalentHoursPerWeek,AverageDailyOccupationalKcal,AverageDailyHomeKcal,AverageDailyHabitualKcal,LightPremiumDailyKcal");

        foreach (var row in rows)
        {
            csv.AppendLine(
                string.Join(
                    ",",
                    row.AgeYears.ToString(
                        CultureInfo.InvariantCulture),
                    Num(row.WindowHoursPerWeek),
                    Num(row.WorkHoursPerWeek),
                    Num(row.HomeEquivalentHoursPerWeek),
                    Num(row.AverageDailyOccupationalKcal),
                    Num(row.AverageDailyHomeKcal),
                    Num(row.AverageDailyHabitualKcal),
                    Num(row.LightPremiumDailyKcal)));
        }

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Habitual_AgeWork_Matrix.csv"),
            csv.ToString());
    }

    private static decimal ExpectedWindow(
        int ageYears)
    {
        if (ageYears <= 50)
            return 60m;

        return Math.Max(
            30m,
            60m - (ageYears - 50));
    }

    private static decimal Round(decimal value)
        => Math.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);

    private static string Num(decimal value)
        => value.ToString(
            "0.####",
            CultureInfo.InvariantCulture);

    private sealed record MatrixRow(
        int AgeYears,
        decimal WindowHoursPerWeek,
        decimal WorkHoursPerWeek,
        decimal TotalOccupationalHoursPerWeek,
        decimal HomeEquivalentHoursPerWeek,
        decimal AverageDailyOccupationalKcal,
        decimal AverageDailyHomeKcal,
        decimal AverageDailyHabitualKcal,
        decimal LightPremiumDailyKcal);
}