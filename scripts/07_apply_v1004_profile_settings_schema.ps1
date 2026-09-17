[CmdletBinding()]
param(
    [string]$SqlServerFqdn = "sql-recomp-test-dan.database.windows.net",
    [string]$DatabaseName = "sqldb-recomp",
    [string]$SqlEntraAdmin
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd is not installed. Run database\\006_upgrade_schema_v1004_profile_settings.sql manually using Microsoft Entra authentication."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$file = Join-Path $repoRoot 'database\006_upgrade_schema_v1004_profile_settings.sql'

Write-Host "Applying v1004 profile/settings schema..." -ForegroundColor Cyan

$args = @('-S', "tcp:$SqlServerFqdn,1433", '-d', $DatabaseName, '-G', '-b', '-i', $file)
if ($SqlEntraAdmin) {
    $args = @('-S', "tcp:$SqlServerFqdn,1433", '-d', $DatabaseName, '-G', '-U', $SqlEntraAdmin, '-b', '-i', $file)
}

& sqlcmd @args
if ($LASTEXITCODE -ne 0) { throw "v1004 schema migration failed." }

Write-Host "PASS: v1004 profile/settings schema applied." -ForegroundColor Green
