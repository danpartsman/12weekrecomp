# v1004 Visual + First-Login Onboarding Upgrade

This patch is designed to install over the already-installed v1004 Profile / Settings / Progress / Evidence build.

## What it adds

- Redesigned public Welcome page based on the approved light/dark mockups.
- Redesigned public Evidence page based on the approved light/dark mockups.
- Responsive light/dark logo and image assets.
- Clickable Welcome poster -> Evidence page.
- Click/tap-to-enlarge Evidence comparison image.
- Simplified Settings wording: “Manage sign-in account.”
- Cleaner whole-number / compact decimal display on Weigh In Stats inputs.
- New authenticated `Onboarding` page explaining:
  - muscle glycogen and carbohydrate use,
  - post-training carbohydrate refill,
  - protein’s role in repair,
  - gluconeogenesis (accurately named and explained),
  - why Recomp uses a controlled deficit rather than chronic severe restriction,
  - how the app uses planned workouts, actual workouts, nutrition, weight/body-composition trends, and recovery inputs to recalculate targets.
- First authenticated account with no Client/Program now routes:
  - `Home/AfterSignIn` -> `Onboarding` -> `Intake` -> `Workout Plan`.
- Existing configured members still route directly to Dashboard after sign-in.
- “How It Works” remains available from authenticated navigation for later review.

## Important physiology wording

The page intentionally does **not** claim that low glycogen automatically destroys muscle or that glycogen creates a fixed 2–3 day “starvation switch.” The underlying idea is kept, but phrased in scientifically defensible terms:

- glycogen is an important fuel source,
- carbohydrate restores depleted glycogen,
- protein supplies amino acids for repair,
- gluconeogenesis can use several substrates including some amino acids,
- sustained low energy availability can cause metabolic/endocrine adaptations,
- Recomp uses rolling training/fueling/recovery data rather than assuming a fixed 72-hour metabolic window.

## Install locally

From PowerShell:

```powershell
cd C:\RecompAzure\web_recomp_v1004
# Unzip this package anywhere convenient, then run its installer.
& "<unzipped-package>\INSTALL_V1004_VISUAL_ONBOARDING.ps1"
```

The installer creates a rollback ZIP before copying files.

## Local validation after install

```powershell
cd C:\RecompAzure\web_recomp_v1004

dotnet restore .\src\WebRecomp.Web\WebRecomp.Web.csproj
dotnet build .\src\WebRecomp.Web\WebRecomp.Web.csproj -c Release --no-restore
.\scripts\06_regression_math_smoke.ps1
```

Do not deploy until build and regression pass.

## Azure Easy Auth note after deployment

The public pages now use static images under `/images/recomp/`. Because the current Easy Auth configuration requires authentication globally, those public image paths must also be added to `globalValidation.excludedPaths` when the patch is deployed.

Preserve the current exclusions and add these exact paths:

- `/images/recomp/recomp-logo-light.png`
- `/images/recomp/recomp-logo-dark.png`
- `/images/recomp/welcome-poster-light.webp`
- `/images/recomp/welcome-poster-dark.webp`
- `/images/recomp/evidence-compare-light.webp`
- `/images/recomp/evidence-compare-dark.webp`

No database migration is required for this patch.
