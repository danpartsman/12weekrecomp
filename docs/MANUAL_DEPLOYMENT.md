# Manual deployment into the Azure resources you already created

The package does not create Azure infrastructure. It loads the database and application into the Azure SQL Database and App Service you created manually.

## Values you need

Record these from Azure Portal:

- Resource group name
- App Service name
- App Service URL
- SQL logical server FQDN, for example `sql-recomp-test-example.database.windows.net`
- SQL database name
- Azure subscription ID

Do not place the SQL administrator password in source files.

## Prerequisites on your Windows computer

- Azure CLI
- .NET 10 SDK
- Microsoft `sqlcmd` or SSMS/Azure Data Studio capable of Microsoft Entra authentication

Sign in:

```powershell
az login
```

## 1. App settings

```powershell
.\scripts\01_configure_app_settings.ps1 `
  -ResourceGroupName "rg-recomp-test" `
  -WebAppName "YOUR-WEB-APP-NAME" `
  -SqlServerFqdn "YOUR-SQL-SERVER.database.windows.net" `
  -DatabaseName "sqldb-recomp-test" `
  -AspNetEnvironment "Development"
```

The database connection uses `Authentication=Active Directory Managed Identity`. No database password is stored in the application.

## 2. Create the database schema and configuration

Your signed-in Microsoft Entra user/group must be configured as the Microsoft Entra administrator on the Azure SQL logical server.

```powershell
.\scripts\02_apply_database.ps1 `
  -SqlServerFqdn "YOUR-SQL-SERVER.database.windows.net" `
  -DatabaseName "sqldb-recomp-test"
```

## 3. Grant the web app's managed identity access

```powershell
.\scripts\03_grant_webapp_identity.ps1 `
  -SqlServerFqdn "YOUR-SQL-SERVER.database.windows.net" `
  -DatabaseName "sqldb-recomp-test" `
  -WebAppName "YOUR-WEB-APP-NAME"
```

## 4. Deploy the application

```powershell
.\scripts\04_deploy_application.ps1 `
  -ResourceGroupName "rg-recomp-test" `
  -WebAppName "YOUR-WEB-APP-NAME"
```

## 5. Verify

```powershell
.\scripts\05_verify_webapp.ps1 `
  -WebAppUrl "https://YOUR-WEB-APP-NAME.azurewebsites.net"
```

Before App Service Authentication is enabled, the diagnostic endpoints are accessible directly. After authentication is required, a browser login is expected.

## Expected first workflow

1. Open the site.
2. Sign in.
3. Complete Intake.
4. Open **Plan Week** and select workouts for Monday–Sunday.
5. The week status should show `GOOD` when it contains the required structure.
6. Enter daily measurements/readiness.
7. Log completed workouts and nutrition.
8. The dashboard calculates today's schedule-first recommendation, macros, glycogen status and timing note.
