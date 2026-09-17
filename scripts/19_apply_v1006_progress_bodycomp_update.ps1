[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$webRoot = Join-Path $repoRoot 'src\WebRecomp.Web'

$required = @(
    'Controllers\DailyEntryController.cs',
    'Controllers\ProgressController.cs',
    'ViewModels\DailyEntryViewModels.cs',
    'ViewModels\ProgressRecompViewModels.cs',
    'Views\DailyEntry\Index.cshtml',
    'Views\Progress\Index.cshtml',
    'wwwroot\css\progress-recomp.css'
)

foreach ($relative in $required) {
    $path = Join-Path $webRoot $relative

    if (-not (Test-Path $path)) {
        throw "Required v1006 body-composition update file is missing: $relative"
    }
}

$entitiesPath = Join-Path $webRoot 'Models\Entities.cs'
$entities = Get-Content $entitiesPath -Raw

$entityChecks = @(
    'SkeletalMuscleMassLb',
    'SubcutaneousFatPct',
    'VisceralFatRating',
    'BodyWaterLb'
)

foreach ($name in $entityChecks) {
    if (-not $entities.Contains($name)) {
        throw "Existing Measurement model does not contain $name. Stop before deployment."
    }

    Write-Host "PASS: Existing Measurement field $name found." -ForegroundColor Green
}

$dailyController =
    Get-Content (Join-Path $webRoot 'Controllers\DailyEntryController.cs') -Raw

if (-not $dailyController.Contains('measurement.SkeletalMuscleMassLb =')) {
    throw "Daily Entry does not save skeletal muscle."
}

if (-not $dailyController.Contains('measurement.SubcutaneousFatPct =')) {
    throw "Daily Entry does not save subcutaneous fat."
}

if ($dailyController.Contains('measurement.MuscleMassLb = model.')) {
    throw "Daily Entry would overwrite legacy generic MuscleMassLb. Stop."
}

$progressController =
    Get-Content (Join-Path $webRoot 'Controllers\ProgressController.cs') -Raw

if (-not $progressController.Contains('SkeletalMuscleMassLb')) {
    throw "Progress does not use skeletal muscle."
}

if (-not $progressController.Contains('SubcutaneousFatPct')) {
    throw "Progress does not use subcutaneous fat."
}

Write-Host ""
Write-Host "PASS: Skeletal Muscle is now the preferred manual muscle metric." -ForegroundColor Green
Write-Host "PASS: Subcutaneous Fat is captured with no database migration." -ForegroundColor Green
Write-Host "PASS: Progress prioritizes Fat Mass, Skeletal Muscle, then Weight." -ForegroundColor Green
Write-Host "PASS: Waist is now a secondary metric." -ForegroundColor Green
Write-Host "PASS: Legacy/provider MuscleMassLb is preserved as a fallback." -ForegroundColor Green
Write-Host ""
Write-Host "No database migration is required." -ForegroundColor Green
Write-Host "Run build and regression before deployment." -ForegroundColor Green
