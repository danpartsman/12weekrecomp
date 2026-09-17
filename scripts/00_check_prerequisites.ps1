[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$missing = @()

foreach ($command in @('az','dotnet','sqlcmd')) {
    if (Get-Command $command -ErrorAction SilentlyContinue) {
        Write-Host "$(command): installed" -ForegroundColor Green
    }
    else {
        Write-Host "$(command): NOT FOUND" -ForegroundColor Yellow
        $missing += $command
    }
}

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "Missing prerequisites: $($missing -join ', ')" -ForegroundColor Yellow
    Write-Host "Azure CLI: install Microsoft Azure CLI."
    Write-Host ".NET: install .NET 10 SDK."
    Write-Host "sqlcmd: install Microsoft sqlcmd, or use SSMS/Azure Data Studio to run the SQL files manually."
    exit 1
}

Write-Host ""
Write-Host "Checking Azure sign-in..." -ForegroundColor Cyan
az account show --only-show-errors | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Host "Azure CLI is not signed in. Run: az login" -ForegroundColor Yellow
    exit 1
}

$account = az account show -o json | ConvertFrom-Json
Write-Host "Azure subscription: $($account.name)" -ForegroundColor Green
Write-Host "Subscription ID: $($account.id)"
Write-Host ""
Write-Host "Prerequisite check passed." -ForegroundColor Green
