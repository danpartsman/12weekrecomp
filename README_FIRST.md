# Web Recomp v1001 — Schedule-First Azure Manual Deployment

This package replaces the earlier v1000 starter for the web track.

**Dan_Recomp remains a separate workbook and is not modified by this package.**

v1001 ports the new **schedule-first** strategy from `dan_recomp_v64_schedule_first.xlsx`:

- The client plans the coming week by date.
- The normal weekly target is:
  - 1 Major Push
  - 1 Major Pull
  - 1 Major Legs
  - at least 1 lighter strength day
  - 1–2 primary conditioning days
  - at least 1 full recovery day
- OTF Strength Upper/Lower/Total count as **lighter strength**, not one of the dedicated heavy days.
- OTF Standard, HIIT, Long Mixed, Pickleball and Pure Cardio count as **primary conditioning**.
- Zone 2 is **recovery cardio** and does not satisfy the primary conditioning quota.
- Recent hard-session count is a **fueling/recovery warning**, not an automatic downgrade.
- The planned workout is kept unless a real guardrail is triggered:
  - pain/injury flag
  - illness/systemic-fatigue flag
  - very low workout-specific fuel readiness
  - the planned heavy region is not recovered
- Modest glycogen shortfalls are handled with carbohydrate allocation and timing instead of automatically replacing a heavy day with Zone 2.

## What is included

### Database
`database/001_create_schema_v1001.sql`
Creates the complete v1001 schema.

`database/002_seed_configuration_v1001.sql`
Loads plans, workout definitions, schedule roles, glycogen coefficients and global settings extracted from the approved v64 Dan_Recomp model.

`database/003_grant_managed_identity.sql`
Grants the App Service system-assigned managed identity only the database permissions the application needs.

`database/004_verify_database.sql`
Checks tables, active configuration and workout-role counts.

### Web application
`src/WebRecomp.Web/`

ASP.NET Core 10 MVC application with:

- first-time intake
- week-ahead workout planning
- daily readiness/measurement entry
- workout logging
- nutrition logging
- schedule-first dashboard
- weekly strategy validation
- glycogen restoration and carbohydrate carryover
- simple 7-day body-composition trends
- App Service Easy Auth / External ID user resolution

### Manual Azure scripts
The Azure resources are assumed to already exist.

- `scripts/01_configure_app_settings.ps1`
- `scripts/02_apply_database.ps1`
- `scripts/03_grant_webapp_identity.ps1`
- `scripts/04_deploy_application.ps1`
- `scripts/05_verify_webapp.ps1`


## Easiest install path

Once your Azure resources exist and the App Service managed identity + Azure SQL Microsoft Entra administrator are configured, you can run the individual scripts or use one orchestration script:

```powershell
.\scripts\INSTALL_TEST_ENVIRONMENT.ps1 `
  -ResourceGroupName "rg-recomp-test" `
  -WebAppName "YOUR-WEB-APP-NAME" `
  -SqlServerFqdn "YOUR-SQL-SERVER.database.windows.net" `
  -DatabaseName "sqldb-recomp-test" `
  -SqlEntraAdmin "your-entra-login@yourdomain.com"
```

`-SqlEntraAdmin` is optional when your installed `sqlcmd` can resolve the current Entra login without an explicit UPN.

The installer:
1. checks prerequisites,
2. runs the Dan-v64 arithmetic regression,
3. configures App Service,
4. creates/seeds the database,
5. grants the App Service managed identity,
6. runs a local .NET build,
7. deploys only if that build succeeds.

## Deployment order

1. Read `docs/MANUAL_DEPLOYMENT.md`.
2. Confirm the App Service system-assigned identity is enabled.
3. Confirm your Microsoft Entra account/group is Azure SQL server Entra administrator.
4. Run `scripts/01_configure_app_settings.ps1`.
5. Run `scripts/02_apply_database.ps1`.
6. Run `scripts/03_grant_webapp_identity.ps1`.
7. Run `scripts/04_deploy_application.ps1`.
8. Run `scripts/05_verify_webapp.ps1`.
9. Configure/confirm App Service Authentication with Microsoft Entra External ID.
10. Create your first account and complete Intake.

No production client data is included.
