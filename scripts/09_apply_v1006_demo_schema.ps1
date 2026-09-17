[CmdletBinding()]
param(
    [string]$SqlServerFqdn = "sql-recomp-test-dan.database.windows.net",
    [string]$DatabaseName = "sqldb-recomp",
    [string]$SqlEntraAdmin
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd is not installed. Run database\008_upgrade_schema_v1006_demo.sql manually using Microsoft Entra authentication."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$file = Join-Path $repoRoot 'database\008_upgrade_schema_v1006_demo.sql'

if (-not (Test-Path $file)) {
    throw "Migration file not found: $file"
}

Write-Host "Applying v1006 demo/invitation schema..." -ForegroundColor Cyan
Write-Host "Server:   $SqlServerFqdn"
Write-Host "Database: $DatabaseName"

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
    throw "v1006 demo/invitation schema migration failed."
}

Write-Host "PASS: v1006 demo/invitation schema applied." -ForegroundColor Green
