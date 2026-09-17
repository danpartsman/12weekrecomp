param(
    [string]$SqlServerFqdn = "sql-recomp-test-dan.database.windows.net",
    [string]$DatabaseName = "sqldb-recomp",
    [string]$SqlEntraAdmin
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$sqlFile = Join-Path $root "database\014_seed_configuration_v1007_engine2.sql"

if (-not (Test-Path $sqlFile)) {
    throw "Configuration script not found: $sqlFile"
}

Write-Host "Applying v1007 Recomp Engine 2.0 draft configuration..."
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

Write-Host "PASS: v1007 Engine 2.0 draft configuration applied."
