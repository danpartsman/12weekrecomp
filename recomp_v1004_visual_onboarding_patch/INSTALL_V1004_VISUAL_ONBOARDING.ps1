param(
    [string]$ProjectRoot = "C:\RecompAzure\web_recomp_v1004"
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$patchRoot = Join-Path $here "patch\src\WebRecomp.Web"
$targetRoot = Join-Path $ProjectRoot "src\WebRecomp.Web"

if (-not (Test-Path $targetRoot)) {
    throw "WebRecomp.Web source tree not found at: $targetRoot"
}

$requiredBaseline = @(
    (Join-Path $targetRoot "Views\Profile\Settings.cshtml"),
    (Join-Path $targetRoot "Controllers\ProfileController.cs"),
    (Join-Path $targetRoot "Views\Home\Evidence.cshtml")
)
foreach ($path in $requiredBaseline) {
    if (-not (Test-Path $path)) {
        throw "Expected installed v1004 baseline file is missing: $path"
    }
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path (Split-Path $ProjectRoot -Parent) "v1004_before_visual_onboarding_$stamp.zip"
Write-Host "Creating rollback backup: $backup"

$items = Get-ChildItem -Path $ProjectRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch "\\bin\\" -and
    $_.FullName -notmatch "\\obj\\" -and
    $_.FullName -notmatch "\\publish\\" -and
    $_.FullName -notmatch "\\node_modules\\"
}
Compress-Archive -Path $items.FullName -DestinationPath $backup -CompressionLevel Optimal

Write-Host "Installing v1004 visual/onboarding patch..."
Copy-Item -Path (Join-Path $patchRoot "*") -Destination $targetRoot -Recurse -Force

$checks = @(
    @{ Name = "Onboarding controller"; Path = (Join-Path $targetRoot "Controllers\OnboardingController.cs"); Pattern = "class OnboardingController" },
    @{ Name = "First-login routing"; Path = (Join-Path $targetRoot "Controllers\HomeController.cs"); Pattern = 'RedirectToAction("Index", "Onboarding")' },
    @{ Name = "Onboarding page"; Path = (Join-Path $targetRoot "Views\Onboarding\Index.cshtml"); Pattern = "Train. Refill. Lean Out. Repeat." },
    @{ Name = "Welcome CTA"; Path = (Join-Path $targetRoot "Views\Home\Index.cshtml"); Pattern = "Recomp Works! Explore the Evidence." },
    @{ Name = "Evidence headline"; Path = (Join-Path $targetRoot "Views\Home\Evidence.cshtml"); Pattern = "Protect Your Tomorrows" },
    @{ Name = "Settings language"; Path = (Join-Path $targetRoot "Views\Profile\Settings.cshtml"); Pattern = "Manage sign-in account" },
    @{ Name = "Visual CSS"; Path = (Join-Path $targetRoot "wwwroot\css\site.css"); Pattern = "v1004 visual + onboarding upgrade" },
    @{ Name = "Dark logo"; Path = (Join-Path $targetRoot "wwwroot\images\recomp\recomp-logo-dark.png"); Pattern = $null },
    @{ Name = "Welcome poster"; Path = (Join-Path $targetRoot "wwwroot\images\recomp\welcome-poster-dark.webp"); Pattern = $null },
    @{ Name = "Evidence comparison"; Path = (Join-Path $targetRoot "wwwroot\images\recomp\evidence-compare-dark.webp"); Pattern = $null }
)

foreach ($check in $checks) {
    if (-not (Test-Path $check.Path)) {
        throw "FAIL: $($check.Name) - missing $($check.Path)"
    }
    if ($check.Pattern) {
        $content = Get-Content -Raw $check.Path
        if ($content -notlike "*$($check.Pattern)*") {
            throw "FAIL: $($check.Name) - expected marker not found"
        }
    }
    Write-Host "PASS: $($check.Name)"
}

Write-Host ""
Write-Host "PASS: v1004 visual + onboarding patch installed locally."
Write-Host "NO database migration was added or run. NO Azure deployment was performed."
Write-Host "Next: dotnet build + regression smoke test."
