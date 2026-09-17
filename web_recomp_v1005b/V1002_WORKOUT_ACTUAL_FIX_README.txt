Web Recomp v1002 — Workout Actual session-management correction

PURPOSE
- Edit an existing saved Workout Actual session without creating a duplicate row.
- Delete an accidentally entered Workout Actual session.
- Add another session cleanly for double-session/split-session days.
- Show friendly workout names in the saved-session list.
- Group and clean the built-in workout selector without changing the database.
- Preserve the planned workout as the normal initial default.
- When '+ Add another workout' is chosen, start with a blank workout selection.
- When a planned workout has a configured default duration/rest, use those defaults.

BUILT-IN WORKOUT SELECTOR CLEANUP
- Groups: Major strength, Accessory strength, Conditioning & cardio,
  Recovery & mobility, Other activity.
- Hides duplicate legacy choices when a cleaner canonical equivalent exists:
  Z2_40_RPE4, ACCESSORY_45_RPE7, MAJOR_60_RPE8, ACC_PLUS_Z2_75_RPE6.
- A hidden legacy workout is still shown if an existing session using it is being edited.
- Friendly display labels remove encoded duration/RPE where appropriate:
  Recovery, HIIT, Long Mixed, Pickleball.

NOT INCLUDED
- No database migration.
- No user-defined custom workout library. That remains planned for v1004.
- No calculation-formula change.
- No engine/configuration-version change.

INSTALL
Extract this ZIP into:
C:\RecompAzure\web_recomp_v1002
and overwrite files.

VALIDATION ORDER
1. cd C:\RecompAzure\web_recomp_v1002\src\WebRecomp.Web
2. dotnet build -c Release
3. cd C:\RecompAzure\web_recomp_v1002
4. .\scripts\06_regression_math_smoke.ps1
5. Do not deploy until both pass.
