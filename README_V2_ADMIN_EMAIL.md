# 12 Week Recomp LIVE v2 - Admin + Email

Adds to the existing LIVE prelaunch site:

- `/admin` protected by Azure App Service Authentication (Microsoft Entra / Easy Auth headers). Admin traffic is intentionally kept on the direct Azure hostname so the Easy Auth login/cookie flow is not broken by the Cloudflare proxy.
- Admin signup counts.
- `/admin/interests` searchable/filterable signup list and CSV export.
- `/admin/send` audience-targeted update emails with preview and per-recipient unsubscribe links.
- `/unsubscribe` preference page.
- Automatic welcome/confirmation email on first signup or re-subscription.
- Microsoft Graph `sendMail` using the LIVE App Service system-assigned managed identity.
- Database email status, consent, unsubscribe token, broadcast, and delivery logging.

## Required Azure configuration after code upload

1. Run `database/002_admin_email.sql` against `sqldb-recomp-live` as the Entra SQL administrator.
2. Configure LIVE App Service Authentication with Microsoft as an identity provider while allowing unauthenticated access to the public site. Use `https://recomp-live.azurewebsites.net/admin` for administration; public-domain `/admin` redirects there.
3. Set `Admin__AllowedObjectId` (and optionally `Admin__AllowedEmail`) in App Service settings.
4. Set `Email__SenderMailbox` and `Email__Enabled=true`.
5. Authorize the LIVE managed identity for the same scoped Exchange Online `Application Mail.Send` role used by TEST.
6. Do not add an unscoped tenant-wide Graph `Mail.Send` grant if the intent is to preserve mailbox-scoped Exchange Application RBAC.

No passwords, client secrets, mailbox credentials, or SQL passwords are included in this package.
