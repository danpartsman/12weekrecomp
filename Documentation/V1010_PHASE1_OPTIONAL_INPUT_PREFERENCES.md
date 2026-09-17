# 12WR TEST v1010 - Phase 1 Optional Input Preference Foundation

## Scope

This phase establishes the user-controlled optional-input switches without
changing the frozen Engine 3.0 physiological calculations.

## Storage decision

The switches are stored on the active 12-week Program, not on UserAccount.

Reason:
- a future coach/trainer may manage multiple clients;
- one account-level preference must not leak across client programs;
- the preference describes how this specific program collects observed inputs.

Fields:
- DetailedWorkoutEntryEnabled
- NutritionFoodLogEnabled

Defaults: OFF / OFF.

## Historical source rule

The current preference controls which entry UI is offered for new/current data.
It does not retroactively change the authoritative source of historical facts.

Historical rows retain source provenance using EntrySourceCode.

## Nutrition warning

NutritionDays currently provides one canonical row per program/date. Detailed
food logging must not overwrite the only copy of manual totals. A later v1010
phase must preserve raw manual nutrition separately before FOOD_LOG becomes an
authoritative canonical source.

## Workout direction

WorkoutSessions already supports multiple sessions per day. Detailed
exercise/set rows will attach beneath a WorkoutSession and normalize into the
same Engine 3.0 workout-session contract.

## Compatibility

OFF/OFF must preserve v1009 manual behavior.
No Engine 3.0 coefficient or physiological formula changes are part of Phase 1.


## Engine 3.0 versioning discipline

Engine 3.0 remains inside each self-contained application snapshot under the stable internal Engine30 path. Engine diagnostics derive the repository root from `$PSScriptRoot` and validate `web_recomp_v####`; they do not hard-code v1009/v1010 paths. Engine tests are feature-named rather than application-version-named so they carry forward unchanged into future TEST versions.
