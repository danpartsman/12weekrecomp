# 12WR TEST v1010 — Phase 2D.1 Saved Workouts & Daily Entry Integration

## Purpose

Complete the reusable Workout Builder workflow before Phase 2E normalization.

## Behavior

- Daily Entry remains the daily body/recovery/nutrition screen.
- When Detailed Workout Entry is ON, the Training section includes a clear
  **Open Workout Builder** action.
- Actual workout sessions can have an optional user-facing workout name.
- Reusable saved workouts are separate templates.
- A template stores:
  - name;
  - workout type;
  - default duration;
  - exercise order;
  - sets;
  - reps;
  - optional max weight;
  - lb/kg unit;
  - exercise notes.
- Starting a saved workout creates a NEW current workout session for the selected date.
- Starting a template never reopens a completed workout.
- Completed workouts are locked in Workout Builder unless the user explicitly chooses Edit.
- Updating a current workout does not silently alter the reusable template.
- **Update saved workout** is the explicit action that overwrites the linked template.
- Deleting a template does not delete or modify workout history.

## Schema

Migration 033 adds:

- `app.WorkoutSessions.workout_name`
- `app.WorkoutSessions.workout_template_id`
- `app.WorkoutTemplates`
- `app.WorkoutTemplateExercises`

## Engine boundary

This increment changes workout organization and UX only.

Engine 3.0 physiology is unchanged.
Phase 2E remains responsible for Workout Intelligence normalization and canonical Engine handoff.
