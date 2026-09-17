$ErrorActionPreference = "Stop"

$project = "C:\RecompAzure\web_recomp_v1004"
$patchRoot = Join-Path $PSScriptRoot "patch"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = "C:\RecompAzure\v1004_before_profile_progress_$stamp.zip"
$tempBackup = Join-Path $env:TEMP "v1004_before_profile_progress_$stamp"

if (-not (Test-Path $project)) { throw "Project not found: $project" }
if (-not (Test-Path $patchRoot)) { throw "Patch folder not found beside installer." }

# Refuse to install over an unexpected or already-patched tree.
$layout = Get-Content (Join-Path $project "src\WebRecomp.Web\Views\Shared\_Layout.cshtml") -Raw
$entities = Get-Content (Join-Path $project "src\WebRecomp.Web\Models\Entities.cs") -Raw
$viewModels = Get-Content (Join-Path $project "src\WebRecomp.Web\ViewModels\AppViewModels.cs") -Raw

if (-not $layout.Contains("Web Recomp v1003 Test")) { throw "STOP: expected v1003 footer baseline not found in v1004 copy." }
if ($entities.Contains("PreferredDisplayName")) { throw "STOP: v1004 profile patch appears to already be installed." }
if ($viewModels.Contains("ProfileViewModel")) { throw "STOP: v1004 profile view models already exist." }

# Back up only files that this patch may replace. New files do not need backup.
if (Test-Path $tempBackup) { Remove-Item $tempBackup -Recurse -Force }
New-Item -ItemType Directory -Path $tempBackup | Out-Null

$existingFiles = @(
    "src\WebRecomp.Web\Models\Entities.cs",
    "src\WebRecomp.Web\Services\AppUserContextService.cs",
    "src\WebRecomp.Web\ViewModels\AppViewModels.cs",
    "src\WebRecomp.Web\Controllers\HomeController.cs",
    "src\WebRecomp.Web\Views\Shared\_Layout.cshtml",
    "src\WebRecomp.Web\Views\Home\Index.cshtml",
    "src\WebRecomp.Web\wwwroot\css\site.css"
)

foreach ($relative in $existingFiles) {
    $source = Join-Path $project $relative
    $dest = Join-Path $tempBackup $relative
    New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
    Copy-Item $source $dest -Force
}

Compress-Archive -Path (Join-Path $tempBackup "*") -DestinationPath $backup -CompressionLevel Optimal
Remove-Item $tempBackup -Recurse -Force
Write-Host "Backup created: $backup" -ForegroundColor Cyan

# Copy patch files into v1004 only.
Get-ChildItem $patchRoot -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($patchRoot.Length).TrimStart("\")
    $dest = Join-Path $project $relative
    New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
    Copy-Item $_.FullName $dest -Force
}

# Static validation.
$layoutAfter = Get-Content (Join-Path $project "src\WebRecomp.Web\Views\Shared\_Layout.cshtml") -Raw
$entitiesAfter = Get-Content (Join-Path $project "src\WebRecomp.Web\Models\Entities.cs") -Raw
$evidence = Join-Path $project "src\WebRecomp.Web\Views\Home\Evidence.cshtml"
$progress = Join-Path $project "src\WebRecomp.Web\Views\Progress\Index.cshtml"
$profile = Join-Path $project "src\WebRecomp.Web\Views\Profile\Index.cshtml"

$checks = @(
    @{ Name="Profile navigation"; Passed=$layoutAfter.Contains('asp-controller="Profile"') },
    @{ Name="Progress navigation"; Passed=$layoutAfter.Contains('asp-controller="Progress"') },
    @{ Name="Theme fields"; Passed=$entitiesAfter.Contains("PreferredDisplayName") -and $entitiesAfter.Contains("ThemeCode") },
    @{ Name="Profile page"; Passed=Test-Path $profile },
    @{ Name="Progress page"; Passed=Test-Path $progress },
    @{ Name="Scientific Evidence page"; Passed=Test-Path $evidence },
    @{ Name="v1004 SQL migration"; Passed=Test-Path (Join-Path $project "database\006_upgrade_schema_v1004_profile_settings.sql") }
)

$failed = $false
foreach ($check in $checks) {
    if ($check.Passed) { Write-Host ("PASS: " + $check.Name) -ForegroundColor Green }
    else { Write-Host ("FAIL: " + $check.Name) -ForegroundColor Red; $failed=$true }
}
if ($failed) { throw "Patch file validation failed. Do not build or deploy." }

Write-Host ""
Write-Host "PASS: v1004 Profile/Settings/Progress/Evidence patch installed locally." -ForegroundColor Green
Write-Host "NO Azure settings changed. NO SQL migration run. NO deployment performed." -ForegroundColor Green
Write-Host "Next: run local build and regression only." -ForegroundColor Yellow
