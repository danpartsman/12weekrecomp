# 12WR TEST v1010 - Current Changelog

Environment: TEST
Active source: C:\RecompAzure\web_recomp_v1010
Baseline: frozen TEST v1009
Baseline SHA-256: 879180ABA5C21C3993901DF0A329EE0B1F033FF0BFAB12BC17E18F1CC5F68DDB
Inherited Engine: 3.0 / CFG-3000.0
LIVE / PRODUCTION: 2.3 - protected

## Initialization

- v1009 freeze accepted before v1010 initialization.
- v1010 working copy inherited from frozen v1009.
- Application metadata set/verified as v1010.
- Engine 3.0 metadata preserved.
- Detailed Workout Entry is optional.
- First-Party Nutrition/Food Log is optional.
- Manual workout and manual daily macro entry remain full supported paths.
- All sources normalize into one Engine 3.0 pipeline.

## Phase 1 - Optional input preference foundation

- Added program-scoped DetailedWorkoutEntryEnabled and NutritionFoodLogEnabled.
- Defaults remain OFF/OFF for inherited/manual behavior.
- Added Settings UI controls for the active program.
- Added Daily Entry projection flags for future detailed-input UI gating.
- Added centralized RecompInputSourceCodes provenance constants.
- Added additive TEST schema script 029_v1010_optional_input_preferences.sql.
- Made Engine 3.0 diagnostic scripts repository-version agnostic; they derive the repository root from their own script location.
- No Engine 3.0 physiological coefficient/formula changes.
- No detailed historical data is deleted when a preference changes.
- Nutrition detailed-entry implementation must preserve raw manual totals before
  FOOD_LOG becomes authoritative for a canonical day.


- Engine 3.0 diagnostic scripts made repository-version agnostic; no hard-coded v1009/v1010 source paths.
- New optional-input test renamed to a version-independent feature test so it carries forward into future TEST versions.
- Multi-day Engine 3.0 gate labels no longer identify themselves as v1009.


## Phase 2A — Workout Intelligence Boundary

- Added standalone `WebRecomp.WorkoutIntelligence` project.
- Added versioned internal contract `workout-intelligence/v1`.
- Added Kinetic JSON adapter behind 12WR catalog models.
- Added local snapshot refresh/provenance script; no hosted API runtime dependency.
- Added independent Workout Intelligence tests.
- Registered the gateway in Web DI without changing Engine 3.0 calculations.
- SQL/database unchanged in Phase 2A.


## Phase 2B — Workout Intelligence persistence boundary
- Added persistence contracts to `WebRecomp.WorkoutIntelligence`.
- Added SQL/EF adapters in `WebRecomp.Web`; Workout Intelligence stays EF-free.
- Added global exercise catalog/import/muscle/equipment entities.
- Added detailed workout exercise/set entities linked to existing WorkoutSession.
- Catalog refresh policy is upsert + deactivate, not destructive delete.
- Existing WorkoutSession remains canonical Engine 3.0 input.
- Added migration `030_v1010_workout_intelligence_persistence.sql`.
- No Engine 3.0 calculation changes.

## Phase 2C — Catalog Provider & Sync

- Added standalone `WebRecomp.CatalogSync` project.
- Added `IExerciseCatalogProvider` internal provider contract.
- Added Kinetic provider adapter using conditional HTTP and SHA-256 change detection.
- Added media-provider contract and media-ready persistence without enabling a media source.
- Added SQL-backed catalog-sync settings, history, and cross-instance lease support.
- Added Administrator Exercise Catalog page with provider, refresh frequency, auto-sync, and Refresh Now controls.
- Added hourly hosted due-check; automatic sync defaults OFF and interval defaults to 7 days.
- Retired the planned one-off catalog importer path; initial and recurring loads use the same coordinator.
- Added migration `031_v1010_catalog_provider_sync.sql` (not applied by source installer).
- Engine 3.0 calculations remain unchanged.
