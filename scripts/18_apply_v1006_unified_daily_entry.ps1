[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$webRoot = Join-Path $repoRoot 'src\WebRecomp.Web'

$required = @(
    'Controllers\DailyEntryController.cs',
    'ViewModels\DailyEntryViewModels.cs',
    'Views\DailyEntry\Index.cshtml',
    'Views\Shared\_Layout.cshtml',
    'wwwroot\css\daily-entry.css'
)

foreach ($relative in $required) {
    $path = Join-Path $webRoot $relative

    if (-not (Test-Path $path)) {
        throw "Required unified Daily Entry file is missing: $relative"
    }
}

$layout =
    Get-Content (Join-Path $webRoot 'Views\Shared\_Layout.cshtml') -Raw

if (-not $layout.Contains('asp-controller="DailyEntry"')) {
    throw "Layout does not contain the new Daily Entry navigation."
}

if ($layout.Contains('>Daily Weigh-In<') -or
    $layout.Contains('>Daily Workout<') -or
    $layout.Contains('>Daily Nutrition<')) {
    throw "Old three-page Daily navigation is still present in the primary layout."
}

$controller =
    Get-Content (Join-Path $webRoot 'Controllers\DailyEntryController.cs') -Raw

$checks = @(
    @{
        Name = 'Normalized Measurement storage retained'
        Passed = $controller.Contains('db.Measurements')
    },
    @{
        Name = 'Normalized DailyCheckin storage retained'
        Passed = $controller.Contains('db.DailyCheckins')
    },
    @{
        Name = 'Normalized WorkoutSession storage retained'
        Passed = $controller.Contains('db.WorkoutSessions')
    },
    @{
        Name = 'Normalized NutritionDay storage retained'
        Passed = $controller.Contains('db.NutritionDays')
    },
    @{
        Name = 'Manual source provenance retained'
        Passed = $controller.Contains('EntrySourceCode = "MANUAL"')
    },
    @{
        Name = 'Scale source provenance retained'
        Passed = $controller.Contains('MeasurementSourceCode = "MANUAL"')
    },
    @{
        Name = 'Actual workout math retained'
        Passed = $controller.Contains('CalculateActualWorkout')
    },
    @{
        Name = 'Multiple same-day workouts supported'
        Passed = $controller.Contains('foreach (var input in model.Workouts')
    }
)

foreach ($check in $checks) {
    if (-not $check.Passed) {
        throw "Unified Daily Entry validation failed: $($check.Name)"
    }

    Write-Host "PASS: $($check.Name)" -ForegroundColor Green
}

Write-Host ""
Write-Host "PASS: Unified Daily Entry source package is installed." -ForegroundColor Green
Write-Host "No database migration is required." -ForegroundColor Green
Write-Host "Legacy Daily/Workout/Nutrition pages remain in the project as fallback routes but are removed from primary navigation." -ForegroundColor Green
Write-Host "Run build and regression before deployment." -ForegroundColor Green
