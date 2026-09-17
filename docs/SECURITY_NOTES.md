# Security notes

- Browser clients never connect directly to Azure SQL.
- Azure App Service connects with its system-assigned managed identity.
- The application database user is not `db_owner`.
- Customer authentication is expected to be enforced with App Service Authentication / Microsoft Entra External ID.
- Each request resolves the external user ID to `app.UserAccounts` and then to an active `app.UserClientAccess` row.
- Client identifiers sent from the browser are never treated as authorization proof.
- Test and production databases must remain separate.
- Do not copy identifiable production client data into test.
- Do not log free-text health/fitness notes to Application Insights.
