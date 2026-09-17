[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Round-Increment([decimal]$Value, [decimal]$Increment) {
    if ($Increment -le 0) { return $Value }
    return [math]::Round(
        [double]($Value / $Increment),
        0,
        [MidpointRounding]::AwayFromZero
    ) * $Increment
}

# Approved Dan v64 baseline.
$weight = [decimal]208.4
$baseRestCarbs = [decimal]130
$proteinBase = Round-Increment ([math]::Max(175, [math]::Min(195, [double]($weight * 0.88)))) 5
$protein = Round-Increment ($proteinBase + 5) 5

$burnRaw = 60 * 2.15 * [math]::Pow([double]($weight / 210), 0.5) * 1.02 * 0.65
$burn = Round-Increment $burnRaw 5
$baseCarbs = Round-Increment ($baseRestCarbs + $burn * 0.70) 5

$fourDayDemand = [decimal]220
$fourDayCredit = [decimal]155
$functionalTarget = [decimal]0.75
$allocation = Round-Increment ([math]::Max(0, [double](($fourDayDemand * $functionalTarget) - $fourDayCredit))) 5
$preWorkout = [math]::Min([double]$allocation, 35)
$finalCarbs = Round-Increment ([math]::Max([double]$baseCarbs, [double]($baseRestCarbs + $allocation))) 5
$fat = [decimal]70
$calories = [math]::Round([double](($protein * 4) + ($finalCarbs * 4) + ($fat * 9)), 0, [MidpointRounding]::AwayFromZero)

$actualRatio = $fourDayCredit / $fourDayDemand
$achievement = $actualRatio / $functionalTarget

$checks = @(
    @('Protein', $protein, 190),
    @('Planned glycogen', $burn, 85),
    @('Base carbs', $baseCarbs, 190),
    @('Carryover allocation', $allocation, 10),
    @('Pre-workout carryover', $preWorkout, 10),
    @('Final carbs', $finalCarbs, 190),
    @('Fat', $fat, 70),
    @('Calories', $calories, 2150)
)

$failed = $false

foreach ($check in $checks) {
    $name = $check[0]
    $actual = [decimal]$check[1]
    $expected = [decimal]$check[2]

    if ($actual -ne $expected) {
        Write-Host "FAIL: $name = $actual; expected $expected" -ForegroundColor Red
        $failed = $true
    }
    else {
        Write-Host "PASS: $name = $actual" -ForegroundColor Green
    }
}

Write-Host ("Absolute restoration = {0:P1}" -f $actualRatio)
Write-Host ("Functional target = {0:P1}" -f $functionalTarget)
Write-Host ("Target achievement = {0:P1}" -f $achievement)

if ($failed) {
    exit 1
}

Write-Host "Dan v64 arithmetic smoke test passed." -ForegroundColor Green
