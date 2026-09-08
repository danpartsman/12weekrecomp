# Azure next steps - LIVE v2.2 staff roles

## 1. Deploy this package

Upload the contents of this package to GitHub `danpartsman/12weekrecomp` branch `main` and wait for the existing Azure App Service Build Service deployment to succeed.

## 2. Configure Microsoft authentication on `recomp-live`

Azure Portal -> App Services -> recomp-live -> Authentication -> Add identity provider -> Microsoft.

Use a workforce/current-tenant app registration and keep public site access allowed anonymously. The application itself protects staff routes.

## 3. Create application roles in Entra

Microsoft Entra admin center -> App registrations -> the LIVE authentication app registration -> App roles -> Create app role.

Create:

- Display name: `Administrator`; Value: `Administrator`; Allowed member types: Users/Groups; Enabled: Yes.
- Display name: `Trainer`; Value: `Trainer`; Allowed member types: Users/Groups; Enabled: Yes.

## 4. Assign staff

Microsoft Entra admin center -> Enterprise applications -> the LIVE enterprise app -> Users and groups -> Add user/group.

Assign people (or staff security groups where supported) to exactly the desired role.

Suggested groups:

- `12WR Administrators` -> `Administrator`
- `12WR Trainers` -> `Trainer`

Do not use Azure resource RBAC Owner/Contributor roles for application permissions.

## 5. Validate

First open `https://recomp-live.azurewebsites.net/staff/access` and confirm the expected role appears. Then verify:

- `https://recomp-live.azurewebsites.net/admin` -> Administrator succeeds; Trainer-only account receives 403.
- `https://recomp-live.azurewebsites.net/trainer` -> Trainer succeeds; Administrator also succeeds for operational support.
- `https://12weekrecomp.com/` -> remains public and anonymous.
- `https://12weekrecomp.com/admin` -> redirects to the direct Azure staff host.

## 6. Finish email configuration

Keep `Email__Enabled=false` until the LIVE managed identity has mailbox-scoped Exchange `Application Mail.Send` authorization. Then configure `Email__SenderMailbox` and enable email, followed by a first-signup welcome-email test and an admin broadcast test.
