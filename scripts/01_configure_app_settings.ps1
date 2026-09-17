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

    [ValidateSet('Development','Production')]
    [string]$AspNetEnvironment = 'Development'
)

$ErrorActionPreference = 'Stop'

$connectionString = "Server=tcp:$SqlServerFqdn,1433;Initial Catalog=$DatabaseName;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

Write-Host "Configuring $WebAppName..." -ForegroundColor Cyan

az webapp config appsettings set `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --settings `
        "ASPNETCORE_ENVIRONMENT=$AspNetEnvironment" `
        "ConnectionStrings__RecompDatabase=$connectionString" `
        "WebRecomp__SpecificationVersion=1008" `
        "WebRecomp__EngineVersion=1001.0" `
        "WebRecomp__AllowDevelopmentUser=false" `
        "WEBSITE_RUN_FROM_PACKAGE=1" `
    --only-show-errors | Out-Null

if ($LASTEXITCODE -ne 0) {
    throw "Failed to configure App Service application settings."
}

$identity = az webapp identity show `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    -o json | ConvertFrom-Json

if (-not $identity.principalId) {
    Write-Host "WARNING: the App Service system-assigned managed identity is not enabled." -ForegroundColor Yellow
    Write-Host "Azure Portal -> App Service -> Identity -> System assigned -> On -> Save."
}
else {
    Write-Host "Managed identity principal ID: $($identity.principalId)" -ForegroundColor Green
}

Write-Host "Application settings configured." -ForegroundColor Green

