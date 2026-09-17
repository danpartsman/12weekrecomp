namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// v1009 Dashboard interpretation layer.
///
/// Engine 3.0 remains authoritative for physiology. This service interprets
/// already-calculated workout, nutrition, fuel-state, and scale evidence.
/// It never turns a single morning scale/BIA change into a claim of literal
/// overnight fat or muscle gain/loss.
/// </summary>
public sealed class Engine30DashboardAnalysisService
{
    public const string MissingCss = "gray";
    public const string GoodCss = "green";
    public const string WatchCss = "yellow";
    public const string InvestigateCss = "orange";
    public const string ProblemCss = "red";

    public Engine30DashboardAnalysisResult Analyze(
        Engine30DashboardAnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var context =
            ClassifyDay(
                input.ActualWorkoutCodes,
                input.PlannedWorkoutCode);

        var workout =
            ScoreWorkout(
                input.PlannedWorkoutCode,
                input.ActualWorkoutCodes,
                input.ActualWorkoutSummary);

        var glycogen =
            ScoreGlycogen(
                context,
                input.GlycogenDemandG,
                input.RestorationRatio);

        var protein =
            ScoreProtein(
                input.TargetProteinG,
                input.ActualProteinG);

        var calories =
            ScoreCalories(
                context,
                input.TargetCalories,
                input.ActualCalories,
                glycogen,
                protein);

        var carbs =
            ScoreCarbohydrate(
                context,
                input.TargetCarbsG,
                input.ActualCarbsG,
                input.RestorationRatio,
                calories);

        var fat =
            ScoreFat(
                input.TargetFatG,
                input.ActualFatG);

        var scale =
            ScoreScaleConsistency(
                context,
                input,
                calories,
                glycogen);

        var tracking =
            ScoreTrackingConfidence(
                input,
                scale);

        var trendTraining =
            ScoreTrendTraining(
                input.FourDayPlannedTrainingDays,
                input.FourDayCompletedTrainingDays);

        var trendFuel =
            ScoreTrendFuel(
                input.FourDayHardDays,
                input.FourDayModerateHardDays,
                input.FourDayCarbohydrateEvidenceDays,
                input.FourDayRestorationRatio);

        var trendEnergy =
            ScoreTrendEnergy(
                input.WeekEnergyOverageKcal);

        var trendProtein =
            ScoreProtein(
                input.TargetProteinG,
                input.FourDayAverageProteinG,
                "Protein consistency");

        var trendFat =
            ScoreFat(
                input.TargetFatG,
                input.FourDayAverageFatG,
                "Fat consistency");

        var trendScale =
            ScoreTrendScale(input);

        var kudos =
            BuildKudos(
                context,
                input,
                calories,
                glycogen,
                protein);

        return new Engine30DashboardAnalysisResult(
            DayContextCode:
                context.ToString(),

            DayContextLabel:
                DayContextLabel(context),

            Workout:
                workout,

            Glycogen:
                glycogen,

            Calories:
                calories,

            Carbohydrate:
                carbs,

            Protein:
                protein,

            Fat:
                fat,

            ScaleConsistency:
                scale,

            TrackingConfidence:
                tracking,

            TrendTraining:
                trendTraining,

            TrendFueling:
                trendFuel,

            TrendEnergy:
                trendEnergy,

            TrendProtein:
                trendProtein,

            TrendFat:
                trendFat,

            TrendScale:
                trendScale,

            TrendTracking:
                tracking,

            HasCurrentMorningScale:
                input.CurrentMorningWeightLb.HasValue,

            WeightChangeLb:
                Difference(
                    input.CurrentMorningWeightLb,
                    input.PriorMorningWeightLb),

            BodyFatChangePercentagePoints:
                Difference(
                    input.CurrentMorningBodyFatFraction,
                    input.PriorMorningBodyFatFraction)
                is decimal bfDifference
                    ? bfDifference * 100m
                    : null,

            MuscleMassChangeLb:
                Difference(
                    input.CurrentMorningMuscleMassLb,
                    input.PriorMorningMuscleMassLb),

            FourDayWeightChangeLb:
                Difference(
                    input.TrendEndWeightLb,
                    input.TrendStartWeightLb),

            EstimatedRestoredGlycogenG:
                EstimateRestoredGlycogenG(input),

            EstimatedAssociatedWaterG:
                EstimateAssociatedWaterG(input),

            EstimatedGlycogenWaterScaleLb:
                EstimateGlycogenWaterScaleLb(input),

            ScaleExplanation:
                BuildScaleExplanation(
                    context,
                    input),

            Kudos:
                kudos,

            YesterdayNarrative:
                BuildYesterdayNarrative(
                    context,
                    input,
                    calories,
                    glycogen,
                    protein,
                    fat,
                    scale),

            DataQualityNarrative:
                BuildDataQualityNarrative(
                    input,
                    workout,
                    calories,
                    glycogen,
                    fat,
                    scale,
                    tracking),

            TrendNarrative:
                BuildTrendNarrative(
                    input,
                    trendTraining,
                    trendFuel,
                    trendEnergy,
                    trendProtein,
                    trendFat,
                    trendScale,
                    tracking),

            CoachingTake:
                BuildCoachingTake(
                    context,
                    input,
                    glycogen,
                    protein,
                    scale,
                    tracking),

            TrackingCaveat:
                "Nutrition tracking is an estimate unless portions, cooking fats, condiments, snacks, restaurant meals, and beverages are entered consistently. A single scale mismatch proves very little; repeated intake/scale mismatches lower confidence in the log and should trigger a tracking review before the prescription is changed.");
    }

    public static bool IsTrainingWorkoutCode(string? code)
    {
        var context =
            ClassifySingleCode(code);

        return context is not DayContext.Recovery
            and not DayContext.Unknown;
    }

    public static string DayContextLabelFromCodes(
        string? actualWorkoutCodes,
        string? plannedWorkoutCode)
        => DayContextLabel(
            ClassifyDay(
                actualWorkoutCodes,
                plannedWorkoutCode));

    private static DashboardAnalysisSignal ScoreWorkout(
        string? plannedCode,
        string? actualCodes,
        string? actualSummary)
    {
        var planned =
            ClassifySingleCode(plannedCode);

        var actual =
            ClassifyDay(actualCodes, null);

        var hasPlannedTraining =
            planned is not DayContext.Recovery
            and not DayContext.Unknown;

        var hasActualTraining =
            actual is not DayContext.Recovery
            and not DayContext.Unknown;

        if (!hasPlannedTraining && !hasActualTraining)
        {
            return Signal(
                "Workout",
                GoodCss,
                actualSummary ?? "Recovery / no training",
                "No missed-training signal detected.");
        }

        if (hasPlannedTraining && !hasActualTraining)
        {
            var css =
                planned is DayContext.HardStrength
                    or DayContext.HardConditioning
                    ? ProblemCss
                    : WatchCss;

            return Signal(
                "Workout",
                css,
                "No completed workout logged",
                css == ProblemCss
                    ? "A meaningful planned session has no completed-workout entry. Verify whether it was missed or simply not logged."
                    : "A planned session has no completed-workout entry.");
        }

        if (!hasPlannedTraining && hasActualTraining)
        {
            return Signal(
                "Workout",
                actual is DayContext.HardStrength
                    or DayContext.HardConditioning
                    ? InvestigateCss
                    : WatchCss,
                actualSummary ?? HumanizeCodes(actualCodes),
                "Unplanned training adds load that the weekly plan may not have anticipated.");
        }

        var normalizedPlanned =
            NormalizePrimaryCode(plannedCode);

        var normalizedActual =
            NormalizePrimaryCode(actualCodes);

        if (!string.IsNullOrWhiteSpace(normalizedPlanned)
            && normalizedPlanned.Equals(
                normalizedActual,
                StringComparison.OrdinalIgnoreCase))
        {
            return Signal(
                "Workout",
                GoodCss,
                actualSummary ?? HumanizeCodes(actualCodes),
                "Completed workout matches the plan.");
        }

        if (SameBroadTrainingPurpose(planned, actual))
        {
            return Signal(
                "Workout",
                WatchCss,
                actualSummary ?? HumanizeCodes(actualCodes),
                "The workout differed from the exact plan but stayed in a similar training-demand category.");
        }

        return Signal(
            "Workout",
            InvestigateCss,
            actualSummary ?? HumanizeCodes(actualCodes),
            "The completed workout differs materially from the planned training demand. Verify the entry if that was not intentional.");
    }

    private static DashboardAnalysisSignal ScoreGlycogen(
        DayContext context,
        decimal demandG,
        decimal? restorationRatio)
    {
        if (context is DayContext.Recovery
            or DayContext.EasyCardio)
        {
            return Signal(
                "Glycogen refill",
                MissingCss,
                demandG <= 0m
                    ? "Not a priority"
                    : $"{demandG:0} g modeled demand",
                "Glycogen refill is not the primary success metric on this type of day.");
        }

        if (demandG <= 0m)
        {
            return Signal(
                "Glycogen refill",
                MissingCss,
                "No modeled demand",
                "There is no meaningful modeled workout glycogen demand to grade.");
        }

        if (!restorationRatio.HasValue)
        {
            return Signal(
                "Glycogen refill",
                MissingCss,
                "Nutrition evidence missing",
                "Carbohydrate intake is needed to evaluate modeled restoration.");
        }

        var ratio =
            Clamp01(restorationRatio.Value);

        string css;

        if (context is DayContext.HardStrength
            or DayContext.HardConditioning)
        {
            css =
                ratio >= 0.80m ? GoodCss :
                ratio >= 0.60m ? WatchCss :
                ratio >= 0.50m ? InvestigateCss :
                ProblemCss;
        }
        else
        {
            css =
                ratio >= 0.70m ? GoodCss :
                ratio >= 0.55m ? WatchCss :
                ratio >= 0.40m ? InvestigateCss :
                ProblemCss;
        }

        var detail =
            context is DayContext.HardStrength
                or DayContext.HardConditioning
                ? "Hard-day bands: green >=80%, yellow 60-79%, orange 50-59%, red <50%."
                : "Moderate-demand bands are less strict because full glycogen restoration is less urgent.";

        return Signal(
            "Glycogen refill",
            css,
            $"{ratio:P0}",
            detail);
    }

    private static DashboardAnalysisSignal ScoreCalories(
        DayContext context,
        decimal? target,
        decimal? actual,
        DashboardAnalysisSignal glycogen,
        DashboardAnalysisSignal protein)
    {
        if (!target.HasValue
            || target.Value <= 0m
            || !actual.HasValue)
        {
            return Signal(
                "Calories",
                MissingCss,
                "Not enough data",
                "Target and actual calories are both required.");
        }

        var ratio =
            actual.Value / target.Value;

        var variance =
            actual.Value - target.Value;

        string css;

        if (context is DayContext.HardStrength
            or DayContext.HardConditioning)
        {
            css =
                ratio >= 0.88m && ratio <= 1.08m ? GoodCss :
                ratio >= 0.80m && ratio <= 1.15m ? WatchCss :
                ratio >= 0.70m && ratio <= 1.25m ? InvestigateCss :
                ProblemCss;

            // When hard-day recovery priorities are protected, allow a
            // modest additional calorie undershoot to remain green.
            if (ratio >= 0.84m
                && ratio < 0.88m
                && glycogen.CssClass == GoodCss
                && protein.CssClass == GoodCss)
            {
                css = GoodCss;
            }
        }
        else
        {
            // Lower-demand days favor calorie control, but extreme
            // under-eating is never graded green.
            css =
                ratio >= 0.85m && ratio <= 1.05m ? GoodCss :
                ratio >= 0.75m && ratio <= 1.12m ? WatchCss :
                ratio >= 0.65m && ratio <= 1.20m ? InvestigateCss :
                ProblemCss;
        }

        return Signal(
            "Calories",
            css,
            $"{actual.Value:0} / {target.Value:0} kcal",
            $"{variance:+0;-0;0} kcal vs prescription.");
    }

    private static DashboardAnalysisSignal ScoreCarbohydrate(
        DayContext context,
        decimal? target,
        decimal? actual,
        decimal? restorationRatio,
        DashboardAnalysisSignal calories)
    {
        if (!target.HasValue
            || target.Value <= 0m
            || !actual.HasValue)
        {
            return Signal(
                "Carbohydrate",
                MissingCss,
                "Not enough data",
                "Target and actual carbohydrate are both required.");
        }

        var foodRatio =
            actual.Value / target.Value;

        string css;
        string detail;

        if (context is DayContext.HardStrength
            or DayContext.HardConditioning)
        {
            if (restorationRatio.HasValue)
            {
                var refill =
                    Clamp01(restorationRatio.Value);

                css =
                    refill >= 0.80m ? GoodCss :
                    refill >= 0.60m ? WatchCss :
                    refill >= 0.50m ? InvestigateCss :
                    ProblemCss;

                detail =
                    $"Food log is {actual.Value:0} g vs {target.Value:0} g, but the hard-day grade follows modeled glycogen restoration ({refill:P0}).";
            }
            else
            {
                css =
                    foodRatio >= 0.80m ? GoodCss :
                    foodRatio >= 0.60m ? WatchCss :
                    foodRatio >= 0.50m ? InvestigateCss :
                    ProblemCss;

                detail =
                    "Hard training prioritizes carbohydrate availability. With no restoration ratio available, food-log adherence is fallback evidence.";
            }
        }
        else if (context is DayContext.ModerateStrength
                 or DayContext.Cardio)
        {
            css =
                foodRatio >= 0.70m && foodRatio <= 1.20m ? GoodCss :
                foodRatio >= 0.55m && foodRatio <= 1.35m ? WatchCss :
                foodRatio >= 0.40m && foodRatio <= 1.55m ? InvestigateCss :
                ProblemCss;

            detail =
                "Moderate training uses carbohydrate as useful support, but exact hard-day target matching is less important.";
        }
        else
        {
            // Easy cardio/recovery: low carbohydrate is not automatically bad.
            css =
                foodRatio <= 1.15m ? GoodCss :
                foodRatio <= 1.35m ? WatchCss :
                foodRatio <= 1.60m ? InvestigateCss :
                ProblemCss;

            if (foodRatio < 0.30m
                && (calories.CssClass is InvestigateCss or ProblemCss))
            {
                css = WatchCss;
            }

            detail =
                "On recovery and easy-cardio days, carbohydrate may reasonably be lower. Energy control and protein receive more weight.";
        }

        return Signal(
            "Carbohydrate",
            css,
            $"{actual.Value:0} / {target.Value:0} g",
            detail);
    }

    private static DashboardAnalysisSignal ScoreProtein(
        decimal? target,
        decimal? actual,
        string label = "Protein")
    {
        if (!target.HasValue
            || target.Value <= 0m
            || !actual.HasValue)
        {
            return Signal(
                label,
                MissingCss,
                "Not enough data",
                "Protein target and actual are required.");
        }

        var ratio =
            actual.Value / target.Value;

        string css;

        if (ratio > 1.50m)
        {
            css = WatchCss;
        }
        else
        {
            css =
                ratio >= 0.90m ? GoodCss :
                ratio >= 0.80m ? WatchCss :
                ratio >= 0.70m ? InvestigateCss :
                ProblemCss;
        }

        return Signal(
            label,
            css,
            $"{actual.Value:0} / {target.Value:0} g",
            ratio >= 0.90m
                ? "Protein support is functionally covered."
                : "Protein is below the preferred recovery / lean-mass support range.");
    }

    private static DashboardAnalysisSignal ScoreFat(
        decimal? target,
        decimal? actual,
        string label = "Fat")
    {
        if (!target.HasValue
            || target.Value <= 0m
            || !actual.HasValue)
        {
            return Signal(
                label,
                MissingCss,
                "Not enough data",
                "Fat target and actual are required.");
        }

        var ratio =
            actual.Value / target.Value;

        var css =
            ratio >= 0.70m && ratio <= 1.30m ? GoodCss :
            ratio >= 0.55m && ratio <= 1.60m ? WatchCss :
            ratio >= 0.40m && ratio < 2.00m ? InvestigateCss :
            ProblemCss;

        return Signal(
            label,
            css,
            $"{actual.Value:0} / {target.Value:0} g",
            ratio >= 1.60m
                ? "Fat is high enough to materially affect calories and can displace planned training carbohydrate."
                : ratio < 0.70m
                    ? "Fat is below the preferred range; keep the physiological minimum protected."
                    : "Fat is within a practical range.");
    }

    private static DashboardAnalysisSignal ScoreScaleConsistency(
        DayContext context,
        Engine30DashboardAnalysisInput input,
        DashboardAnalysisSignal calories,
        DashboardAnalysisSignal glycogen)
    {
        if (!input.CurrentMorningWeightLb.HasValue)
        {
            return Signal(
                "Scale response",
                MissingCss,
                "Please enter this morning's scale readings",
                "Today's measurement closes the loop between yesterday's behavior and the next-morning response.");
        }

        if (!input.PriorMorningWeightLb.HasValue)
        {
            return Signal(
                "Scale response",
                MissingCss,
                $"{input.CurrentMorningWeightLb.Value:0.0} lb",
                "A prior morning weight is needed to calculate the one-day change.");
        }

        var delta =
            input.CurrentMorningWeightLb.Value
            - input.PriorMorningWeightLb.Value;

        var lowLoggedFuel =
            (calories.CssClass is WatchCss or InvestigateCss or ProblemCss)
            && (glycogen.CssClass is InvestigateCss or ProblemCss);

        if (delta >= 1.5m && lowLoggedFuel)
        {
            return Signal(
                "Scale response",
                delta >= 3.0m
                    ? InvestigateCss
                    : WatchCss,
                $"{delta:+0.0;-0.0;0.0} lb",
                "The log implies relatively low intake/fueling while weight rose meaningfully. Hard training, sodium, hydration, inflammation, gut content, and ordinary scale noise can still explain this; if the mismatch repeats, review nutrition-entry accuracy.");
        }

        if (context is DayContext.HardStrength
            or DayContext.HardConditioning)
        {
            if (glycogen.CssClass == GoodCss
                && delta >= -1.5m
                && delta <= 3.0m)
            {
                return Signal(
                    "Scale response",
                    GoodCss,
                    $"{delta:+0.0;-0.0;0.0} lb",
                    "A flat or higher morning weight can be compatible with hard training and good glycogen restoration because glycogen-associated water, inflammation, sodium, and food volume can raise short-term scale weight.");
            }

            return Signal(
                "Scale response",
                Math.Abs(delta) <= 3.0m
                    ? WatchCss
                    : InvestigateCss,
                $"{delta:+0.0;-0.0;0.0} lb",
                "Hard-training mornings are noisy. Treat this as supporting evidence rather than a one-day tissue-change verdict.");
        }

        if (delta <= 1.5m
            && delta >= -2.5m)
        {
            return Signal(
                "Scale response",
                GoodCss,
                $"{delta:+0.0;-0.0;0.0} lb",
                "The morning movement is compatible with ordinary lower-demand-day fluid and energy variability.");
        }

        return Signal(
            "Scale response",
            Math.Abs(delta) <= 3.0m
                ? WatchCss
                : InvestigateCss,
            $"{delta:+0.0;-0.0;0.0} lb",
            "The scale moved more than the food log alone would predict. One day is not enough to identify the cause.");
    }

    private static DashboardAnalysisSignal ScoreTrackingConfidence(
        Engine30DashboardAnalysisInput input,
        DashboardAnalysisSignal scale)
    {
        if (input.FourDayNutritionCompleteDays <= 0)
        {
            return Signal(
                "Nutrition-log confidence",
                MissingCss,
                "Insufficient",
                "No complete recent nutrition days are available.");
        }

        if (!input.CurrentMorningWeightLb.HasValue)
        {
            if (input.FourDayNutritionCompleteDays >= 3
                && input.FourDayScaleDays >= 2)
            {
                return Signal(
                    "Nutrition-log confidence",
                    WatchCss,
                    "Moderate",
                    "Today's morning scale is missing, so the Yesterday analysis cannot be high confidence yet. Enter today's scale to complete the behavior-to-response comparison.");
            }

            return Signal(
                "Nutrition-log confidence",
                InvestigateCss,
                "Low",
                "Today's morning scale is missing and recent evidence is incomplete. Enter today's scale and keep nutrition logging complete before judging the prescription.");
        }

        var mismatch =
            scale.CssClass is WatchCss or InvestigateCss;

        if (input.FourDayNutritionCompleteDays >= 4
            && input.FourDayScaleDays >= 3
            && !mismatch)
        {
            return Signal(
                "Nutrition-log confidence",
                GoodCss,
                "High",
                "Recent nutrition and scale evidence are sufficiently complete and internally consistent.");
        }

        if (input.FourDayNutritionCompleteDays >= 3
            && input.FourDayScaleDays >= 2)
        {
            return Signal(
                "Nutrition-log confidence",
                WatchCss,
                "Moderate",
                mismatch
                    ? "Logging is reasonably complete, but the current intake/scale relationship deserves another day or two of evidence."
                    : "Evidence is usable but not complete enough for high confidence.");
        }

        return Signal(
            "Nutrition-log confidence",
            InvestigateCss,
            "Low",
            "Recent evidence is incomplete. Improve logging precision before treating model-vs-scale differences as a prescription problem.");
    }

    private static DashboardAnalysisSignal ScoreTrendTraining(
        int plannedDays,
        int completedDays)
    {
        if (plannedDays <= 0)
        {
            return Signal(
                "Training consistency",
                completedDays == 0 ? GoodCss : WatchCss,
                completedDays == 0
                    ? "Recovery window"
                    : $"{completedDays} unplanned day(s)",
                completedDays == 0
                    ? "No training was planned in this recent window."
                    : "Training occurred despite no planned training days.");
        }

        var missed =
            Math.Max(0, plannedDays - completedDays);

        var css =
            missed == 0 ? GoodCss :
            missed == 1 ? WatchCss :
            missed == 2 ? InvestigateCss :
            ProblemCss;

        return Signal(
            "Training consistency",
            css,
            $"{completedDays} / {plannedDays} planned days",
            missed == 0
                ? "Recent planned training was completed."
                : $"{missed} planned training day(s) do not have a completed training entry.");
    }

    private static DashboardAnalysisSignal ScoreTrendFuel(
        short hardDays,
        short moderateHardDays,
        short carbohydrateEvidenceDays,
        decimal restorationRatio)
    {
        if (moderateHardDays <= 0)
        {
            return Signal(
                "Hard-day glycogen support",
                MissingCss,
                "No hard-demand days",
                "This metric is not primary when the recent window contains no meaningful glycogen-demand training.");
        }

        if (carbohydrateEvidenceDays <= 0)
        {
            return Signal(
                "Hard-day glycogen support",
                MissingCss,
                "Nutrition evidence missing",
                "Carbohydrate evidence is required to assess recent restoration.");
        }

        var ratio =
            Clamp01(restorationRatio);

        string css;

        if (hardDays > 0)
        {
            css =
                ratio >= 0.80m ? GoodCss :
                ratio >= 0.60m ? WatchCss :
                ratio >= 0.50m ? InvestigateCss :
                ProblemCss;
        }
        else
        {
            css =
                ratio >= 0.70m ? GoodCss :
                ratio >= 0.55m ? WatchCss :
                ratio >= 0.40m ? InvestigateCss :
                ProblemCss;
        }

        return Signal(
            "Hard-day glycogen support",
            css,
            $"{ratio:P0}",
            $"{carbohydrateEvidenceDays} / 4 recent day(s) contain carbohydrate evidence.");
    }

    private static DashboardAnalysisSignal ScoreTrendEnergy(
        decimal weekEnergyOverageKcal)
    {
        if (weekEnergyOverageKcal <= 1m)
        {
            return Signal(
                "Recent energy control",
                GoodCss,
                "No unresolved overage",
                "Engine 3.0 does not currently show unresolved above-target energy.");
        }

        var css =
            weekEnergyOverageKcal <= 250m ? WatchCss :
            weekEnergyOverageKcal <= 500m ? InvestigateCss :
            ProblemCss;

        return Signal(
            "Recent energy control",
            css,
            $"+{weekEnergyOverageKcal:0} kcal",
            "This is current Engine 3.0 recent/weekly over-target energy context, not a one-day fat-gain calculation.");
    }

    private static DashboardAnalysisSignal ScoreTrendScale(
        Engine30DashboardAnalysisInput input)
    {
        if (input.FourDayScaleDays < 2
            || !input.TrendStartWeightLb.HasValue
            || !input.TrendEndWeightLb.HasValue)
        {
            return Signal(
                "Scale trend",
                MissingCss,
                "Building evidence",
                "At least two recent morning measurements are needed.");
        }

        var delta =
            input.TrendEndWeightLb.Value
            - input.TrendStartWeightLb.Value;

        var plan =
            (input.PlanCode ?? "").Trim().ToUpperInvariant();

        string css;

        if (plan is "PLAN_2" or "FAT_LOSS" or "FAT_LOSS_RECOMP")
        {
            css =
                delta >= -3.0m && delta <= 1.0m ? GoodCss :
                Math.Abs(delta) <= 4.0m ? WatchCss :
                InvestigateCss;
        }
        else if (plan is "PLAN_3" or "MUSCLE" or "MUSCLE_BUILD")
        {
            css =
                delta >= -1.5m && delta <= 3.0m ? GoodCss :
                Math.Abs(delta) <= 4.0m ? WatchCss :
                InvestigateCss;
        }
        else
        {
            css =
                Math.Abs(delta) <= 2.5m ? GoodCss :
                Math.Abs(delta) <= 4.0m ? WatchCss :
                InvestigateCss;
        }

        return Signal(
            "Scale trend",
            css,
            $"{delta:+0.0;-0.0;0.0} lb",
            "Multi-day direction is more useful than a single morning. BIA body-fat and muscle estimates remain hydration-sensitive.");
    }

    private static string? BuildKudos(
        DayContext context,
        Engine30DashboardAnalysisInput input,
        DashboardAnalysisSignal calories,
        DashboardAnalysisSignal glycogen,
        DashboardAnalysisSignal protein)
    {
        if (context is DayContext.HardStrength
            or DayContext.HardConditioning)
        {
            if (glycogen.CssClass == GoodCss
                && protein.CssClass == GoodCss
                && input.TargetCalories.HasValue
                && input.ActualCalories.HasValue
                && input.ActualCalories.Value <= input.TargetCalories.Value
                && input.ActualCalories.Value >= input.TargetCalories.Value * 0.84m)
            {
                var under =
                    input.TargetCalories.Value
                    - input.ActualCalories.Value;

                return
                    $"Strong execution: modeled glycogen restoration reached {Clamp01(input.RestorationRatio ?? 0m):P0} while intake finished about {under:0} kcal below the prescription. Recovery fueling was protected without simply pushing calories higher.";
            }

            if (glycogen.CssClass == GoodCss)
            {
                return
                    $"Good hard-day fueling: modeled glycogen restoration reached {Clamp01(input.RestorationRatio ?? 0m):P0}, which is in the functional green range.";
            }
        }

        if (context is DayContext.Cardio
            or DayContext.EasyCardio
            or DayContext.Recovery)
        {
            if (calories.CssClass == GoodCss
                && protein.CssClass == GoodCss)
            {
                return
                    "Good lower-demand-day execution: energy stayed controlled while protein remained protected.";
            }
        }

        return null;
    }

    private static string BuildYesterdayNarrative(
        DayContext context,
        Engine30DashboardAnalysisInput input,
        DashboardAnalysisSignal calories,
        DashboardAnalysisSignal glycogen,
        DashboardAnalysisSignal protein,
        DashboardAnalysisSignal fat,
        DashboardAnalysisSignal scale)
    {
        var parts =
            new List<string>();

        parts.Add(
            context switch
            {
                DayContext.HardStrength =>
                    "Yesterday was a high-demand resistance day. Glycogen restoration is the primary fueling score; calories matter too, but a modest calorie undershoot can still be productive when protein and recovery carbohydrate are protected.",

                DayContext.HardConditioning =>
                    "Yesterday was a high-demand conditioning / interval day. Glycogen support matters substantially, while total energy still needs to stay consistent with the recomp prescription.",

                DayContext.ModerateStrength =>
                    "Yesterday was a moderate resistance day. Protein and useful carbohydrate support matter, but perfect glycogen replacement is less urgent than after a major hard session.",

                DayContext.Cardio =>
                    "Yesterday was a cardio-focused day. Energy control receives more weight than maximizing glycogen refill, while protein remains protected.",

                DayContext.EasyCardio =>
                    "Yesterday was an easy-cardio / Zone 2 type day. The main objective is useful aerobic work with controlled energy intake; low carbohydrate is not automatically a problem.",

                DayContext.Recovery =>
                    "Yesterday was a recovery / no-training day. Protein and energy control matter more than forcing carbohydrate up to a training-day level.",

                _ =>
                    "Yesterday's training demand could not be classified confidently, so the analysis stays conservative."
            });

        if (!input.ActualCalories.HasValue)
        {
            parts.Add(
                "Nutrition actuals are missing, so no intake-performance conclusion is made.");
        }
        else
        {
            parts.Add(
                $"Calories are {PlainStatus(calories.CssClass)}, glycogen support {PlainStatus(glycogen.CssClass)}, protein {PlainStatus(protein.CssClass)}, and fat {PlainStatus(fat.CssClass)} for this day type.");
        }

        if (!input.CurrentMorningWeightLb.HasValue)
        {
            parts.Add(
                "Please enter this morning's scale readings for a more detailed next-morning analysis.");
        }
        else
        {
            parts.Add(
                BuildScaleExplanation(
                    context,
                    input));
        }

        return string.Join(" ", parts);
    }

    private static string BuildDataQualityNarrative(
        Engine30DashboardAnalysisInput input,
        DashboardAnalysisSignal workout,
        DashboardAnalysisSignal calories,
        DashboardAnalysisSignal glycogen,
        DashboardAnalysisSignal fat,
        DashboardAnalysisSignal scale,
        DashboardAnalysisSignal tracking)
    {
        if (!input.ActualCalories.HasValue
            || !input.ActualProteinG.HasValue
            || !input.ActualCarbsG.HasValue
            || !input.ActualFatG.HasValue)
        {
            return
                "Nutrition actuals are incomplete. Missing intake remains missing evidence; the Dashboard will not pretend the missing values were zero.";
        }

        if ((scale.CssClass is WatchCss or InvestigateCss)
            && (calories.CssClass is WatchCss or InvestigateCss or ProblemCss)
            && (glycogen.CssClass is InvestigateCss or ProblemCss))
        {
            return
                "Potential tracking mismatch: the food log implies relatively low energy/fueling, yet the next-morning scale moved higher than that log alone would suggest. One day can be explained by hydration, sodium, hard-training inflammation, gut content, or scale noise. If this repeats, review portion sizes, cooking oils, condiments, restaurant estimates, beverages, snacks, and missed entries before changing the metabolic prescription.";
        }

        if (fat.CssClass is InvestigateCss or ProblemCss)
        {
            return
                "Potential nutrition-entry review: fat is materially outside the planned range. If that does not match what you remember eating, verify portions, cooking fats, sauces/condiments, restaurant estimates, and any foods that may have been omitted.";
        }

        if (workout.CssClass is InvestigateCss or ProblemCss)
        {
            return
                "Potential workout-entry review: the completed-workout record differs materially from the planned demand or is missing. Verify workout type, duration, RPE, and completion status if the Dashboard description does not match what actually happened.";
        }

        if (tracking.CssClass == GoodCss)
        {
            return
                "No obvious internal conflict is present in the recent workout, nutrition, and scale evidence. Continue logging with the same level of care.";
        }

        return
            "The entries are usable, but confidence is not yet high. Real-world nutrition logs commonly contain estimation error unless portions and calorie-dense extras are measured consistently; use repeated scale/training evidence to decide whether a logging review is warranted.";
    }

    private static string BuildTrendNarrative(
        Engine30DashboardAnalysisInput input,
        DashboardAnalysisSignal training,
        DashboardAnalysisSignal fuel,
        DashboardAnalysisSignal energy,
        DashboardAnalysisSignal protein,
        DashboardAnalysisSignal fat,
        DashboardAnalysisSignal scale,
        DashboardAnalysisSignal tracking)
    {
        var parts =
            new List<string>
            {
                $"Over the recent four-day window, training consistency is {PlainStatus(training.CssClass)}, hard-day fueling is {PlainStatus(fuel.CssClass)}, and recent energy control is {PlainStatus(energy.CssClass)}.",
                $"Protein consistency is {PlainStatus(protein.CssClass)} and fat consistency is {PlainStatus(fat.CssClass)}."
            };

        if (input.FourDayScaleDays >= 2)
        {
            parts.Add(
                $"The multi-day scale signal is {PlainStatus(scale.CssClass)}. This is more useful than a single morning, although short-term BIA body-fat and muscle estimates still move with hydration.");
        }
        else
        {
            parts.Add(
                "There are not yet enough recent scale mornings for a confident body-composition trend.");
        }

        parts.Add(
            $"Nutrition-log confidence is currently {tracking.Value.ToLowerInvariant()}. The system should increase suspicion of under- or over-tracking only when intake, training, and scale evidence disagree repeatedly, not from one odd morning.");

        if (input.FourDayHardDays > 0
            && input.FourDayRestorationRatio >= 0.80m)
        {
            parts.Add(
                "Recent hard training has been functionally fueled: the four-day glycogen restoration ratio is in the green range.");
        }

        return string.Join(" ", parts);
    }

    private static string BuildCoachingTake(
        DayContext context,
        Engine30DashboardAnalysisInput input,
        DashboardAnalysisSignal glycogen,
        DashboardAnalysisSignal protein,
        DashboardAnalysisSignal scale,
        DashboardAnalysisSignal tracking)
    {
        if (!input.CurrentMorningWeightLb.HasValue)
        {
            return
                "Enter this morning's scale readings when convenient. Keep following today's Engine 3.0 plan; do not invent a correction from missing scale evidence.";
        }

        if (tracking.CssClass == InvestigateCss)
        {
            return
                "Keep the current prescription for now, but improve logging precision. Do not recalibrate the engine from one or two low-confidence days.";
        }

        if (scale.CssClass is WatchCss or InvestigateCss)
        {
            return
                "Do not compensate for one unusual morning scale move. Follow today's plan and look for repetition before changing intake.";
        }

        if ((context is DayContext.HardStrength or DayContext.HardConditioning)
            && glycogen.CssClass == GoodCss
            && protein.CssClass == GoodCss)
        {
            return
                "Recovery support was adequate. Follow today's plan without adding a compensatory refeed or restriction solely because of the morning scale.";
        }

        return
            "Follow today's Engine 3.0 prescription and let the multi-day pattern, not a single metric, drive any future adjustment.";
    }

    private static decimal? EstimateRestoredGlycogenG(
        Engine30DashboardAnalysisInput input)
    {
        if (input.GlycogenDemandG <= 0m
            || !input.RestorationRatio.HasValue)
        {
            return null;
        }

        return Math.Round(
            input.GlycogenDemandG
            * Clamp01(input.RestorationRatio.Value),
            0,
            MidpointRounding.AwayFromZero);
    }

    private static decimal? EstimateAssociatedWaterG(
        Engine30DashboardAnalysisInput input)
    {
        var glycogen =
            EstimateRestoredGlycogenG(input);

        if (!glycogen.HasValue)
            return null;

        // Practical educational rule of thumb:
        // approximately 3 g of associated water per 1 g glycogen.
        // This is not a literal scale-decomposition algorithm.
        return Math.Round(
            glycogen.Value * 3m,
            0,
            MidpointRounding.AwayFromZero);
    }

    private static decimal? EstimateGlycogenWaterScaleLb(
        Engine30DashboardAnalysisInput input)
    {
        var glycogen =
            EstimateRestoredGlycogenG(input);

        var water =
            EstimateAssociatedWaterG(input);

        if (!glycogen.HasValue
            || !water.HasValue)
        {
            return null;
        }

        return Math.Round(
            (glycogen.Value + water.Value)
            / 453.59237m,
            1,
            MidpointRounding.AwayFromZero);
    }

    private static string BuildScaleExplanation(
        DayContext context,
        Engine30DashboardAnalysisInput input)
    {
        if (!input.CurrentMorningWeightLb.HasValue)
        {
            return
                "Today's scale is still missing, so yesterday's interpretation is preliminary. Enter this morning's weight and body-composition readings to close the loop.";
        }

        if (!input.PriorMorningWeightLb.HasValue)
        {
            return
                "Today's scale was entered, but there is no prior morning weight available for a one-day comparison.";
        }

        var delta =
            input.CurrentMorningWeightLb.Value
            - input.PriorMorningWeightLb.Value;

        if (delta > 0m
            && (context is DayContext.HardStrength
                or DayContext.HardConditioning))
        {
            var restored =
                EstimateRestoredGlycogenG(input);

            var water =
                EstimateAssociatedWaterG(input);

            var combined =
                EstimateGlycogenWaterScaleLb(input);

            if (restored.HasValue
                && water.HasValue
                && combined.HasValue)
            {
                return
                    $"Do not worry about the {delta:+0.0;-0.0;0.0} lb morning increase by itself. You did not build {Math.Abs(delta):0.0} lb of muscle or add {Math.Abs(delta):0.0} lb of body fat in one day. Engine 3.0 modeled about {restored.Value:0} g of glycogen restoration. Using the practical rule of thumb of about 3 g of associated water per 1 g of stored glycogen, that amount represents roughly {water.Value:0} g of associated water and about {combined.Value:0.0} lb of combined glycogen + water scale mass. The remainder of the one-day change can readily come from normal hydration, sodium, hard-training inflammation, and food/gut content. This is an explanatory estimate, not a precise decomposition of the scale change.";
            }

            return
                $"Do not worry about the {delta:+0.0;-0.0;0.0} lb morning increase by itself. You did not add that amount of fat or muscle overnight. Hard training commonly produces temporary scale increases through glycogen-associated water, inflammation, sodium, hydration, and food/gut content.";
        }

        if (delta < 0m)
        {
            return
                $"The scale moved {delta:0.0} lb this morning. That direction may be consistent with yesterday's energy balance, but a one-day loss is still mostly a mixture of water, glycogen, gut content, and tissue change rather than a direct measurement of fat loss.";
        }

        return
            "Morning weight was essentially unchanged. That is fully compatible with recomp progress; one-day scale stability does not mean nothing happened physiologically.";
    }
    private static DayContext ClassifyDay(
        string? actualWorkoutCodes,
        string? plannedWorkoutCode)
    {
        if (!string.IsNullOrWhiteSpace(actualWorkoutCodes))
        {
            var contexts =
                actualWorkoutCodes
                    .Split(
                        new[] { '|', '+', ',', ';' },
                        StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries)
                    .Select(ClassifySingleCode)
                    .ToArray();

            if (contexts.Length > 0)
            {
                return contexts
                    .OrderByDescending(ContextRank)
                    .First();
            }
        }

        return ClassifySingleCode(plannedWorkoutCode);
    }

    private static DayContext ClassifySingleCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return DayContext.Recovery;

        var c =
            code.Trim().ToUpperInvariant();

        c = c switch
        {
            "RECOVERY_0_RPE2" => "REST",
            "MOBILITY__POSTURE" => "MOBILITY_RECOVERY",
            "ZONE_2" or "Z2_40_RPE4" => "ZONE2_CARDIO",
            "PURE_CARDIO" => "GENERAL_CARDIO",
            "MAJOR_60_RPE8" or "MAJOR_MIXED" => "FULL_BODY_STRENGTH",
            "ACCESSORY_45_RPE7"
                or "ACCESSORY_UPPER"
                or "ACCESSORY_LOWER"
                or "ACCESSORY_MIXED"
                or "OTF_STRENGTH_UPPER"
                or "OTF_STRENGTH_LOWER"
                or "OTF_STRENGTH_TOTAL"
                => "ACCESSORY",
            "ACCESSORY_MIXED_PLUS_CARDIO" => "MIXED_GYM_SESSION",
            "ACC_PLUS_Z2_75_RPE6" => "CUSTOM_COMPOSITE",
            "HIIT_45_RPE9" => "TRUE_HIIT",
            "LONG_MIXED_75_RPE9" => "MIXED_CONDITIONING",
            "OTF_STANDARD" => "ORANGETHEORY",
            "PICKLEBALL_120_RPE6" => "MIXED_CONDITIONING",
            _ => c
        };

        return c switch
        {
            "REST"
                or "MOBILITY_RECOVERY"
                => DayContext.Recovery,

            "EASY_CARDIO"
                or "ZONE2_CARDIO"
                or "WALKING"
                => DayContext.EasyCardio,

            "GENERAL_CARDIO"
                or "RUNNING"
                or "TREADMILL"
                or "CYCLING"
                or "ROWING"
                or "ELLIPTICAL"
                or "STAIR_CLIMBER"
                or "SWIMMING"
                => DayContext.Cardio,

            "ACCESSORY"
                => DayContext.ModerateStrength,

            "MAJOR_LEGS"
                or "MAJOR_PUSH"
                or "MAJOR_PULL"
                or "FULL_BODY_STRENGTH"
                => DayContext.HardStrength,

            "RESISTANCE_CIRCUIT"
                or "RUNNING_INTERVALS"
                or "CYCLING_INTERVALS"
                or "ROWING_INTERVALS"
                or "MIXED_CONDITIONING"
                or "TRUE_HIIT"
                or "ORANGETHEORY"
                or "MIXED_GYM_SESSION"
                or "CUSTOM_COMPOSITE"
                => DayContext.HardConditioning,

            _ => DayContext.Unknown
        };
    }

    private static int ContextRank(DayContext context)
        => context switch
        {
            DayContext.HardConditioning => 6,
            DayContext.HardStrength => 5,
            DayContext.ModerateStrength => 4,
            DayContext.Cardio => 3,
            DayContext.EasyCardio => 2,
            DayContext.Recovery => 1,
            _ => 0
        };

    private static bool SameBroadTrainingPurpose(
        DayContext planned,
        DayContext actual)
    {
        if (planned == actual)
            return true;

        if ((planned is DayContext.HardStrength or DayContext.ModerateStrength)
            && (actual is DayContext.HardStrength or DayContext.ModerateStrength))
        {
            return true;
        }

        if ((planned is DayContext.HardConditioning or DayContext.Cardio or DayContext.EasyCardio)
            && (actual is DayContext.HardConditioning or DayContext.Cardio or DayContext.EasyCardio))
        {
            return true;
        }

        return false;
    }

    private static string NormalizePrimaryCode(string? codes)
    {
        if (string.IsNullOrWhiteSpace(codes))
            return "";

        return codes
            .Split(
                new[] { '|', '+', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToUpperInvariant())
            .FirstOrDefault()
            ?? "";
    }

    private static string HumanizeCodes(string? codes)
        => string.IsNullOrWhiteSpace(codes)
            ? "No completed workout logged"
            : codes.Replace("_", " ");

    private static string DayContextLabel(DayContext context)
        => context switch
        {
            DayContext.HardStrength => "Hard resistance",
            DayContext.HardConditioning => "Hard conditioning",
            DayContext.ModerateStrength => "Moderate resistance",
            DayContext.Cardio => "Cardio",
            DayContext.EasyCardio => "Zone 2 / easy cardio",
            DayContext.Recovery => "Recovery / rest",
            _ => "Unclassified"
        };

    private static DashboardAnalysisSignal Signal(
        string label,
        string cssClass,
        string value,
        string detail)
        => new(
            Label: label,
            CssClass: cssClass,
            Value: value,
            Detail: detail);

    private static string PlainStatus(string css)
        => css switch
        {
            GoodCss => "green / supported",
            WatchCss => "yellow / worth watching",
            InvestigateCss => "orange / investigate",
            ProblemCss => "red / materially off",
            _ => "gray / insufficient evidence"
        };

    private static decimal Clamp01(decimal value)
        => value < 0m
            ? 0m
            : value > 1m
                ? 1m
                : value;

    private static decimal? Difference(
        decimal? current,
        decimal? previous)
        => current.HasValue && previous.HasValue
            ? current.Value - previous.Value
            : null;

    private enum DayContext
    {
        Unknown,
        Recovery,
        EasyCardio,
        Cardio,
        ModerateStrength,
        HardStrength,
        HardConditioning
    }
}

public sealed record DashboardAnalysisSignal(
    string Label,
    string CssClass,
    string Value,
    string Detail);

public sealed record Engine30DashboardAnalysisInput(
    DateOnly ActionDate,
    string? PlanCode,

    string? PlannedWorkoutCode,
    string? ActualWorkoutCodes,
    string? ActualWorkoutSummary,

    decimal? TargetCalories,
    decimal? ActualCalories,
    decimal? TargetProteinG,
    decimal? ActualProteinG,
    decimal? TargetCarbsG,
    decimal? ActualCarbsG,
    decimal? TargetFatG,
    decimal? ActualFatG,

    decimal GlycogenDemandG,
    decimal? RestorationRatio,

    decimal FourDayRestorationRatio,
    short FourDayHardDays,
    short FourDayModerateHardDays,
    short FourDayCarbohydrateEvidenceDays,

    int FourDayNutritionCompleteDays,
    int FourDayPlannedTrainingDays,
    int FourDayCompletedTrainingDays,
    decimal? FourDayAverageProteinG,
    decimal? FourDayAverageFatG,

    decimal WeekEnergyOverageKcal,
    decimal WeekEligibleEnergyCarryoverKcal,

    decimal? CurrentMorningWeightLb,
    decimal? PriorMorningWeightLb,
    decimal? CurrentMorningBodyFatFraction,
    decimal? PriorMorningBodyFatFraction,
    decimal? CurrentMorningMuscleMassLb,
    decimal? PriorMorningMuscleMassLb,

    int FourDayScaleDays,
    decimal? TrendStartWeightLb,
    decimal? TrendEndWeightLb,
    decimal? TrendStartBodyFatFraction,
    decimal? TrendEndBodyFatFraction,
    decimal? TrendStartMuscleMassLb,
    decimal? TrendEndMuscleMassLb);

public sealed record Engine30DashboardAnalysisResult(
    string DayContextCode,
    string DayContextLabel,

    DashboardAnalysisSignal Workout,
    DashboardAnalysisSignal Glycogen,
    DashboardAnalysisSignal Calories,
    DashboardAnalysisSignal Carbohydrate,
    DashboardAnalysisSignal Protein,
    DashboardAnalysisSignal Fat,
    DashboardAnalysisSignal ScaleConsistency,
    DashboardAnalysisSignal TrackingConfidence,

    DashboardAnalysisSignal TrendTraining,
    DashboardAnalysisSignal TrendFueling,
    DashboardAnalysisSignal TrendEnergy,
    DashboardAnalysisSignal TrendProtein,
    DashboardAnalysisSignal TrendFat,
    DashboardAnalysisSignal TrendScale,
    DashboardAnalysisSignal TrendTracking,

    bool HasCurrentMorningScale,
    decimal? WeightChangeLb,
    decimal? BodyFatChangePercentagePoints,
    decimal? MuscleMassChangeLb,
    decimal? FourDayWeightChangeLb,

    decimal? EstimatedRestoredGlycogenG,
    decimal? EstimatedAssociatedWaterG,
    decimal? EstimatedGlycogenWaterScaleLb,
    string ScaleExplanation,

    string? Kudos,
    string YesterdayNarrative,
    string DataQualityNarrative,
    string TrendNarrative,
    string CoachingTake,
    string TrackingCaveat);