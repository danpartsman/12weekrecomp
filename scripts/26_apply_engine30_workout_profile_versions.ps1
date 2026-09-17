[CmdletBinding()]
param(
    [string]$SqlServerFqdn = "sql-recomp-test-dan.database.windows.net",
    [string]$DatabaseName = "sqldb-recomp",
    [string]$SqlEntraAdmin
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd is not installed. Run database\026_engine30_workout_profile_versions.sql manually using Microsoft Entra authentication."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$sqlFile = Join-Path $repoRoot "database\026_engine30_workout_profile_versions.sql"

if (-not (Test-Path $sqlFile)) {
    throw "Migration file not found: $sqlFile"
}

Write-Host "Applying Engine 3.0 workout profile version schema..." -ForegroundColor Cyan
Write-Host "Server:   $SqlServerFqdn"
Write-Host "Database: $DatabaseName"

$args = @(
    "-S", "tcp:$SqlServerFqdn,1433",
    "-d", $DatabaseName,
    "-G",
    "-b",
    "-i", $sqlFile
)

if (-not [string]::IsNullOrWhiteSpace($SqlEntraAdmin)) {
    $args = @(
        "-S", "tcp:$SqlServerFqdn,1433",
        "-d", $DatabaseName,
        "-G",
        "-U", $SqlEntraAdmin,
        "-b",
        "-i", $sqlFile
    )
}

& sqlcmd @args

if ($LASTEXITCODE -ne 0) {
    throw "Engine 3.0 workout profile schema migration failed."
}

Write-Host "PASS: Engine 3.0 workout profile version schema applied." -ForegroundColor Green
