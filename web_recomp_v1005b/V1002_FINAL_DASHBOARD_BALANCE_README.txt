Web Recomp v1002 — Final Dashboard Balance Patch

Incremental UI-only patch for the deployed v1002 Option 4 visual build.

TODAY / WORKOUT
- Vertical sequence with whitespace and NO divider lines:
  PLANNED
  RECOMP RECOMMENDATION
  ACTUAL
- Planned shows:
  workout name
  planned minutes + RPE/effort
  one concise description line
- Recomp shows "Keep Plan" when unchanged.
- If Recomp changes the plan, it shows "Change to <workout>" plus one concise WHY line.
- Actual remains underneath and uses the existing actual-workout summary,
  which already includes duration and logged RPE.

TOMORROW'S PLAN
- Tomorrow now truly repeats Today's visual language:
  Workout = blue card
  Nutrition = green card
  Coach = purple card
- No amber Tomorrow content card.
- Tomorrow heading is neutral with the Preliminary status.
- Tomorrow's three cards use the same Dashboard panel styles as Today's sections.

NO database changes.
NO calculation changes.
NO recommendation-logic changes.
NO glycogen or macro changes.

INSTALL
Extract over:
C:\RecompAzure\web_recomp_v1002

Then:
cd C:\RecompAzure\web_recomp_v1002\src\WebRecomp.Web
dotnet build -c Release

Do not deploy until the local build passes.
