Web Recomp v1003 — Identity / Coach Correction Patch

This patch CORRECTS the previously extracted v1003_identity_oid_patch.

WHY
The previous identity patch enforced exactly one active client assignment for every user.
That conflicts with the planned coach model, where a coach may have access to many clients.

NEW BEHAVIOR
- A user's one OWNER profile is preferred for normal member pages.
- The same account may also have any number of COACH/non-owner client assignments.
- If an account has no OWNER and exactly one client assignment, the existing pages may use it.
- If an account has multiple non-owner clients, the app does NOT silently choose one.
  The future coach workflow will require explicit client selection.
- Multiple OWNER profiles remain an error because that indicates ambiguous ownership data.

ALSO ADDED
docs/COMMERCIAL_COACH_PRODUCT_PATH.md

That document records:
- test-vs-production strategy
- free invited testers
- trials / complimentary / coach-sponsored / paid access concepts
- coach multi-client model
- invitation workflow
- coach product selling points
- future commercial/version path

INSTALL
Extract this patch over:
C:\RecompAzure\web_recomp_v1003

It intentionally overwrites the AppUserContextService.cs installed by the prior patch.

Then build:
cd C:\RecompAzure\web_recomp_v1003\src\WebRecomp.Web
dotnet build -c Release

No database schema changes.
No calculation changes.
Do not deploy until build and regression pass.
