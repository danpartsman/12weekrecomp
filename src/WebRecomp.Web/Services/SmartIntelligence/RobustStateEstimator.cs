namespace WebRecomp.Web.Services.SmartIntelligence;

/// <summary>Two-state local linear trend, H=[1,0], daily F=[1,1;0,1].
/// Huber innovation weighting inflates observation variance for isolated outliers.
/// Parameters are explicit smoothing assumptions, not calibrated physiological measurements.
/// Causal: future values never change an earlier point. Missing dates predict, never zero-fill.
/// </summary>
public static class RobustStateEstimator
{
    public static StateSeries Fit(string key, string label, IReadOnlyList<(DateOnly Date, double? Value)> input,
        double noiseFloor, string sensitivity)
    {
        var differences = new List<double>();
        double? priorValue = null;
        var sigma = noiseFloor;
        var multiplier = sensitivity == "responsive" ? 3d : sensitivity == "steady" ? .3d : 1d;
        var qLevel = .0009 * multiplier; var qSlope = .000004 * multiplier;
        double? level = null; double slope = 0, p00 = sigma * sigma, p01 = 0, p11 = .0004;
        var count = 0; var missing = 0; var points = new List<StatePoint>();
        var byDate = input.ToDictionary(x => x.Date, x => x.Value);
        double? Average(DateOnly end, int length)
        {
            var values = Enumerable.Range(0, length).Select(n => byDate.GetValueOrDefault(end.AddDays(-n)))
                .Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            return values.Length == 0 ? null : values.Average();
        }
        foreach (var (date, value) in input)
        {
            // Estimate observation variability only from earlier adjacent readings.
            // A future observation cannot change an earlier filtered estimate.
            if (differences.Count >= 8)
            {
                var center = Median(differences);
                sigma = Math.Max(noiseFloor, 1.4826 * Median(differences.Select(x => Math.Abs(x-center)).ToArray()) / Math.Sqrt(2));
            }
            double? residual = null; var outlier = false;
            if (level.HasValue)
            {
                level += slope;
                p00 += 2 * p01 + p11 + qLevel;
                p01 += p11; p11 += qSlope;
            }
            if (value.HasValue && double.IsFinite(value.Value))
            {
                count++; missing = 0;
                if (!level.HasValue) { level = value; slope = 0; }
                else
                {
                    residual = value - level;
                    var r = sigma * sigma;
                    var z = Math.Abs(residual.Value) / Math.Sqrt(p00 + r);
                    outlier = z > 2.5;
                    if (outlier) r *= Math.Pow(z / 2.5, 2);
                    var denom = p00 + r; var k0 = p00 / denom; var k1 = p01 / denom;
                    level += k0 * residual; slope += k1 * residual.Value;
                    var old00 = p00; var old01 = p01;
                    p00 = Math.Max(1e-9, (1 - k0) * old00);
                    p01 = (1 - k0) * old01; p11 = Math.Max(1e-9, p11 - k1 * old01);
                }
            }
            else missing++;
            if (value.HasValue && priorValue.HasValue) differences.Add(value.Value-priorValue.Value);
            if (differences.Count > 56) differences.RemoveAt(0);
            priorValue = value;
            // This band is conditional on the model. It does not cover unknown device bias.
            var spread = 1.96 * Math.Sqrt(p00);
            var a3 = Average(date, 3); var a7 = Average(date, 7);
            points.Add(new(date, value, level, level - spread, level + spread,
                level.HasValue ? slope * 7 : null, residual, outlier, missing, count,
                a3, a7, a3 - Average(date.AddDays(-7), 3), a7 - Average(date.AddDays(-7), 7)));
        }
        return new(key, label, "lb", sigma, qLevel, points);
    }
    public static double Median(IReadOnlyList<double> source)
    {
        if (source.Count == 0) return 0;
        var x = source.Order().ToArray(); var n = x.Length;
        return n % 2 == 1 ? x[n / 2] : (x[n / 2 - 1] + x[n / 2]) / 2;
    }
}
