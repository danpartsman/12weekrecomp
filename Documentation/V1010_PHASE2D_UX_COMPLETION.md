# 12WR TEST v1010 — Phase 2D UX Completion

## Goal

Keep Workout Builder simple while preserving the richer Workout Intelligence
persistence model underneath.

## User experience

When Detailed Workout Entry is enabled:

- The application header shows **Workout Builder**.
- The header link is hidden when Detailed Workout Entry is disabled.
- Exercise search matches exercise name or muscle metadata.
- Search results are compact.
- Matching favorites sort first; favorites and non-favorites are alphabetical.
- The heart is outline when not favorite and red/filled when favorite.
- Clicking the exercise row adds it to the current workout.
- Normal fields are only:
  - Sets
  - Reps
  - Max weight (optional)
- New exercises default to 3 × 10.
- Last saved sets, reps, max weight, and unit become the client's defaults
  the next time that exercise is added.

## Persistence

Migration 032 adds:

- `app.ClientExercisePreferences`
- `app.DetailedWorkoutExercises.max_weight`
- `app.DetailedWorkoutExercises.load_unit_code`

Preferences are client-scoped.

## Max-weight rule

`max_weight` is stored at the exercise-summary level. It is not copied into
each detailed set's `load` field. This avoids treating a single maximum or
representative weight as if every set used that load.

The normal UI creates the requested number of internal set records with the
selected rep count. The richer set schema remains available for future
advanced entry and Workout Intelligence normalization.

## Engine boundary

Engine 3.0 remains unchanged. Workout Intelligence will normalize this workout
data into the existing canonical Engine input contract in the next phase.

## Environment

TEST v1010 only.
No Azure App Service deployment is performed by this installer.
LIVE / production is not touched.
