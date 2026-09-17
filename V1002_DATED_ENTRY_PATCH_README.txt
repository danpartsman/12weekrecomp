Web Recomp v1002 dated-entry repair

Extract this ZIP into:
C:\RecompAzure\web_recomp_v1002

Allow Windows to merge folders and replace the files in this patch.

This patch changes only:
- navigation/page labels
- Nutrition dated load/save workflow
- Weigh In Stats dated load/save workflow
- Workout Actual dated load/save workflow and saved-session display

It does NOT change:
- calculation formulas
- SQL schema
- recommendation engine version
- active configuration
- authentication settings

After extraction:
1. cd C:\RecompAzure\web_recomp_v1002\src\WebRecomp.Web
2. dotnet build -c Release
3. Do not deploy until the build and regression tests pass.
