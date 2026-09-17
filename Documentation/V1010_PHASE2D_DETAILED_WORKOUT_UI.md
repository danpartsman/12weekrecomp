# 12WR TEST v1010 — Phase 2D Exercise Library + Detailed Workout UI

## Purpose

Phase 2D exposes the Workout Intelligence persistence layer to the user without
changing Engine 3.0 physiology calculations.

## User-facing capability

When `DetailedWorkoutEntryEnabled` is OFF:
- standard/manual workout entry remains unchanged;
- Detailed Workout and Exercise Library explain that the feature is disabled;
- no detailed workout records are required.

When `DetailedWorkoutEntryEnabled` is ON:
- Exercise Library supports name search over the active imported catalog;
- Detailed Workout selects any saved WorkoutSession for a date;
- sessions created by either Daily Entry or Workout Actual can be detailed;
- exercises can be searched and added;
- sets can record reps, load, lb/kg, RPE, RIR, rest seconds, duration seconds,
  warm-up status, and notes;
- detailed data persists through `IDetailedWorkoutStore`.

## Architectural boundary

Detailed workout data remains attached to the canonical `WorkoutSession`.

Phase 2D does NOT:
- create another WorkoutSession;
- add detailed and manual physiology together;
- modify Engine 3.0 formulas;
- change workout calorie/glycogen calculations;
- change SQL schema;
- deploy to Azure;
- touch LIVE / production.

The future normalized Workout Intelligence -> Engine 3.0 handoff remains a
separate phase so source-selection behavior can be tested explicitly.
