[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SqlServerFqdn,

    [Parameter(Mandatory)]
    [string]$DatabaseName,

    [Parameter(Mandatory)]
    [string]$WebAppName,

    [string]$SqlEntraAdmin
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd is not installed."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$templatePath = Join-Path $repoRoot 'database\003_grant_managed_identity.sql'
$template = Get-Content $templatePath -Raw

# Web App names cannot contain SQL identifier closing brackets, but escape defensively.
$safeWebAppName = $WebAppName.Replace(']', ']]')
$sql = $template.Replace('__WEB_APP_NAME__', $safeWebAppName)

$tempPath = Join-Path $env:TEMP "web-recomp-grant-$([Guid]::NewGuid().ToString('N')).sql"
$sql | Set-Content $tempPath -Encoding UTF8

try {
    Write-Host "Creating database principal for managed identity '$WebAppName'..." -ForegroundColor Cyan

    $sqlcmdArgs = @(
        '-S', "tcp:$SqlServerFqdn,1433",
        '-d', $DatabaseName,
        '-G',
        '-b',
        '-i', $tempPath
    )

    if ($SqlEntraAdmin) {
        $sqlcmdArgs = @('-S', "tcp:$SqlServerFqdn,1433", '-d', $DatabaseName, '-G', '-U', $SqlEntraAdmin, '-b', '-i', $tempPath)
    }

    & sqlcmd @sqlcmdArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Managed-identity database grant failed."
    }

    Write-Host "Managed identity has application-level database permissions." -ForegroundColor Green
}
finally {
    Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
}
