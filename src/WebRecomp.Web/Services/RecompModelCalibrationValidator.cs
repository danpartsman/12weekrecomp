namespace WebRecomp.Web.Services;

public static class RecompModelCalibrationValidator
{
    public static IReadOnlyList<RecompEngineValidationIssue> Validate(
        IReadOnlyDictionary<string, decimal> settings)
    {
        var issues = new List<RecompEngineValidationIssue>();

        decimal Value(string key) =>
            ConfigurationService.Setting(
                settings,
                key,
                RecompModelCalibrationCatalog.Default(key));

        foreach (var definition in RecompModelCalibrationCatalog.All)
        {
            var value = Value(definition.Key);

            if (definition.MinimumValue.HasValue
                && value < definition.MinimumValue.Value)
            {
                issues.Add(new RecompEngineValidationIssue(
                    "MODEL_VALUE_BELOW_MINIMUM",
                    $"{definition.DisplayName} is below its allowed minimum of {definition.MinimumValue.Value}.",
                    definition.Key));
            }

            if (definition.MaximumValue.HasValue
                && value > definition.MaximumValue.Value)
            {
                issues.Add(new RecompEngineValidationIssue(
                    "MODEL_VALUE_ABOVE_MAXIMUM",
                    $"{definition.DisplayName} is above its allowed maximum of {definition.MaximumValue.Value}.",
                    definition.Key));
            }
        }

        ValidateOrderedPair(
            RecompModelCalibrationCatalog.RpeIntensityMinimum,
            RecompModelCalibrationCatalog.RpeIntensityMaximum,
            "RPE intensity minimum cannot exceed RPE intensity maximum.");

        ValidateOrderedPair(
            RecompModelCalibrationCatalog.DensityFactorMinimum,
            RecompModelCalibrationCatalog.DensityFactorMaximum,
            "Workout density minimum cannot exceed workout density maximum.");

        ValidateOrderedPair(
            RecompModelCalibrationCatalog.DurationFactorMinimum,
            RecompModelCalibrationCatalog.DurationFactorMaximum,
            "Duration load minimum cannot exceed duration load maximum.");

        ValidateOrderedPair(
            RecompModelCalibrationCatalog.LoadRpeFactorMinimum,
            RecompModelCalibrationCatalog.LoadRpeFactorMaximum,
            "RPE load minimum cannot exceed RPE load maximum.");

        if (Value(RecompModelCalibrationCatalog.SleepSevereRatio)
            >= Value(RecompModelCalibrationCatalog.SleepReducedRatio))
        {
            issues.Add(new RecompEngineValidationIssue(
                "SLEEP_SEVERE_NOT_BELOW_REDUCED",
                "Severely Reduced Sleep Ratio must be lower than Reduced Sleep Ratio.",
                RecompModelCalibrationCatalog.SleepSevereRatio));
        }

        if (Value(RecompModelCalibrationCatalog.EnergySevereMax)
            > Value(RecompModelCalibrationCatalog.EnergyReducedMax))
        {
            issues.Add(new RecompEngineValidationIssue(
                "ENERGY_SEVERE_ABOVE_REDUCED",
                "Severely Reduced Energy Maximum cannot exceed Reduced Energy Maximum.",
                RecompModelCalibrationCatalog.EnergySevereMax));
        }

        if (Value(RecompModelCalibrationCatalog.SorenessSevereMin)
            < Value(RecompModelCalibrationCatalog.SorenessReducedMin))
        {
            issues.Add(new RecompEngineValidationIssue(
                "SORENESS_SEVERE_BELOW_REDUCED",
                "Severely Reduced Soreness Minimum cannot be lower than Reduced Soreness Minimum.",
                RecompModelCalibrationCatalog.SorenessSevereMin));
        }
        var fuelWindow =
            Value(RecompModelCalibrationCatalog.FuelWindowDays);

        if (fuelWindow != Math.Truncate(fuelWindow))
        {
            issues.Add(new RecompEngineValidationIssue(
                "FUEL_WINDOW_NOT_WHOLE_DAYS",
                "Fuel Recovery Lookback Window must be a whole number of days.",
                RecompModelCalibrationCatalog.FuelWindowDays));
        }

        return issues;

        void ValidateOrderedPair(
            string minimumKey,
            string maximumKey,
            string message)
        {
            if (Value(minimumKey) > Value(maximumKey))
            {
                issues.Add(new RecompEngineValidationIssue(
                    "MODEL_MINIMUM_EXCEEDS_MAXIMUM",
                    message,
                    minimumKey));
            }
        }
    }
}

