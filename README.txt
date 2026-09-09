v1006 Body Composition / Progress Update
========================================

Application-wide changes:

DAILY ENTRY
-----------
Body + Recovery fields are now ordered around recomposition:

1. Weight
2. Body Fat %
3. Skeletal Muscle (lb) - preferred muscle metric
4. Subcutaneous Fat %
5. Visceral Fat Rating
6. Body Water
7. Waist - secondary
8. Hips - secondary

Generic "Muscle Mass" is no longer a normal manual-entry field.
The underlying MuscleMassLb database field is retained for old/provider data.

PROGRESS
--------
The Progress page is redesigned around the program's actual outcomes:

#1 Fat Mass
#2 Skeletal Muscle
#3 Body Weight

Supporting metrics:
- Body Fat %
- Subcutaneous Fat %
- Visceral Fat
- Body Water

Waist is moved to Additional Body Metrics.

MUSCLE METRIC RULE
------------------
Progress does NOT calculate muscle as lean mass minus water.

It prefers SkeletalMuscleMassLb when enough skeletal-muscle data exists for a
trend. For legacy/provider histories that do not yet contain skeletal muscle,
it temporarily falls back to MuscleMassLb and labels that clearly.

INTEGRATION READINESS
---------------------
No database migration is required.

The existing Measurement model already contains:
- SkeletalMuscleMassLb
- MuscleMassLb
- SubcutaneousFatPct
- VisceralFatRating
- BodyWaterLb
- MeasurementSourceCode
- SourceRecordId

That remains suitable for future scale integrations.

INSTALL
-------
Extract over:

C:\RecompAzure\web_recomp_v1006

Run:

powershell -ExecutionPolicy Bypass `
  -File .\scripts\19_apply_v1006_progress_bodycomp_update.ps1

Then:

dotnet restore .\src\WebRecomp.Web\WebRecomp.Web.csproj

dotnet build .\src\WebRecomp.Web\WebRecomp.Web.csproj `
  -c Release `
  --no-restore

powershell -ExecutionPolicy Bypass `
  -File .\scripts\06_regression_math_smoke.ps1

Deploy only after build + regression pass.
