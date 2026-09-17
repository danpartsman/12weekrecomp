# 12WR TEST v1010 — Phase 2C Catalog Provider & Sync

## Objective

Separate exercise-catalog acquisition and synchronization from the web UI,
Workout Intelligence persistence, and Engine 3.0 calculations.

## Architecture

```text
Admin Console / Scheduler
        |
        v
WebRecomp.CatalogSync
        |
        +--> IExerciseCatalogProvider
        |        |
        |        +--> KineticExerciseCatalogProvider
        |        +--> future providers
        |
        +--> IExerciseMediaProvider
        |        |
        |        +--> NONE today
        |        +--> future image/video providers
        |
        +--> IExerciseCatalogStore
                 |
                 v
         WebRecomp.Web SQL adapter
                 |
                 v
              TEST SQL
```

Engine 3.0 is not referenced by CatalogSync and does not consume provider-specific
structures.

## Admin controls

Administrator > Exercise Catalog exposes:

- Exercise catalog provider
- Exercise media provider
- Automatic refresh enabled/disabled
- Refresh interval: 1, 3, 7, 14, or 30 days
- Check for Updates Now
- Last check / last successful sync
- Dataset SHA-256 / provider version
- Recent synchronization history

Defaults are KINETIC, media provider NONE, 7 days, automatic sync OFF.

## Scheduling

The web application hosts CatalogSyncHostedService. It polls once per hour to see
whether the configured refresh interval is due. It does not download provider data
when the schedule is not due.

A SQL-backed lease prevents two web-app instances from running the same catalog
sync concurrently.

## Provider update behavior

Kinetic is fetched using conditional HTTP when an ETag is available. If the
provider returns 304, or the downloaded SHA-256 matches the prior dataset, the
run is recorded as NO_CHANGE and no catalog import occurs.

Changed data follows the existing Workout Intelligence persistence boundary:

KineticExerciseCatalogProvider -> normalized ExerciseCatalogSnapshot ->
IExerciseCatalogStore -> SqlExerciseCatalogStore.

Catalog refresh remains add/update/deactivate. Provider refresh never deletes
12WR-specific intelligence.

## Media readiness

Migration 031 adds ExerciseMediaAssets. Media is deliberately separate from
exercise metadata and supports provider code, source external ID, media type,
URL/local path, license, attribution, ordering, active state, and sync provenance.

No external media provider is enabled in Phase 2C.

## Engine boundary

Engine 3.0 calculations remain unchanged. Detailed workout normalization into
Engine 3.0 remains a later phase.
