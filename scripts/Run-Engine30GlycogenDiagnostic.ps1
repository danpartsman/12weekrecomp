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
    -Name "GlycogenFixturePrep" `
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
        $_.Name -like "*GlycogenFixturePrep*"
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $fixtureRun) {
    throw "Could not locate the fresh GlycogenFixturePrep calibration directory."
}

$fixturePath =
    Join-Path `
        $fixtureRun.FullName `
        "active_workout_profiles.json"

if (-not (Test-Path -LiteralPath $fixturePath)) {
    throw "Fresh active workout fixture not found: $fixturePath"
}

Section "2 - RUN GLYCOGEN COMPONENT DIAGNOSTIC"

$stamp =
    Get-Date -Format "yyyyMMdd_HHmmss"

$outputDirectory =
    Join-Path `
        $calibrationRoot `
        "glycogen-diagnostics\${stamp}_ReferenceMale"

New-Item `
    -ItemType Directory `
    -Path $outputDirectory `
    -Force | Out-Null

$oldFixture =
    [Environment]::GetEnvironmentVariable(
        "ENGINE30_GLYCOGEN_DIAGNOSTIC_FIXTURE_PATH",
        "Process")

$oldOutput =
    [Environment]::GetEnvironmentVariable(
        "ENGINE30_GLYCOGEN_DIAGNOSTIC_OUTPUT_DIR",
        "Process")

try {
    [Environment]::SetEnvironmentVariable(
        "ENGINE30_GLYCOGEN_DIAGNOSTIC_FIXTURE_PATH",
        $fixturePath,
        "Process")

    [Environment]::SetEnvironmentVariable(
        "ENGINE30_GLYCOGEN_DIAGNOSTIC_OUTPUT_DIR",
        $outputDirectory,
        "Process")

    Set-Location $RepoRoot

    & dotnet test `
        $testProject `
        --configuration Release `
        --no-build `
        --filter "FullyQualifiedName~Engine30GlycogenComponentDiagnosticTests" `
        --logger "console;verbosity=minimal"

    if ($LASTEXITCODE -ne 0) {
        throw "Glycogen component diagnostic test failed."
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        "ENGINE30_GLYCOGEN_DIAGNOSTIC_FIXTURE_PATH",
        $oldFixture,
        "Process")

    [Environment]::SetEnvironmentVariable(
        "ENGINE30_GLYCOGEN_DIAGNOSTIC_OUTPUT_DIR",
        $oldOutput,
        "Process")
}

$requiredOutputs =
    @(
        "Engine30_Glycogen_Diagnostic_Report.txt",
        "Engine30_Glycogen_AllWorkouts.csv",
        "Engine30_Glycogen_Resistance_Detail.csv",
        "Engine30_Glycogen_Composite_Detail.csv"
    )

foreach ($name in $requiredOutputs) {
    $path =
        Join-Path $outputDirectory $name

    if (-not (Test-Path -LiteralPath $path)) {
        throw "Expected diagnostic output was not generated: $path"
    }
}

Section "GLYCOGEN DIAGNOSTIC COMPLETE"

Write-Host "Output directory:" -ForegroundColor Green
Write-Host $outputDirectory
Write-Host ""
Write-Host "Files:" -ForegroundColor Cyan

foreach ($name in $requiredOutputs) {
    Write-Host "  $name"
}

Write-Host ""
Write-Host "No Engine coefficient change, database write, Azure deployment, LIVE change, or Git operation occurred." -ForegroundColor Green