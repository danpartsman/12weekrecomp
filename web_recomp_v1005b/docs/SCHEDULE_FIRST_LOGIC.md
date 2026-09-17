# v1001 schedule-first logic

## Weekly structure

A week is `GOOD` when:

- Major Push count = 1
- Major Pull count = 1
- Major Legs count = 1
- Light strength count >= 1
- Primary conditioning count between 1 and 2
- Recovery count >= 1

The validator returns specific notes for missing or excessive categories.

## Workout classification

Dedicated heavy:
- Major Push
- Major Pull
- Major Legs

Light strength:
- Accessory 45/RPE7
- Acc+Z2 75/RPE6
- Accessory Upper
- Accessory Lower
- Accessory Mixed
- Accessory Mixed + Cardio
- OTF Strength Upper
- OTF Strength Lower
- OTF Strength Total

Primary conditioning:
- HIIT 45/RPE9
- Long Mixed 75/RPE9
- Pickleball 120/RPE6
- OTF Standard
- Pure Cardio

Recovery:
- Recovery 0/RPE2
- Mobility / Posture

Recovery cardio:
- Z2 40/RPE4
- Zone 2

Recovery cardio does not satisfy the primary-conditioning quota.

## Schedule-first decision order

1. Start with the dated planned workout.
2. Apply safety overrides:
   - pain/injury today
   - illness/systemic fatigue today
3. If the planned workout is a dedicated heavy day, verify that its region is recovered.
4. Calculate workout-specific glycogen readiness.
5. Only `Very low` readiness can override a planned high-fuel workout automatically.
6. Otherwise keep the planned workout.
7. Recent hard-session count can raise the functional restoration target and generate a warning, but does not by itself replace a planned heavy workout.

## Regional recovery

Recent adjusted region loads follow the spreadsheet model:

- Push: sum last 2 completed days; recovered if < 1.50
- Pull: sum last 2 completed days; recovered if < 1.50
- Legs: sum last 3 completed days; recovered if < 1.75
- Cardio: sum last 2 completed days; recovered if < 4.00

Adjusted load:

`base region load × duration factor × RPE/intensity factor`

Duration factor is clamped 0.50–1.50.

RPE factor is clamped 0.75–1.25 relative to the workout's default intensity factor.

## Planned glycogen demand

`duration × raw glycogen g/min × (weight / 210)^0.5 × intensity factor × 0.65`

Round to the configured 5 g increment.

## Actual glycogen demand

Actual duration, RPE and rest are used when present. Rest/density adjustment is applied for sessions with fuel score >= 2.

## Credited restoration

For each completed day:

`credit = min(day glycogen demand, max(0, actual carbs - base rest carbs) + min(actual carbs, base rest carbs) × 20%)`

Missing nutrition is unknown and is not treated as zero.

## Four-day restoration

Use the latest four completed calendar days before today's action date.

`absolute restoration = total credited restoration / total glycogen demand`

If there was no glycogen demand, readiness is adequate.

Functional target:

`workout planned restoration % + 5 percentage points when >=3 moderate/hard days, +10 points when >=4; capped at 90%`

Target achievement:

`absolute restoration / functional target`

Basic classification:

- >= 100%: Adequate
- 85–99%: Slightly reduced
- 70–84%: Moderately low
- 50–69%: Low
- < 50%: Very low

## Carryover allocation

Calculate the grams needed to move the rolling window toward the functional target, then cap by workout fuel score.

This allocation is added only when needed; it is not a requirement to replace 100% of estimated glycogen use.

## Fuel timing

Heavy:
- protect the workout
- place the carryover portion before training
- approximately 25–35% of daily carbohydrate 2–4 hours before
- approximately 25–35% within 4 hours after

Light strength:
- keep protein distributed through the day
- place roughly 20–30% of carbohydrate around training

Conditioning:
- roughly 20–30% of carbohydrate 2–3 hours before
- normal protein/carbohydrate meal after

Recovery:
- normal meal timing
- use the day to prepare for the next heavy session
