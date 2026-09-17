# Manual Azure scripts

Run from PowerShell in the extracted package directory.

## 0 — prerequisites

```powershell
.\scripts\00_check_prerequisites.ps1
```

## 1 — configure App Service

```powershell
.\scripts\01_configure_app_settings.ps1 `
  -ResourceGroupName "rg-recomp-test" `
  -WebAppName "YOUR-APP-NAME" `
  -SqlServerFqdn "YOUR-SQL-SERVER.database.windows.net" `
  -DatabaseName "sqldb-recomp-test"
```

## 2 — database

```powershell
.\scripts\02_apply_database.ps1 `
  -SqlServerFqdn "YOUR-SQL-SERVER.database.windows.net" `
  -DatabaseName "sqldb-recomp-test"
```

## 3 — managed identity database permissions

```powershell
.\scripts\03_grant_webapp_identity.ps1 `
  -SqlServerFqdn "YOUR-SQL-SERVER.database.windows.net" `
  -DatabaseName "sqldb-recomp-test" `
  -WebAppName "YOUR-APP-NAME"
```

## 4 — deploy application

```powershell
.\scripts\04_deploy_application.ps1 `
  -ResourceGroupName "rg-recomp-test" `
  -WebAppName "YOUR-APP-NAME"
```

## 5 — verify

```powershell
.\scripts\05_verify_webapp.ps1 `
  -WebAppUrl "https://YOUR-APP-NAME.azurewebsites.net"
```


## Optional explicit SQL Entra admin

If `sqlcmd -G` needs your Entra login name, add:

```powershell
-SqlEntraAdmin "your-account@yourdomain.com"
```

to scripts 02 and 03.

## Arithmetic regression smoke test

Before or after deployment:

```powershell
.\scripts\06_regression_math_smoke.ps1
```

It verifies the approved Dan v64 heavy-day arithmetic: 2,150 kcal, P190/C190/F70, 10 g carryover, with the carryover already contained inside the 190 g daily carbohydrate target.
