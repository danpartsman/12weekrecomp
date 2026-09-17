Web Recomp v1004 — Profile / Settings / Progress / Scientific Evidence foundation

Scope of this patch
- Adds authenticated Profile page.
- Adds editable basic profile fields.
- Adds Settings with preferred display name, contact email, and Light/Dark/System theme.
- Keeps sign-in credentials owned by Microsoft Entra / the user's identity provider.
- Adds authenticated Progress page using existing dated Measurements history.
- Calculates display-only fat mass and lean mass when weight + body-fat data exist.
- Adds graph-ready Progress layout.
- Adds progress-photo UI placeholder but DOES NOT enable file storage.
- Adds public Scientific Evidence page and links it from Welcome/public navigation.
- Adds additive v1004 UserAccounts columns for application-facing preferences.

Not included yet
- Actual progress-photo upload/storage. Correct private Blob Storage can incur Azure charges and requires explicit approval first.
- AI photo analysis.
- Admin console (planned v1005).
- My Workouts/deeper adaptive engine (planned v1006).
- Full 12 Week Recomp branding/homepage rewrite. That follows after this structure is validated.

Important public Evidence routing note
App Service Easy Auth currently protects most paths. After this code is built, tested,
and deployed, /Home/Evidence must be added to Easy Auth globalValidation.excludedPaths
so anonymous visitors can open it. Do not change Easy Auth until the local build and
regression tests pass.

Database migration
The migration is additive and backward compatible with the currently running v1003 app:
  database\006_upgrade_schema_v1004_profile_settings.sql

Apply it ONLY after v1004 build + regression pass and before v1004 deployment:
  .\scripts\07_apply_v1004_profile_settings_schema.ps1

Validation order
1. Install patch into C:\RecompAzure\web_recomp_v1004
2. dotnet build .\src\WebRecomp.Web\WebRecomp.Web.csproj
3. .\scripts\06_regression_math_smoke.ps1
4. STOP and report both results before touching Azure/SQL.
