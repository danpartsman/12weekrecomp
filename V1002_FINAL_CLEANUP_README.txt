Web Recomp v1002 FINAL cleanup — workout semantics and Workout Actual layout

THIS PATCH SUPERSEDES THE PREVIOUS v1002 WORKOUT-ACTUAL FIX.
Apply it to the current v1002 project that already contains the Option 4 Dashboard.

WHAT THIS CORRECTS

1. One intended-workout concept
   - Workout Plan is now the single source of truth for the workout the user intends to do.
   - Weigh In Stats no longer has a second Workout Selection / Today's Intention control.
   - Daily check-in still supplies sleep, HR, energy, soreness, pain/injury, illness, and notes.
   - Saving Weigh In Stats clears any old legacy intended_workout_type_code value.
   - No database migration is required; the old column is retained for compatibility/history.

2. Recommendation baseline
   - Recomp now evaluates the Workout Plan directly.
   - If guardrails do not require a change, the recommendation is Keep <planned workout>.
   - If pain, illness, regional recovery, or very-low fuel readiness requires a change,
     macros are calculated from the Recomp-recommended workout.
   - user_selected_workout_type_code is no longer populated by the normal v1002 flow.

3. Actual-workout glycogen semantics remain intact
   - Workout Actual sessions calculate and store actual workout calories, glycogen demand,
     and push/pull/legs/cardio exposure from workout type + duration + rest + RPE.
   - Only sessions with Completed checked contribute to the rolling prior-day workload.
   - The current v1002 recommendation remains the prescription snapshot; the full automatic
     downstream recalculation pipeline after every input edit remains planned for v1004.

4. Dashboard workout order
   ACTUAL
   -> PLANNED
   -> RECOMP RECOMMENDATION
   -> WHY / rationale

5. Workout Actual page layout
   - Primary Actual Workout form is now above the saved/additional-workout section.
   - Edit/Delete/Add Another functionality remains unchanged.
   - Additional/saved workouts appear below the main entry form.

NO DATABASE CHANGE.
NO ENGINE VERSION CHANGE.
NO CUSTOM USER WORKOUT LIBRARY (planned for v1004).

VALIDATION
1. Extract into C:\RecompAzure\web_recomp_v1002 with overwrite.
2. Build Release.
3. Run scripts\06_regression_math_smoke.ps1.
4. Do not deploy unless both pass.
