[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory)]
    [string]$WebAppName,

    [Parameter(Mandatory)]
    [string]$SqlServerFqdn,

    [Parameter(Mandatory)]
    [string]$DatabaseName,

    [string]$SqlEntraAdmin
)

$ErrorActionPreference = 'Stop'
$scriptDirectory = $PSScriptRoot

Write-Host ""
Write-Host "WEB RECOMP v1001 — TEST ENVIRONMENT INSTALL" -ForegroundColor Cyan
Write-Host "This script does not create Azure resources." -ForegroundColor Cyan
Write-Host "It configures and loads the resources you already created." -ForegroundColor Cyan
Write-Host ""

& (Join-Path $scriptDirectory '00_check_prerequisites.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $scriptDirectory '06_regression_math_smoke.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $scriptDirectory '01_configure_app_settings.ps1') `
    -ResourceGroupName $ResourceGroupName `
    -WebAppName $WebAppName `
    -SqlServerFqdn $SqlServerFqdn `
    -DatabaseName $DatabaseName `
    -AspNetEnvironment Development

$databaseArgs = @{
    SqlServerFqdn = $SqlServerFqdn
    DatabaseName = $DatabaseName
}
if ($SqlEntraAdmin) { $databaseArgs.SqlEntraAdmin = $SqlEntraAdmin }

& (Join-Path $scriptDirectory '02_apply_database.ps1') @databaseArgs

$grantArgs = @{
    SqlServerFqdn = $SqlServerFqdn
    DatabaseName = $DatabaseName
    WebAppName = $WebAppName
}
if ($SqlEntraAdmin) { $grantArgs.SqlEntraAdmin = $SqlEntraAdmin }

& (Join-Path $scriptDirectory '03_grant_webapp_identity.ps1') @grantArgs

& (Join-Path $scriptDirectory '04_deploy_application.ps1') `
    -ResourceGroupName $ResourceGroupName `
    -WebAppName $WebAppName

Write-Host ""
Write-Host "INSTALLATION COMPLETE." -ForegroundColor Green
Write-Host "Open: https://$WebAppName.azurewebsites.net/" -ForegroundColor Green
Write-Host ""
Write-Host "If App Service Authentication is already required, sign in in the browser."
Write-Host "If it is not configured yet, complete docs\EXTERNAL_ID_APP_SERVICE_AUTH.md before inviting anyone."
