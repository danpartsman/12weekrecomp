[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory)]
    [string]$WebAppName
)

$ErrorActionPreference = 'Stop'

foreach ($command in @('az','dotnet')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command was not found."
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src\WebRecomp.Web\WebRecomp.Web.csproj'
$artifactDirectory = Join-Path $repoRoot 'artifacts'
$publishDirectory = Join-Path $artifactDirectory 'publish'
$zipPath = Join-Path $artifactDirectory 'web-recomp-v1005a.zip'

Remove-Item $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

Write-Host "Building..." -ForegroundColor Cyan
dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish $project -c Release --no-build -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Compress-Archive -Path "$publishDirectory\*" -DestinationPath $zipPath -Force

Write-Host "Deploying ZIP to Azure App Service..." -ForegroundColor Cyan
az webapp deploy `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --src-path $zipPath `
    --type zip `
    --clean true `
    --restart true `
    --only-show-errors

if ($LASTEXITCODE -ne 0) {
    throw "Azure App Service deployment failed."
}

Write-Host ""
Write-Host "Application deployed." -ForegroundColor Green
Write-Host "Site: https://$WebAppName.azurewebsites.net/"
Write-Host "System status: https://$WebAppName.azurewebsites.net/api/system/status"
Write-Host "Database status: https://$WebAppName.azurewebsites.net/api/system/database"
