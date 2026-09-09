namespace WebRecomp.Web.Services;

public sealed record RecompEngineValidationIssue(
    string Code,
    string Message,
    string? ParameterKey = null);

public static class RecompEngineConfigurationValidator
{
    public static IReadOnlyList<RecompEngineValidationIssue> Validate(
        IReadOnlyDictionary<string, decimal> settings)
    {
        var issues = new List<RecompEngineValidationIssue>();

        decimal Value(string key) =>
            ConfigurationService.Setting(
                settings,
                key,
                RecompEngineParameterCatalog.Default(key));

        // Validate every catalog parameter against its documented range.
        foreach (var definition in RecompEngineParameterCatalog.All)
        {
            var value = Value(definition.Key);

            if (definition.MinimumValue.HasValue
                && value < definition.MinimumValue.Value)
            {
                issues.Add(new RecompEngineValidationIssue(
                    "BELOW_MINIMUM",
                    $"{definition.DisplayName} is {value}, below the allowed minimum of {definition.MinimumValue.Value}.",
                    definition.Key));
            }

            if (definition.MaximumValue.HasValue
                && value > definition.MaximumValue.Value)
            {
                issues.Add(new RecompEngineValidationIssue(
                    "ABOVE_MAXIMUM",
                    $"{definition.DisplayName} is {value}, above the allowed maximum of {definition.MaximumValue.Value}.",
                    definition.Key));
            }
        }

        var adequate =
            Value(RecompEngineParameterCatalog.ReadinessAdequate);

        var slightlyReduced =
            Value(RecompEngineParameterCatalog.ReadinessSlightlyReduced);

        var moderatelyLow =
            Value(RecompEngineParameterCatalog.ReadinessModeratelyLow);

        var low =
            Value(RecompEngineParameterCatalog.ReadinessLow);

        if (!(adequate > slightlyReduced
              && slightlyReduced > moderatelyLow
              && moderatelyLow > low))
        {
            issues.Add(new RecompEngineValidationIssue(
                "READINESS_ORDER_INVALID",
                "Readiness thresholds must be ordered: ADEQUATE > SLIGHTLY_REDUCED > MODERATELY_LOW > LOW."));
        }

        var hard3 =
            Value(RecompEngineParameterCatalog.Hard3TargetBonus);

        var hard4 =
            Value(RecompEngineParameterCatalog.Hard4TargetBonus);

        if (hard4 < hard3)
        {
            issues.Add(new RecompEngineValidationIssue(
                "HARD_DAY_BONUS_ORDER_INVALID",
                "The four-hard-day restoration bonus cannot be lower than the three-hard-day restoration bonus."));
        }

        ValidateDescendingCaps(
            issues,
            "ALLOCATION_CAP_ORDER_INVALID",
            "Daily rescue allocation caps",
            [
                Value(RecompEngineParameterCatalog.AllocationCapFuel5),
                Value(RecompEngineParameterCatalog.AllocationCapFuel4),
                Value(RecompEngineParameterCatalog.AllocationCapFuel3),
                Value(RecompEngineParameterCatalog.AllocationCapFuel25),
                Value(RecompEngineParameterCatalog.AllocationCapFuel2),
                Value(RecompEngineParameterCatalog.AllocationCapFuel1),
                Value(RecompEngineParameterCatalog.AllocationCapDefault)
            ]);

        ValidateDescendingCaps(
            issues,
            "PREWORKOUT_CAP_ORDER_INVALID",
            "Pre-workout rescue caps",
            [
                Value(RecompEngineParameterCatalog.PreWorkoutCapFuel5),
                Value(RecompEngineParameterCatalog.PreWorkoutCapFuel4),
                Value(RecompEngineParameterCatalog.PreWorkoutCapFuel3),
                Value(RecompEngineParameterCatalog.PreWorkoutCapDefault)
            ]);

        ValidatePreWorkoutWithinAllocation(
            issues,
            "Fuel Score 5",
            RecompEngineParameterCatalog.PreWorkoutCapFuel5,
            RecompEngineParameterCatalog.AllocationCapFuel5,
            settings);

        ValidatePreWorkoutWithinAllocation(
            issues,
            "Fuel Score 4",
            RecompEngineParameterCatalog.PreWorkoutCapFuel4,
            RecompEngineParameterCatalog.AllocationCapFuel4,
            settings);

        ValidatePreWorkoutWithinAllocation(
            issues,
            "Fuel Score 3",
            RecompEngineParameterCatalog.PreWorkoutCapFuel3,
            RecompEngineParameterCatalog.AllocationCapFuel3,
            settings);

        ValidatePreWorkoutWithinAllocation(
            issues,
            "Default",
            RecompEngineParameterCatalog.PreWorkoutCapDefault,
            RecompEngineParameterCatalog.AllocationCapDefault,
            settings);

        return issues;
    }

    public static void ValidateOrThrow(
        IReadOnlyDictionary<string, decimal> settings)
    {
        var issues = Validate(settings);

        if (issues.Count == 0)
            return;

        throw new InvalidOperationException(
            "Recomp Engine configuration is invalid: "
            + string.Join(
                " | ",
                issues.Select(x => x.Message)));
    }

    private static void ValidateDescendingCaps(
        ICollection<RecompEngineValidationIssue> issues,
        string code,
        string label,
        IReadOnlyList<decimal> values)
    {
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i - 1] >= values[i])
                continue;

            issues.Add(new RecompEngineValidationIssue(
                code,
                $"{label} must not increase as workout fuel demand decreases."));

            return;
        }
    }

    private static void ValidatePreWorkoutWithinAllocation(
        ICollection<RecompEngineValidationIssue> issues,
        string label,
        string preWorkoutKey,
        string allocationKey,
        IReadOnlyDictionary<string, decimal> settings)
    {
        decimal Value(string key) =>
            ConfigurationService.Setting(
                settings,
                key,
                RecompEngineParameterCatalog.Default(key));

        var preWorkout = Value(preWorkoutKey);
        var allocation = Value(allocationKey);

        if (preWorkout <= allocation)
            return;

        issues.Add(new RecompEngineValidationIssue(
            "PREWORKOUT_EXCEEDS_ALLOCATION",
            $"{label} pre-workout rescue cap ({preWorkout} g) cannot exceed its total daily rescue allocation cap ({allocation} g).",
            preWorkoutKey));
    }
}
