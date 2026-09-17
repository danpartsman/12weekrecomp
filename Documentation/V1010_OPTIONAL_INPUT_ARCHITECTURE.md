# 12WR TEST v1010 - Optional Detailed Input Architecture

## Core rule

**Optional UI, canonical Engine inputs.**

Do not create a simplified Engine and an advanced Engine.

Engine 3.0 consumes normalized daily facts with source provenance, regardless
of whether those facts came from manual entry, detailed 12WR logging, a future
trainer/coach source, or a future external integration.

## Independent user preferences

Conceptual settings:
- DetailedWorkoutEntryEnabled
- NutritionFoodLogEnabled

Initial default: OFF / OFF to preserve inherited v1009 behavior until the user opts in.

## Workout input

Detailed OFF:
- current workout profile/type;
- duration;
- effort/RPE;
- existing actual/planned behavior;
- multiple sessions where supported.

Detailed ON may add:
- exercise;
- sets/reps/load;
- rest;
- RPE/RIR;
- tempo;
- muscle emphasis;
- notes.

Richer detail must normalize into the SAME Engine 3.0 energy, recruitment,
hard-set, glycogen, recovery, and actual-workout contracts.

## Nutrition input

Food log OFF:
- manual calories;
- protein;
- carbohydrate;
- fat.

Food log ON:
- meals/foods/servings aggregate into the same normalized daily totals.

## Anti-double-counting

For a domain/day, exactly one normalized source is authoritative.
Manual and detailed totals are never added together.

Missing detailed data are missing evidence, not zero.

If detailed and manual values materially disagree, reconcile explicitly rather
than silently guessing.

Turning a detailed feature OFF must not delete historical detailed data.

## Required regression matrix

- OFF/OFF manual-only behavior remains GREEN.
- Workout ON / Nutrition OFF.
- Workout OFF / Nutrition ON.
- Both ON.
- ON -> OFF -> ON without data loss.
- Incomplete detail -> intended fallback.
- Manual/detail disagreement -> no double counting.
- Multiple workout sessions -> correct combined energy/glycogen.
- Missed planned workout -> zero actual demand.
- Existing Engine 3.0 calibration/regression gates remain GREEN.
