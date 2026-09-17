[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SqlServerFqdn,

    [Parameter(Mandatory)]
    [string]$DatabaseName,

    [string]$SqlEntraAdmin
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd is not installed. Run the SQL files in SSMS/Azure Data Studio using Microsoft Entra authentication instead."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$files = @(
    'database\001_create_schema_v1001.sql',
    'database\002_seed_configuration_v1001.sql',
    'database\004_verify_database.sql'
)

foreach ($relative in $files) {
    $file = Join-Path $repoRoot $relative
    Write-Host "Running $relative..." -ForegroundColor Cyan

    $sqlcmdArgs = @(
        '-S', "tcp:$SqlServerFqdn,1433",
        '-d', $DatabaseName,
        '-G',
        '-b',
        '-i', $file
    )

    if ($SqlEntraAdmin) {
        $sqlcmdArgs = @('-S', "tcp:$SqlServerFqdn,1433", '-d', $DatabaseName, '-G', '-U', $SqlEntraAdmin, '-b', '-i', $file)
    }

    & sqlcmd @sqlcmdArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Database script failed: $relative"
    }
}

Write-Host ""
Write-Host "v1001 schema and schedule-first configuration loaded successfully." -ForegroundColor Green
