param(
    [string]$RepoRoot = $null
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Section([string]$Title) {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host $Title -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$repoLeaf = Split-Path $RepoRoot -Leaf
$versionMatch = [regex]::Match(
    $repoLeaf,
    '^web_recomp_(v\d+)$',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

if (-not $versionMatch.Success) {
    throw "TEST safety stop: expected repository folder named web_recomp_v####. Got: $RepoRoot"
}

$ExpectedAppVersion = $versionMatch.Groups[1].Value

if ($RepoRoot -match '(?i)(^|[\\/])(live|production)([\\/]|$)') {
    throw "TEST safety stop: RepoRoot contains LIVE/PRODUCTION wording."
}

$testProject =
    Join-Path $RepoRoot "tests\WebRecomp.Web.Tests\WebRecomp.Web.Tests.csproj"

$calibrationRunner =
    Join-Path $RepoRoot "scripts\Run-Engine30Calibration.ps1"

$calibrationRoot =
    Join-Path $RepoRoot "artifacts\engine30-calibration"

foreach ($path in @(
    $testProject,
    $calibrationRunner
)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

Section "1 - REFRESH ACTIVE CFG-3000.0 PROFILE FIXTURE"

& powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File $calibrationRunner `
    -Name "MultidayE2EFixturePrep" `
    -Sex "MALE" `
    -Age 40 `
    -HeightIn 69 `
    -WeightLb 180 `
    -BodyFatPct 20 `
    -Lifestyle "MINIMAL" `
    -PlanCode "PLAN_2" `
    -OccupationName "Office" `
    -OccupationActivityCode "SEDENTARY" `
    -OccupationHoursPerWeek 40 `
    -OccupationActivityFraction 0.20 `
    -DurationMode "FIXED" `
    -FixedDurationMin 60 `
    -ActionDate "2026-09-07"

if ($LASTEXITCODE -ne 0) {
    throw "Fresh calibration fixture run failed."
}

$fixtureRun =
    Get-ChildItem `
        -LiteralPath $calibrationRoot `
        -Directory |
    Where-Object {
        $_.Name -like "*MultidayE2EFixturePrep*"
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $fixtureRun) {
    throw "Could not locate the fresh MultidayE2EFixturePrep calibration directory."
}

$fixturePath =
    Join-Path `
        $fixtureRun.FullName `
        "active_workout_profiles.json"

if (-not (Test-Path -LiteralPath $fixturePath)) {
    throw "Fresh active workout fixture not found: $fixturePath"
}

Section "2 - RUN MULTI-DAY ENGINE PIPELINE"

$stamp =
    Get-Date -Format "yyyyMMdd_HHmmss"

$outputDirectory =
    Join-Path `
        $calibrationRoot `
        "multiday-e2e\${stamp}_ReferenceMale"

New-Item `
    -ItemType Directory `
    -Path $outputDirectory `
    -Force | Out-Null

$oldFixture =
    [Environment]::GetEnvironmentVariable(
        "ENGINE30_MULTIDAY_E2E_FIXTURE_PATH",
        "Process")

$oldOutput =
    [Environment]::GetEnvironmentVariable(
        "ENGINE30_MULTIDAY_E2E_OUTPUT_DIR",
        "Process")

try {
    [Environment]::SetEnvironmentVariable(
        "ENGINE30_MULTIDAY_E2E_FIXTURE_PATH",
        $fixturePath,
        "Process")

    [Environment]::SetEnvironmentVariable(
        "ENGINE30_MULTIDAY_E2E_OUTPUT_DIR",
        $outputDirectory,
        "Process")

    Set-Location $RepoRoot

    & dotnet test `
        $testProject `
        --configuration Release `
        --no-build `
        --filter "FullyQualifiedName~Engine30MultidayEndToEndGateTests" `
        --logger "console;verbosity=minimal"

    if ($LASTEXITCODE -ne 0) {
        throw "Multi-day Engine 3.0 end-to-end test failed."
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        "ENGINE30_MULTIDAY_E2E_FIXTURE_PATH",
        $oldFixture,
        "Process")

    [Environment]::SetEnvironmentVariable(
        "ENGINE30_MULTIDAY_E2E_OUTPUT_DIR",
        $oldOutput,
        "Process")
}

$reportPath =
    Join-Path `
        $outputDirectory `
        "Engine30_Multiday_E2E_Report.txt"

$csvPath =
    Join-Path `
        $outputDirectory `
        "Engine30_Multiday_E2E_Days.csv"

if (-not (Test-Path -LiteralPath $reportPath)) {
    throw "E2E report was not generated: $reportPath"
}

if (-not (Test-Path -LiteralPath $csvPath)) {
    throw "E2E CSV was not generated: $csvPath"
}

Section "3 - DASHBOARD TEMPORAL / ANALYSIS REGRESSION"

Set-Location $RepoRoot

& dotnet test `
    $testProject `
    --configuration Release `
    --no-build `
    --filter "FullyQualifiedName~Engine30DashboardProjectionServiceTests|FullyQualifiedName~Engine30DashboardAnalysisServiceTests|FullyQualifiedName~DashboardTodayAnalysisSwitchTests|FullyQualifiedName~DashboardTomorrowCoachGuideViewTests" `
    --logger "console;verbosity=minimal"

if ($LASTEXITCODE -ne 0) {
    throw "Dashboard temporal/analysis regression gate failed."
}

Section "MULTI-DAY END-TO-END GATE COMPLETE"

Write-Host "Report:" -ForegroundColor Green
Write-Host $reportPath
Write-Host ""
Write-Host "CSV:" -ForegroundColor Green
Write-Host $csvPath
Write-Host ""
Write-Host "No Engine coefficient change, production formula change, database write, Azure deployment, LIVE change, or Git operation occurred." -ForegroundColor Green