param(
    [string]$SqlServerFqdn = "sql-recomp-test-dan.database.windows.net",
    [string]$DatabaseName = "sqldb-recomp",
    [string]$SqlEntraAdmin
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$sqlFile = Join-Path $root "database\013_upgrade_schema_v1006_demo_analytics.sql"

if (-not (Test-Path $sqlFile)) {
    throw "Migration file not found: $sqlFile"
}

Write-Host "Applying v1006 demo analytics schema..."
Write-Host "Server:   $SqlServerFqdn"
Write-Host "Database: $DatabaseName"

$args = @(
    "-S", $SqlServerFqdn,
    "-d", $DatabaseName,
    "-G",
    "-b",
    "-i", $sqlFile
)

if (-not [string]::IsNullOrWhiteSpace($SqlEntraAdmin)) {
    $args += @("-U", $SqlEntraAdmin)
}

& sqlcmd @args

if ($LASTEXITCODE -ne 0) {
    throw "sqlcmd failed with exit code $LASTEXITCODE."
}

Write-Host "PASS: v1006 demo analytics schema applied."
