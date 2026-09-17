Web Recomp v1003 — Responsive Navigation + Profile Roadmap Patch

THIS REPLACES the earlier v1003_signout_nav_patch.
Do not install the earlier Sign Out-only patch.

IMPLEMENTED NOW
- Desktop authenticated navigation remains visible.
- Mobile/tablet authenticated navigation collapses into a hamburger menu.
- Menu contains:
  Dashboard
  Workout Plan
  Weigh In Stats
  Workout Actual
  Nutrition
  Sign Out
- Public Welcome page keeps Sign In and Create Account visible.
- Keyboard Escape closes the mobile menu.
- Selecting a menu item closes the mobile menu.

DOCUMENTED FOR LATER
docs/PROFILE_TARGETS_CHECKINS_UPLOADS.md

That roadmap preserves future plans for:
- Profile
- editable/versioned targets
- recurring check-ins
- uploads/progress photos/documents
- coach visibility and multi-client review
- private/secure storage requirements

NO database changes.
NO calculation changes.
NO Azure authentication-setting changes.

INSTALL
Expand-Archive -Path "$env:USERPROFILE\Downloads\v1003_responsive_nav_profile_roadmap_patch.zip" -DestinationPath "C:\RecompAzure\web_recomp_v1003" -Force

Then:
cd C:\RecompAzure\web_recomp_v1003\src\WebRecomp.Web
dotnet build -c Release

Do not deploy until build and regression pass.
