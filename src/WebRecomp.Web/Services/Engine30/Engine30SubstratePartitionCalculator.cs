namespace WebRecomp.Web.Services.Engine30;

public static class Engine30SubstratePartitionSourceCodes
{
    public const string HeartRateZones = "HEART_RATE_ZONES";
    public const string RpeFallback = "RPE_FALLBACK";
}

public sealed record Engine30SubstratePartitionBlockResult(
    decimal StartMinute,
    decimal EndMinute,
    decimal GrossEnergyKcal,
    decimal MetabolicIntensityIndex,
    string IntensitySourceCode,
    decimal PhosphagenFraction,
    decimal MuscleGlycogenFraction,
    decimal BloodGlucoseFraction,
    decimal FatFraction,
    decimal ProteinFraction,
    decimal PhosphagenEnergyKcal,
    decimal MuscleGlycogenEnergyKcal,
    decimal BloodGlucoseEnergyKcal,
    decimal FatEnergyKcal,
    decimal ProteinEnergyKcal);

public sealed record Engine30SubstratePartitionSegmentResult(
    string SegmentCode,
    string ModalityCode,
    string TrainingPatternCode,
    string ModelCode,
    decimal DurationMinutes,
    decimal GrossEnergyKcal,
    decimal EstimatedMuscleGlycogenG,
    decimal MuscleGlycogenEnergyKcal,
    decimal BloodGlucoseEnergyKcal,
    decimal FatEnergyKcal,
    decimal ProteinEnergyKcal,
    decimal PhosphagenEnergyKcal,
    IReadOnlyList<Engine30SubstratePartitionBlockResult> Blocks);

public sealed record Engine30SubstratePartitionResult(
    decimal GrossEnergyKcal,
    decimal EstimatedMuscleGlycogenG,
    decimal MuscleGlycogenEnergyKcal,
    decimal BloodGlucoseEnergyKcal,
    decimal FatEnergyKcal,
    decimal ProteinEnergyKcal,
    decimal PhosphagenEnergyKcal,
    decimal MuscleGlycogenEnergyFraction,
    decimal TotalCarbohydrateEnergyFraction,
    IReadOnlyList<Engine30SubstratePartitionSegmentResult> Segments,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Engine 3.0 substrate-partition model.
///
/// PURPOSE
/// -------
/// Separate total workout energy from the fuels/pathways that supplied it.
/// Total workout calories are NOT muscle glycogen.
///
/// The model deliberately keeps five acute energy buckets:
///   - phosphagen / stored ATP-PCr contribution,
///   - muscle glycogen,
///   - circulating blood glucose,
///   - fat,
///   - protein.
///
/// Scientific boundary:
/// - Continuous-exercise anchors are literature-informed from human tracer
///   work showing that muscle glycogen, plasma glucose, plasma FFA and
///   intramuscular triglyceride contributions change with intensity and
///   duration (e.g. Romijn et al., PMID 8214047; review PMID 9740552).
/// - The three ATP-resynthesis systems overlap rather than switching on/off
///   sequentially (Gastin, PMID 11547894).
/// - Resistance work retains the separate muscle/set/biopsy-informed
///   glycogen model. This class supplies an independent whole-session
///   plausibility constraint because resistance energy expenditure is hard
///   to quantify from oxygen consumption alone and contains substantial
///   anaerobic contribution (PMID 23487002; PMID 41420384).
///
/// The published literature does not provide one universal equation that
/// converts RPE into exact substrate fractions for every person and modality.
/// Therefore RPE and HR zones are used as explicit engineering proxies around
/// evidence anchors, never mislabeled as measured VO2 or tracer data.
///
/// v1015a intentionally does not split circulating glucose into liver
/// glycogenolysis, gluconeogenesis, and ingested carbohydrate because the
/// runtime does not yet possess reliable meal-timing / hepatic-state data.
/// Doing so would create false precision.
/// </summary>
public sealed class Engine30SubstratePartitionCalculator
{
    private const decimal KcalPerGramCarbohydrate = 4m;
    private const decimal BlockMinutes = 10m;

    public Engine30SubstratePartitionResult Calculate(
        Engine30ResolvedWorkoutSession session,
        Engine30WorkoutAdminProfile profile,
        Engine30WorkoutEnergyAdapterResult energy)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(energy);

        if (energy.GrossWorkoutEnergyKcal <= 0m ||
            energy.Segments.Count == 0)
        {
            return Zero();
        }

        var configuredSegments =
            profile.Segments
                .Where(x => x.Enabled)
                .ToDictionary(
                    x => Normalize(x.SegmentCode),
                    StringComparer.OrdinalIgnoreCase);

        var runtimeSegments =
            session.Segments
                .ToDictionary(
                    x => Normalize(x.SegmentCode),
                    StringComparer.OrdinalIgnoreCase);

        var results =
            new List<Engine30SubstratePartitionSegmentResult>();

        foreach (var energySegment in energy.Segments)
        {
            var code = Normalize(energySegment.SegmentCode);

            configuredSegments.TryGetValue(code, out var configured);
            runtimeSegments.TryGetValue(code, out var runtime);

            var modelCode =
                Normalize(
                    configured?.GlycogenModelCode
                    ?? profile.GlycogenModelCode);

            var trainingPattern =
                Normalize(
                    configured?.TrainingPatternCode
                    ?? session.TrainingPatternCode);

            var modality =
                Normalize(
                    configured?.ModalityCode
                    ?? runtime?.ModalityCode
                    ?? energySegment.ModalityCode);

            var useWholeSessionMonitor =
                energy.Segments.Count == 1
                && session.MonitorData is not null;

            results.Add(
                CalculateSegment(
                    energySegment,
                    modelCode,
                    trainingPattern,
                    modality,
                    useWholeSessionMonitor
                        ? session.MonitorData
                        : null));
        }

        var gross = results.Sum(x => x.GrossEnergyKcal);
        var glycogenKcal = results.Sum(x => x.MuscleGlycogenEnergyKcal);
        var bloodGlucoseKcal = results.Sum(x => x.BloodGlucoseEnergyKcal);
        var fatKcal = results.Sum(x => x.FatEnergyKcal);
        var proteinKcal = results.Sum(x => x.ProteinEnergyKcal);
        var phosphagenKcal = results.Sum(x => x.PhosphagenEnergyKcal);

        var reasons = new List<string>
        {
            "V1015A_SUBSTRATE_PARTITION_ACTIVE",
            "TOTAL_WORKOUT_ENERGY_NOT_GLYCOGEN_PROXY",
            "MUSCLE_GLYCOGEN_SEPARATED_FROM_BLOOD_GLUCOSE",
            "DURATION_BLOCKS_SHIFT_SUBSTRATE_USE"
        };

        if (results.Any(x =>
                x.Blocks.Any(b =>
                    b.IntensitySourceCode ==
                    Engine30SubstratePartitionSourceCodes.HeartRateZones)))
        {
            reasons.Add("HEART_RATE_ZONE_EVIDENCE_USED");
        }
        else
        {
            reasons.Add("RPE_SUBSTRATE_PROXY_USED");
        }

        if (results.Any(x => x.PhosphagenEnergyKcal > 0m))
            reasons.Add("PHOSPHAGEN_CONTRIBUTION_MODELED");

        if (results.Any(x => x.ProteinEnergyKcal > 0m))
            reasons.Add("SMALL_PROTEIN_CONTRIBUTION_MODELED");

        return new Engine30SubstratePartitionResult(
            GrossEnergyKcal: Round(gross),
            EstimatedMuscleGlycogenG: Round(glycogenKcal / KcalPerGramCarbohydrate),
            MuscleGlycogenEnergyKcal: Round(glycogenKcal),
            BloodGlucoseEnergyKcal: Round(bloodGlucoseKcal),
            FatEnergyKcal: Round(fatKcal),
            ProteinEnergyKcal: Round(proteinKcal),
            PhosphagenEnergyKcal: Round(phosphagenKcal),
            MuscleGlycogenEnergyFraction:
                gross > 0m ? Round(glycogenKcal / gross, 4) : 0m,
            TotalCarbohydrateEnergyFraction:
                gross > 0m
                    ? Round((glycogenKcal + bloodGlucoseKcal) / gross, 4)
                    : 0m,
            Segments: results,
            ReasonCodes: reasons);
    }

    private static Engine30SubstratePartitionSegmentResult CalculateSegment(
        Engine30WorkoutEnergySegmentResult energy,
        string modelCode,
        string trainingPattern,
        string modality,
        Engine30WorkoutMonitorData? monitor)
    {
        if (energy.DurationMinutes <= 0m ||
            energy.Energy.GrossWorkoutEnergyKcal <= 0m)
        {
            return new Engine30SubstratePartitionSegmentResult(
                energy.SegmentCode,
                modality,
                trainingPattern,
                modelCode,
                energy.DurationMinutes,
                energy.Energy.GrossWorkoutEnergyKcal,
                0m, 0m, 0m, 0m, 0m, 0m,
                Array.Empty<Engine30SubstratePartitionBlockResult>());
        }

        var intensity =
            ResolveIntensity(
                energy.SessionRpe,
                monitor);

        var blocks =
            new List<Engine30SubstratePartitionBlockResult>();

        var energyPerMinute =
            energy.Energy.GrossWorkoutEnergyKcal
            / energy.DurationMinutes;

        decimal cursor = 0m;

        while (cursor < energy.DurationMinutes)
        {
            var end =
                Math.Min(
                    energy.DurationMinutes,
                    cursor + BlockMinutes);

            var minutes = end - cursor;
            var midpoint = cursor + minutes / 2m;
            var blockEnergy = energyPerMinute * minutes;

            var shares =
                ResolveShares(
                    modelCode,
                    trainingPattern,
                    modality,
                    intensity.Index,
                    energy.SessionRpe,
                    midpoint,
                    cursor == 0m);

            blocks.Add(
                new Engine30SubstratePartitionBlockResult(
                    StartMinute: Round(cursor),
                    EndMinute: Round(end),
                    GrossEnergyKcal: Round(blockEnergy),
                    MetabolicIntensityIndex: Round(intensity.Index, 4),
                    IntensitySourceCode: intensity.Source,
                    PhosphagenFraction: Round(shares.Phosphagen, 4),
                    MuscleGlycogenFraction: Round(shares.MuscleGlycogen, 4),
                    BloodGlucoseFraction: Round(shares.BloodGlucose, 4),
                    FatFraction: Round(shares.Fat, 4),
                    ProteinFraction: Round(shares.Protein, 4),
                    PhosphagenEnergyKcal: Round(blockEnergy * shares.Phosphagen),
                    MuscleGlycogenEnergyKcal: Round(blockEnergy * shares.MuscleGlycogen),
                    BloodGlucoseEnergyKcal: Round(blockEnergy * shares.BloodGlucose),
                    FatEnergyKcal: Round(blockEnergy * shares.Fat),
                    ProteinEnergyKcal: Round(blockEnergy * shares.Protein)));

            cursor = end;
        }

        var glycogenKcal = blocks.Sum(x => x.MuscleGlycogenEnergyKcal);

        return new Engine30SubstratePartitionSegmentResult(
            SegmentCode: energy.SegmentCode,
            ModalityCode: modality,
            TrainingPatternCode: trainingPattern,
            ModelCode: modelCode,
            DurationMinutes: Round(energy.DurationMinutes),
            GrossEnergyKcal: Round(energy.Energy.GrossWorkoutEnergyKcal),
            EstimatedMuscleGlycogenG: Round(glycogenKcal / KcalPerGramCarbohydrate),
            MuscleGlycogenEnergyKcal: Round(glycogenKcal),
            BloodGlucoseEnergyKcal: Round(blocks.Sum(x => x.BloodGlucoseEnergyKcal)),
            FatEnergyKcal: Round(blocks.Sum(x => x.FatEnergyKcal)),
            ProteinEnergyKcal: Round(blocks.Sum(x => x.ProteinEnergyKcal)),
            PhosphagenEnergyKcal: Round(blocks.Sum(x => x.PhosphagenEnergyKcal)),
            Blocks: blocks);
    }

    private static (decimal Index, string Source) ResolveIntensity(
        decimal rpe,
        Engine30WorkoutMonitorData? monitor)
    {
        var zoneMinutes =
            new[]
            {
                (Minutes: Positive(monitor?.MinutesZone1), Index: 0.45m),
                (Minutes: Positive(monitor?.MinutesZone2), Index: 0.60m),
                (Minutes: Positive(monitor?.MinutesZone3), Index: 0.70m),
                (Minutes: Positive(monitor?.MinutesZone4), Index: 0.82m),
                (Minutes: Positive(monitor?.MinutesZone5), Index: 0.92m)
            };

        var totalZoneMinutes =
            zoneMinutes.Sum(x => x.Minutes);

        if (totalZoneMinutes > 0m)
        {
            var weighted =
                zoneMinutes.Sum(x => x.Minutes * x.Index)
                / totalZoneMinutes;

            return (
                Clamp(weighted, 0.40m, 0.95m),
                Engine30SubstratePartitionSourceCodes.HeartRateZones);
        }

        /*
         * RPE fallback is an operational metabolic-intensity proxy.
         * It is deliberately NOT labeled %VO2max.
         *
         * The mapping is centered so RPE 3 ~= 0.60 and RPE 8 ~= 0.90,
         * which places common Zone-2 and hard-session observations near
         * the human tracer-study regions used by the substrate anchors.
         */
        return (
            Clamp(
                0.42m + rpe * 0.06m,
                0.40m,
                0.95m),
            Engine30SubstratePartitionSourceCodes.RpeFallback);
    }

    private static SubstrateShares ResolveShares(
        string modelCode,
        string trainingPattern,
        string modality,
        decimal intensity,
        decimal rpe,
        decimal elapsedMidpointMinutes,
        bool firstBlock)
    {
        if (modelCode == "ZERO" ||
            trainingPattern == Engine30TrainingPatternCodes.Recovery)
        {
            return NormalizeShares(
                phosphagen: firstBlock ? 0.01m : 0m,
                muscleGlycogen: 0m,
                bloodGlucose: 0.05m,
                fat: 0.94m,
                protein: 0.01m);
        }

        var isResistance =
            modelCode.Contains(
                "RESISTANCE",
                StringComparison.OrdinalIgnoreCase)
            || modality == Engine30WorkoutModalityCodes.Resistance;

        var isInterval =
            modelCode.Contains(
                "INTERVAL",
                StringComparison.OrdinalIgnoreCase)
            || trainingPattern == Engine30TrainingPatternCodes.Intervals
            || trainingPattern == Engine30TrainingPatternCodes.Hiit;

        if (isResistance)
        {
            /*
             * Resistance training is not treated as steady-state cardio.
             * Stored ATP/PCr and anaerobic glycolysis can make large
             * contributions to individual hard sets. These coefficients
             * intentionally remain broad because whole-session resistance
             * calorimetry under-resolves anaerobic energy.
             */
            var resistancePhosphagen =
                Clamp(
                    0.09m + (intensity - 0.60m) * 0.25m,
                    0.10m,
                    0.18m);

            var muscleGlycogen =
                Clamp(
                    0.36m + (intensity - 0.60m) * 0.65m,
                    0.34m,
                    0.60m);

            var bloodGlucose =
                Clamp(
                    0.06m + (rpe - 5m) * 0.005m,
                    0.05m,
                    0.09m);

            var protein = 0.01m;

            return NormalizeShares(
                resistancePhosphagen,
                muscleGlycogen,
                bloodGlucose,
                1m - resistancePhosphagen - muscleGlycogen - bloodGlucose - protein,
                protein);
        }

        if (isInterval)
        {
            var intervalPhosphagen =
                Clamp(
                    0.05m + (intensity - 0.60m) * 0.22m,
                    0.05m,
                    0.13m);

            var muscleGlycogen =
                Clamp(
                    0.40m + (intensity - 0.65m) * 0.55m,
                    0.38m,
                    0.60m);

            var bloodGlucose =
                Clamp(
                    0.08m + (intensity - 0.65m) * 0.12m,
                    0.08m,
                    0.13m);

            var protein =
                EnduranceProteinFraction(
                    elapsedMidpointMinutes);

            var fat =
                1m
                - intervalPhosphagen
                - muscleGlycogen
                - bloodGlucose
                - protein;

            var shifted =
                ApplyDurationShift(
                    muscleGlycogen,
                    bloodGlucose,
                    fat,
                    intensity,
                    elapsedMidpointMinutes,
                    maximumRelativeGlycogenReduction:
                        intensity >= 0.85m ? 0.15m : 0.25m);

            return NormalizeShares(
                intervalPhosphagen,
                shifted.MuscleGlycogen,
                shifted.BloodGlucose,
                shifted.Fat,
                protein);
        }

        var aerobic =
            InterpolateContinuousShares(
                intensity);

        var durationAdjusted =
            ApplyDurationShift(
                aerobic.MuscleGlycogen,
                aerobic.BloodGlucose,
                aerobic.Fat,
                intensity,
                elapsedMidpointMinutes,
                maximumRelativeGlycogenReduction:
                    intensity < 0.75m
                        ? 0.50m
                        : intensity < 0.85m
                            ? 0.35m
                            : 0.20m);

        var proteinTarget =
            EnduranceProteinFraction(
                elapsedMidpointMinutes);

        var proteinIncrease =
            Math.Max(
                0m,
                proteinTarget - aerobic.Protein);

        var fatAfterProtein =
            Math.Max(
                0m,
                durationAdjusted.Fat - proteinIncrease);

        var phosphagen =
            firstBlock
                ? 0.01m
                : 0m;

        return NormalizeShares(
            phosphagen,
            durationAdjusted.MuscleGlycogen,
            durationAdjusted.BloodGlucose,
            fatAfterProtein,
            proteinTarget);
    }

    private static SubstrateShares InterpolateContinuousShares(
        decimal intensity)
    {
        /*
         * Literature-informed anchors.
         *
         * Around moderate intensity the 0.65 anchor intentionally resembles
         * the classic tracer pattern: substantial muscle glycogen, a smaller
         * circulating-glucose contribution, and substantial lipid oxidation.
         * These are model anchors, not claims that every person at a given
         * intensity has an identical measured partition.
         */
        var anchors =
            new[]
            {
                new Anchor(0.35m, 0.04m, 0.06m, 0.89m, 0.01m),
                new Anchor(0.45m, 0.10m, 0.07m, 0.82m, 0.01m),
                new Anchor(0.55m, 0.20m, 0.08m, 0.71m, 0.01m),
                new Anchor(0.65m, 0.38m, 0.08m, 0.53m, 0.01m),
                new Anchor(0.75m, 0.48m, 0.10m, 0.41m, 0.01m),
                new Anchor(0.85m, 0.60m, 0.12m, 0.27m, 0.01m),
                new Anchor(0.95m, 0.66m, 0.14m, 0.19m, 0.01m)
            };

        var clamped =
            Clamp(
                intensity,
                anchors[0].Intensity,
                anchors[^1].Intensity);

        for (var i = 0; i < anchors.Length - 1; i++)
        {
            var left = anchors[i];
            var right = anchors[i + 1];

            if (clamped <= right.Intensity)
            {
                var span =
                    right.Intensity - left.Intensity;

                var t =
                    span <= 0m
                        ? 0m
                        : (clamped - left.Intensity) / span;

                return new SubstrateShares(
                    0m,
                    Lerp(left.MuscleGlycogen, right.MuscleGlycogen, t),
                    Lerp(left.BloodGlucose, right.BloodGlucose, t),
                    Lerp(left.Fat, right.Fat, t),
                    Lerp(left.Protein, right.Protein, t));
            }
        }

        var last = anchors[^1];

        return new SubstrateShares(
            0m,
            last.MuscleGlycogen,
            last.BloodGlucose,
            last.Fat,
            last.Protein);
    }

    private static (
        decimal MuscleGlycogen,
        decimal BloodGlucose,
        decimal Fat)
        ApplyDurationShift(
            decimal muscleGlycogen,
            decimal bloodGlucose,
            decimal fat,
            decimal intensity,
            decimal elapsedMidpointMinutes,
            decimal maximumRelativeGlycogenReduction)
    {
        if (elapsedMidpointMinutes <= 30m)
        {
            return (
                muscleGlycogen,
                bloodGlucose,
                fat);
        }

        /*
         * Human tracer work shows that during prolonged moderate exercise
         * plasma-derived substrate oxidation rises while the relative
         * contribution from muscle glycogen falls. We model that gradually
         * rather than multiplying the first 10 minutes across the entire
         * workout.
         */
        var progression =
            Clamp(
                (elapsedMidpointMinutes - 30m) / 90m,
                0m,
                1m);

        var intensityProtection =
            intensity >= 0.90m
                ? 0.70m
                : 1m;

        var reduction =
            muscleGlycogen
            * maximumRelativeGlycogenReduction
            * progression
            * intensityProtection;

        return (
            MuscleGlycogen:
                Math.Max(
                    0m,
                    muscleGlycogen - reduction),

            BloodGlucose:
                bloodGlucose
                + reduction * 0.20m,

            Fat:
                fat
                + reduction * 0.80m);
    }

    private static decimal EnduranceProteinFraction(
        decimal elapsedMidpointMinutes)
    {
        if (elapsedMidpointMinutes <= 60m)
            return 0.01m;

        var progression =
            Clamp(
                (elapsedMidpointMinutes - 60m) / 60m,
                0m,
                1m);

        return
            0.01m
            + 0.02m * progression;
    }

    private static SubstrateShares NormalizeShares(
        decimal phosphagen,
        decimal muscleGlycogen,
        decimal bloodGlucose,
        decimal fat,
        decimal protein)
    {
        phosphagen = Math.Max(0m, phosphagen);
        muscleGlycogen = Math.Max(0m, muscleGlycogen);
        bloodGlucose = Math.Max(0m, bloodGlucose);
        fat = Math.Max(0m, fat);
        protein = Math.Max(0m, protein);

        var total =
            phosphagen
            + muscleGlycogen
            + bloodGlucose
            + fat
            + protein;

        if (total <= 0m)
        {
            return new SubstrateShares(
                0m,
                0m,
                0m,
                1m,
                0m);
        }

        return new SubstrateShares(
            phosphagen / total,
            muscleGlycogen / total,
            bloodGlucose / total,
            fat / total,
            protein / total);
    }

    private static Engine30SubstratePartitionResult Zero()
        => new(
            GrossEnergyKcal: 0m,
            EstimatedMuscleGlycogenG: 0m,
            MuscleGlycogenEnergyKcal: 0m,
            BloodGlucoseEnergyKcal: 0m,
            FatEnergyKcal: 0m,
            ProteinEnergyKcal: 0m,
            PhosphagenEnergyKcal: 0m,
            MuscleGlycogenEnergyFraction: 0m,
            TotalCarbohydrateEnergyFraction: 0m,
            Segments: Array.Empty<Engine30SubstratePartitionSegmentResult>(),
            ReasonCodes: new[] { "NO_WORKOUT_ENERGY" });

    private static decimal Positive(decimal? value)
        => value.HasValue
            ? Math.Max(0m, value.Value)
            : 0m;

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim().ToUpperInvariant();

    private static decimal Lerp(
        decimal left,
        decimal right,
        decimal t)
        => left + (right - left) * t;

    private static decimal Clamp(
        decimal value,
        decimal minimum,
        decimal maximum)
        => Math.Min(
            maximum,
            Math.Max(
                minimum,
                value));

    private static decimal Round(
        decimal value,
        int decimals = 2)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);

    private sealed record Anchor(
        decimal Intensity,
        decimal MuscleGlycogen,
        decimal BloodGlucose,
        decimal Fat,
        decimal Protein);

    private sealed record SubstrateShares(
        decimal Phosphagen,
        decimal MuscleGlycogen,
        decimal BloodGlucose,
        decimal Fat,
        decimal Protein);
}
