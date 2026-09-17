Web Recomp v1002 — Vertical Workout Plan Patch

UI-only Workout Plan refinement.

WHAT CHANGES
- Monday through Sunday now display vertically instead of seven horizontal columns.
- Every day is color-coded by the same category logic used by the Workout Actual selector:
  RED    = Major / hard strength
  BLUE   = Accessory strength
  ORANGE = Conditioning / cardio
  GREEN  = Recovery / rest
  SLATE  = Other activity
  GRAY   = Unplanned
- A weekly mix strip at the top counts each category so the user can immediately see
  how many hard, accessory, cardio, recovery/rest, other, and unplanned days are present.
- The Workout Plan dropdown itself is grouped into the same categories.
- Category colors and counts update immediately when a selection changes.
- Macro/fuel targets remain the saved/recalculated values; if a workout selection is changed,
  the row displays "Save the week to recalculate this day's targets."

NO DATABASE CHANGES.
NO CALCULATION CHANGES.
NO RECOMMENDATION CHANGES.
NO DASHBOARD CHANGES.

IMPORTANT
This patch's site.css is based on the latest Dashboard-balance CSS, so installing it will NOT
revert the final Dashboard layout.

INSTALL
Extract over:
C:\RecompAzure\web_recomp_v1002

Then build:
cd C:\RecompAzure\web_recomp_v1002\src\WebRecomp.Web
dotnet build -c Release

Do not deploy until the build passes.
