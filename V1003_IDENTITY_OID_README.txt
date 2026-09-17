Web Recomp v1003 — Entra OID Identity Hardening

Purpose
- Read the authenticated Microsoft Entra object ID (oid) explicitly from
  X-MS-CLIENT-PRINCIPAL when Easy Auth is enabled.
- Fall back to X-MS-CLIENT-PRINCIPAL-ID only when the oid claim is unavailable.
- Accept only the AAD identity provider when an authenticated provider header is present.
- Preserve the existing development-user fallback while
  WebRecomp__AllowDevelopmentUser=true.
- Reject inactive UserAccounts.
- Enforce the v1003 rule of exactly one active UserClientAccess per account.
- Keep first-login UserAccount creation behavior for valid authenticated users.

No database schema changes.
No workout/nutrition/recommendation calculation changes.
No Azure authentication settings are changed by this patch.

Install over:
C:\RecompAzure\web_recomp_v1003

Then:
cd C:\RecompAzure\web_recomp_v1003\src\WebRecomp.Web
dotnet build -c Release

Do not enable Easy Auth enforcement until this build passes.
