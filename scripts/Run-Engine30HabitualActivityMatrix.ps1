param(
    [string]$RepoRoot = $null,
    [string]$Name = "AgeWorkMatrix"
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

if (-not (Test-Path -LiteralPath $testProject)) {
    throw "Test project not found: $testProject"
}

$stamp =
    Get-Date -Format "yyyyMMdd_HHmmss"

$safeName =
    ($Name -replace '[^A-Za-z0-9._-]', '_')

$outputDirectory =
    Join-Path `
        $RepoRoot `
        "artifacts\engine30-calibration\habitual-activity-matrix\${stamp}_${safeName}"

New-Item `
    -ItemType Directory `
    -Path $outputDirectory `
    -Force | Out-Null

$prior =
    [Environment]::GetEnvironmentVariable(
        "ENGINE30_HABITUAL_MATRIX_OUTPUT_DIR",
        "Process")

try {
    [Environment]::SetEnvironmentVariable(
        "ENGINE30_HABITUAL_MATRIX_OUTPUT_DIR",
        $outputDirectory,
        "Process")

    Section "12WR TEST - RUN HABITUAL ACTIVITY AGE x WORK-HOURS MATRIX"

    Set-Location $RepoRoot

    & dotnet test `
        $testProject `
        --configuration Release `
        --filter "FullyQualifiedName~Engine30HabitualActivityMatrixTests" `
        --logger "console;verbosity=minimal"

    if ($LASTEXITCODE -ne 0) {
        throw "Habitual activity matrix tests failed."
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        "ENGINE30_HABITUAL_MATRIX_OUTPUT_DIR",
        $prior,
        "Process")
}

$reportPath =
    Join-Path `
        $outputDirectory `
        "Engine30_Habitual_AgeWork_Matrix_Report.txt"

$csvPath =
    Join-Path `
        $outputDirectory `
        "Engine30_Habitual_AgeWork_Matrix.csv"

if (-not (Test-Path -LiteralPath $reportPath)) {
    throw "Matrix report was not generated: $reportPath"
}

if (-not (Test-Path -LiteralPath $csvPath)) {
    throw "Matrix CSV was not generated: $csvPath"
}

Section "HABITUAL ACTIVITY MATRIX COMPLETE"

Write-Host "Report:" -ForegroundColor Green
Write-Host $reportPath
Write-Host ""
Write-Host "CSV:" -ForegroundColor Green
Write-Host $csvPath
Write-Host ""
Write-Host "No database write, Azure deployment, LIVE change, or Git operation occurred." -ForegroundColor Green