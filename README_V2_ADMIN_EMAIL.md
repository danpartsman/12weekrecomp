# 12 Week Recomp LIVE v2.2 - Entra Roles + Admin + Trainer + Email

This package extends the LIVE prelaunch site while keeping public visitors anonymous.

## Staff authorization model

Staff access is assigned in Microsoft Entra, not by hard-coded email addresses or object IDs in the application.

Application roles:

- `Administrator` - grants `/admin` access.
- `Trainer` - grants `/trainer` access.

The LIVE application reads the Easy Auth `X-MS-CLIENT-PRINCIPAL` claims header and authorizes from the Entra application-role claim. Administrators may also enter the trainer workspace for operational support. Normal authenticated users receive neither role automatically.

Recommended Entra administration pattern:

1. In the LIVE app registration, create app roles `Administrator` and `Trainer` with Allowed member types = Users/Groups.
2. In the Enterprise Application for LIVE, assign staff users or Entra security groups to those roles.
3. If group-based enterprise-app assignment is available in the tenant, maintain `12WR Administrators` and `12WR Trainers` security groups and assign each group to its corresponding app role. Otherwise, assign users directly to the roles.
4. Do not use Azure subscription/resource RBAC roles such as Owner or Contributor for 12WR application authorization.

## Included functionality

- `/admin` protected by the Entra `Administrator` app role.
- `/trainer` protected by the Entra `Trainer` app role (Administrator is also allowed for support).
- `/staff/access` authenticated diagnostic showing the Entra roles received by the application.
- Admin signup counts.
- `/admin/interests` searchable/filterable signup list and CSV export.
- `/admin/send` audience-targeted update emails with preview and per-recipient unsubscribe links.
- `/unsubscribe` preference page.
- Automatic welcome/confirmation email on first signup or re-subscription.
- Microsoft Graph `sendMail` using the LIVE App Service system-assigned managed identity.
- Database email status, consent, unsubscribe token, broadcast, and delivery logging.

## Required Azure configuration after code upload

1. `database/002_admin_email.sql` has already been applied to `sqldb-recomp-live` if upgrading from v2.1. It is idempotent and may be rerun if needed.
2. Configure LIVE App Service Authentication with Microsoft as an identity provider while allowing unauthenticated access to the public site.
3. Define the Entra application roles `Administrator` and `Trainer` on the LIVE app registration.
4. Assign staff users/groups to the LIVE Enterprise Application roles.
5. Use `https://recomp-live.azurewebsites.net/admin` and `https://recomp-live.azurewebsites.net/trainer` for staff access. Public-domain staff URLs redirect to the Azure hostname so Easy Auth login/cookies remain reliable behind Cloudflare.
6. Set `Email__SenderMailbox` and, after Exchange authorization is complete, `Email__Enabled=true`.
7. Authorize the LIVE managed identity for the same scoped Exchange Online `Application Mail.Send` role used by TEST.
8. Do not add an unscoped tenant-wide Graph `Mail.Send` grant if the intent is to preserve mailbox-scoped Exchange Application RBAC.

No passwords, client secrets, mailbox credentials, SQL passwords, hard-coded administrator IDs, or hard-coded trainer IDs are included in this package.
