12 Week Recomp v1005a - visual cleanup patch

Changes:
- Required-field asterisks now stay on the same line as the field label, with a small gap.
- Applied consistently to Daily Weigh-In, Daily Nutrition, Daily Workout, Intake, Profile Edit, and Settings.
- Removed the redundant Evidence-page Back to Welcome control; the standard public navigation remains.
- Replaced corrupted Evidence-page arrow/apostrophe/dash/close symbols with HTML entities.
- Replaced corrupted footer bullets with HTML entities.
- Authentication behavior was not changed.

Before deployment:
1. dotnet build .\src\WebRecomp.Web\WebRecomp.Web.csproj -c Release
2. powershell -ExecutionPolicy Bypass -File .\scripts\06_regression_math_smoke.ps1
