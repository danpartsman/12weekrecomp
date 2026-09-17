[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$WebAppUrl
)

$ErrorActionPreference = 'Stop'
$base = $WebAppUrl.TrimEnd('/')

$targets = @(
    "$base/api/system/status",
    "$base/api/system/database",
    "$base/health"
)

foreach ($url in $targets) {
    Write-Host ""
    Write-Host "Checking $url" -ForegroundColor Cyan

    try {
        $response = Invoke-WebRequest `
            -Uri $url `
            -MaximumRedirection 0 `
            -UseBasicParsing `
            -TimeoutSec 30

        Write-Host "PASS ($($response.StatusCode))" -ForegroundColor Green
        if ($response.Content) {
            Write-Host $response.Content
        }
    }
    catch [System.Net.WebException] {
        $response = $_.Exception.Response

        if ($response -and [int]$response.StatusCode -ge 300 -and [int]$response.StatusCode -lt 400) {
            Write-Host "Authentication redirect returned. This is expected if App Service Authentication is enforced." -ForegroundColor Yellow
            Write-Host "Open in a signed-in browser: $url"
        }
        elseif ($response) {
            Write-Host "HTTP failure: $([int]$response.StatusCode) $($response.StatusDescription)" -ForegroundColor Red
        }
        else {
            Write-Host "Connection failed: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "Could not check automatically: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "Open in browser: $url"
    }
}
