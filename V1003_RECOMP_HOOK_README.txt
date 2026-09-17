Web Recomp v1003 — Welcome Page Recomp Hook Patch

PURPOSE
Refines the public Welcome page so the primary selling proposition is BODY RECOMPOSITION,
not generic tracking.

CORE MESSAGE
Recomp connects:
- resistance-training stimulus
- protein intake
- total energy balance
- glycogen/carbohydrate fueling
- recovery
- actual versus planned behavior
- body-composition trends

The program uses those inputs to make daily training and nutrition decisions intended to
support muscle growth/preservation while reducing body fat.

The wording deliberately avoids promising guaranteed simultaneous muscle gain and fat loss.
It explains the physiology and decision logic in language that is understandable to a
general user while remaining defensible to nutrition and fitness professionals.

FILES CHANGED
src/WebRecomp.Web/Views/Home/Index.cshtml

NO database changes.
NO calculation changes.
NO authentication changes.
NO CSS changes.

INSTALL
Expand-Archive -Path "$env:USERPROFILE\Downloads\v1003_recomp_hook_patch.zip" -DestinationPath "C:\RecompAzure\web_recomp_v1003" -Force

Then:
cd C:\RecompAzure\web_recomp_v1003\src\WebRecomp.Web
dotnet build -c Release

Do not deploy until build and regression pass.
