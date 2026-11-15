# Flexible nutrition, training and scale interpretation

Baseline LIVE source: 21d330a89c048ae33e4f4d0eb15448d6d7c2eda3.
Preserved on TEST before promoting these changes; TEST database and settings remain isolated.

## Behavior

- Protein's configured amount becomes the upper end of an 80–100% planning range; meal allocation aims at 90%, with no obligatory top-up. Fat is not added to use leftover calories.
- Fat planning uses 20–35% of prescribed energy; adherence uses the average across known completed days. Carbohydrate ±20% is an explicit planning tolerance, not a scientifically measured boundary. Daily carbohydrate deviations no longer deduct score points.
- V7 scoring uses aggregate completed-week modeled glycogen and calorie work, resistance-session coverage, and recovery. Moved sessions and substitutions count. A recorded rest day satisfies weekly recovery. Resistance-session count is a proxy, not proof of all-muscle coverage. Previous saved scores preserve their original components and version.
- A four-day scenario model estimates available glycogen as a range, including replenishment on rest days. Missing nutrition/training evidence pauses adjustments. Adjustments are capped at 25% of baseline carbohydrate, respect the carb floor and existing energy headroom, and never compound an existing calorie carryover correction or override coach prescriptions.
- The fuel range is not measured glycogen, a statistical confidence interval, a validated personal physiological threshold, or a fee calculation. Simplified capacity, starting-state and input-uncertainty assumptions are disclosed in the UI. Background use is approximated by the engine base-rest carbohydrate reference. All intake remains recorded in energy balance. No fat-to-glycogen credit is made.
- Weekly composition compares seven completed days with the seven before them. Newest-first weights: .20/.18/.16/.14/.12/.11/.09, renormalized per available metric. Monthly composition uses straight seven-day averages ending 30 days apart. Today is excluded from both.
- Comparisons display coverage, weight context and descriptive daily variation. Fewer than four readings per period is a sparse-data reporting rule, not a biological threshold. Changes are neutral, estimated and unconfirmed. No invented weight correction, generic least-significant-change threshold or glycogen-based BIA correction is applied. Skeletal and generic muscle are never mixed.
- No raw measurement, Fitdays account, coach agreement, ledger assessment or financial fee rule is changed.

## Evidence and limits

Protein: https://bjsm.bmj.com/content/early/2018/01/18/bjsports-2017-097608
Adult fat reference: https://www.nationalacademies.org/read/10925/chapter/25
Glycogen physiology: https://pmc.ncbi.nlm.nih.gov/articles/PMC6019055/
Consumer BIA reliability and multi-day monitoring: https://pmc.ncbi.nlm.nih.gov/articles/PMC10404482/
Consecutive-day biological variation: https://pubmed.ncbi.nlm.nih.gov/33186896/

These sources support ranges, continuity of fuel storage and cautious interpretation. They do not validate this application's precise capacity coefficients, adjustment caps, scoring weights or personal tissue estimates. Outcomes should be reviewed using standardized measurements, waist and performance over multiple weeks.
