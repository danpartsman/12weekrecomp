# v1002 provenance foundation

The v1001 schema already preserves dated measurements, check-ins, planned workouts,
completed workouts, nutrition, analysis runs and recommendation snapshots. v1002 keeps
that model and adds only the missing import identity needed for future integrations.

## Source fields

- Measurements: existing `measurement_source_code` + new `source_record_id`
- WorkoutSessions: new `entry_source_code` + new `source_record_id`
- NutritionDays: existing `entry_source_code` + new `source_record_id`
- PlannedWorkouts: existing `source_code` remains unchanged

Manual records leave `source_record_id` null. Imported records will eventually use stable
provider codes such as `HEALTH_CONNECT`, `HEALTHKIT`, `LOSE_IT`, `WITHINGS`, `GARMIN`,
or `SAMSUNG_HEALTH`, plus the provider's stable record identifier.

Filtered unique indexes make future synchronization idempotent: the same external record
cannot be imported twice for the same client/source.

DailyCheckins are intentionally not assigned one blanket external source in v1002 because
a single check-in can combine manual energy/soreness with sleep, resting-HR or steps from
different future sources. Field-level/import-observation provenance should be introduced
with the integration layer rather than mislabeling the entire check-in row.
