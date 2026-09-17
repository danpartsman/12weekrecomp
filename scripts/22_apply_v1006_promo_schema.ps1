[CmdletBinding()]
param(
    [string]$SqlServerFqdn = "sql-recomp-test-dan.database.windows.net",
    [string]$DatabaseName = "sqldb-recomp",
    [string]$SqlEntraAdmin
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd is not installed."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$file = Join-Path $repoRoot 'database\011_upgrade_schema_v1006_promo_codes.sql'

if (-not (Test-Path $file)) {
    throw "Migration file not found: $file"
}

Write-Output "Applying v1006 Promotion Code schema..."
Write-Output "Server:   $SqlServerFqdn"
Write-Output "Database: $DatabaseName"

$args = @(
    '-S', "tcp:$SqlServerFqdn,1433",
    '-d', $DatabaseName,
    '-G',
    '-b',
    '-i', $file
)

if ($SqlEntraAdmin) {
    $args = @(
        '-S', "tcp:$SqlServerFqdn,1433",
        '-d', $DatabaseName,
        '-G',
        '-U', $SqlEntraAdmin,
        '-b',
        '-i', $file
    )
}

& sqlcmd @args

if ($LASTEXITCODE -ne 0) {
    throw "v1006 Promotion Code schema migration failed."
}

Write-Output "PASS: v1006 Promotion Code schema applied."
