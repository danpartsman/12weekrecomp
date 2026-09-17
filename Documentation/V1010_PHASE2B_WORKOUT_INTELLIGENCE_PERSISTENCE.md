# 12WR TEST v1010 — Phase 2B Workout Intelligence Persistence

Phase 2B adds SQL persistence behind the `WebRecomp.WorkoutIntelligence` internal API boundary.

```text
Kinetic/open catalog
    ↓
WebRecomp.WorkoutIntelligence contracts
    ↓
IExerciseCatalogStore / IDetailedWorkoutStore
    ↓
WebRecomp.Web SQL/EF adapters
    ↓
TEST Azure SQL
```

Engine 3.0 remains disconnected from detailed exercise/set rows in this phase.

Tables:
- app.ExerciseCatalogImports
- app.ExerciseCatalogItems
- app.ExerciseMuscleTargets
- app.ExerciseEquipmentRequirements
- app.DetailedWorkoutExercises
- app.DetailedWorkoutSets

Detailed workout rows are children of the existing WorkoutSession, so multiple workouts per day remain naturally supported.

Catalog refreshes are non-destructive: add new exercises, update existing exercises, mark missing source exercises inactive, and retain historical references.

The existing WorkoutSession summary remains the only canonical Engine 3.0 input until a later explicit normalization adapter is enabled.
