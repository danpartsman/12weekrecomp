Web Recomp v1003 — Public Welcome / Authentication Entry Patch

PURPOSE
Adds the public entry experience before Easy Auth enforcement is enabled.

WELCOME PAGE
- Public description of Web Recomp.
- Sign In button.
- Create Account button.
- Clear private-test notice: invited testers are not charged.
- Product explanation using Planned -> Recomp -> Actual.
- Coach-enabled future positioning without adding payment complexity.

AUTHENTICATION ROUTING
Sign In:
  /.auth/login/aad
    -> /Home/AfterSignIn
      -> existing Client + Program = Dashboard
      -> no completed client/program = Intake

Create Account:
  /.auth/login/aad
    -> /Intake
      -> new authenticated tester completes Intake
      -> existing client is automatically redirected to Dashboard by IntakeController

IMPORTANT
This patch does NOT enable App Service Authentication.
The public Welcome page must be added to Easy Auth excluded paths before platform
authentication enforcement is enabled.

Required public/excluded resources for the next Azure configuration step:
- /
- /Home
- /Home/Index
- /css/site.css
- /health
- /api/system/status

The .auth login routes are App Service authentication routes and are handled by Easy Auth.

NO database schema changes.
NO calculation changes.
NO payment processing.
NO entitlement enforcement yet.

INSTALL
Run:
Expand-Archive -Path "$env:USERPROFILE\Downloads\v1003_public_welcome_patch.zip" -DestinationPath "C:\RecompAzure\web_recomp_v1003" -Force

Then:
cd C:\RecompAzure\web_recomp_v1003\src\WebRecomp.Web
dotnet build -c Release

Do not deploy or enable Easy Auth until build and regression pass.
