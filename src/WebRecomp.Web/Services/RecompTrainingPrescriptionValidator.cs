namespace WebRecomp.Web.Services;

public static class RecompTrainingPrescriptionValidator
{
    public static IReadOnlyList<RecompEngineValidationIssue> Validate(
        IReadOnlyDictionary<string, decimal> settings)
    {
        var issues = new List<RecompEngineValidationIssue>();

        decimal Value(string key) =>
            ConfigurationService.Setting(
                settings,
                key,
                RecompTrainingPrescriptionCatalog.Default(key));

        foreach (var definition in RecompTrainingPrescriptionCatalog.All)
        {
            var value = Value(definition.Key);

            if (definition.MinimumValue.HasValue
                && value < definition.MinimumValue.Value)
            {
                issues.Add(new RecompEngineValidationIssue(
                    "TRAINING_VALUE_BELOW_MINIMUM",
                    $"{definition.DisplayName} is below its allowed minimum of {definition.MinimumValue.Value}.",
                    definition.Key));
            }

            if (definition.MaximumValue.HasValue
                && value > definition.MaximumValue.Value)
            {
                issues.Add(new RecompEngineValidationIssue(
                    "TRAINING_VALUE_ABOVE_MAXIMUM",
                    $"{definition.DisplayName} is above its allowed maximum of {definition.MaximumValue.Value}.",
                    definition.Key));
            }
        }

        ValidateOrderedPair(
            RecompTrainingPrescriptionCatalog.HeavyRpeMinimum,
            RecompTrainingPrescriptionCatalog.HeavyRpeMaximum,
            "Major Strength RPE minimum cannot exceed the maximum.");

        ValidateOrderedPair(
            RecompTrainingPrescriptionCatalog.LightRpeMinimum,
            RecompTrainingPrescriptionCatalog.LightRpeMaximum,
            "Light Strength RPE minimum cannot exceed the maximum.");

        ValidateOrderedPair(
            RecompTrainingPrescriptionCatalog.Zone2RpeMinimum,
            RecompTrainingPrescriptionCatalog.Zone2RpeMaximum,
            "Zone 2 RPE minimum cannot exceed the maximum.");

        ValidateOrderedPair(
            RecompTrainingPrescriptionCatalog.ConditioningRpeMinimum,
            RecompTrainingPrescriptionCatalog.ConditioningRpeMaximum,
            "Hard Conditioning RPE minimum cannot exceed the maximum.");

        if (Value(RecompTrainingPrescriptionCatalog.LightVolumeFactor)
            > Value(RecompTrainingPrescriptionCatalog.HeavyVolumeFactor))
        {
            issues.Add(new RecompEngineValidationIssue(
                "LIGHT_VOLUME_EXCEEDS_MAJOR_VOLUME",
                "Light Strength Volume Factor cannot exceed Major Strength Volume Factor.",
                RecompTrainingPrescriptionCatalog.LightVolumeFactor));
        }

        var rpeKeys = new[]
        {
            RecompTrainingPrescriptionCatalog.HeavyRpeMinimum,
            RecompTrainingPrescriptionCatalog.HeavyRpeMaximum,
            RecompTrainingPrescriptionCatalog.LightRpeMinimum,
            RecompTrainingPrescriptionCatalog.LightRpeMaximum,
            RecompTrainingPrescriptionCatalog.Zone2RpeMinimum,
            RecompTrainingPrescriptionCatalog.Zone2RpeMaximum,
            RecompTrainingPrescriptionCatalog.ConditioningRpeMinimum,
            RecompTrainingPrescriptionCatalog.ConditioningRpeMaximum
        };

        foreach (var key in rpeKeys)
        {
            var value = Value(key);

            if (value != Math.Truncate(value))
            {
                issues.Add(new RecompEngineValidationIssue(
                    "TRAINING_RPE_NOT_WHOLE_NUMBER",
                    $"{key} must be a whole-number RPE from 1 through 10.",
                    key));
            }
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
                    "TRAINING_MINIMUM_EXCEEDS_MAXIMUM",
                    message,
                    minimumKey));
            }
        }
    }
}
