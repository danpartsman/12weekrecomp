Web Recomp v1002 — Final Option 4 Visual Polish

SUPERSEDES:
- v1002_compact_workout_card_patch.zip
Do not install that patch separately.

CHANGES
1. Dashboard Workout
   PLANNED (blue) -> RECOMP (purple) -> ACTUAL (green)
   Equal-size peer tiles.
   Recomp displays "Keep Plan" unless it actually recommends a change.
   WHY is one compact line below.
   Removes redundant planned description/time/RPE from the Dashboard card.

2. Recommendation wording
   Ordinary recommendations use recovery, recent workload, and fuel status.
   Pain/injury or illness wording appears only when those inputs are actually relevant.

3. Site-wide Option 4 look and feel
   - Workout Plan
   - Weigh In Stats
   - Workout Actual
   - Nutrition
   - Intake
   - Dashboard harmonization
   Includes numbered accent cards, consistent inputs/buttons/statuses, and mobile styling.

4. Workout Actual
   Keeps Edit / Delete / Add another functionality.
   Main actual-workout form remains above saved/additional sessions.

NO DATABASE CHANGE.
NO CALCULATION FORMULA CHANGE.
NO GLYCOGEN LOGIC CHANGE.
NO ENGINE/CONFIG VERSION CHANGE.
NO USER-DEFINED WORKOUT LIBRARY (planned for v1004).

INSTALL
Extract over:
C:\RecompAzure\web_recomp_v1002

VALIDATE
cd C:\RecompAzure\web_recomp_v1002\src\WebRecomp.Web
dotnet build -c Release

Then:
cd C:\RecompAzure\web_recomp_v1002
.\scripts\06_regression_math_smoke.ps1

Do not deploy until both pass.
