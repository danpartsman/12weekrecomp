param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Target = Join-Path $RepoRoot "src\WebRecomp.WorkoutIntelligence\Data\Kinetic"
New-Item -ItemType Directory -Path $Target -Force | Out-Null

$Files = @{
    "exercises.json" = "https://raw.githubusercontent.com/kinetic-place/exercises-db/main/en/exercises.json"
    "muscle_groups.json" = "https://raw.githubusercontent.com/kinetic-place/exercises-db/main/en/muscle_groups.json"
    "equipment.json" = "https://raw.githubusercontent.com/kinetic-place/exercises-db/main/en/equipment.json"
    "LICENSE-KINETIC.txt" = "https://raw.githubusercontent.com/kinetic-place/exercises-db/main/LICENSE"
}

foreach ($name in $Files.Keys) {
    $destination = Join-Path $Target $name
    Invoke-WebRequest -Uri $Files[$name] -OutFile $destination -UseBasicParsing
}

$exercises = Get-Content (Join-Path $Target "exercises.json") -Raw | ConvertFrom-Json
$muscles = Get-Content (Join-Path $Target "muscle_groups.json") -Raw | ConvertFrom-Json
$equipment = Get-Content (Join-Path $Target "equipment.json") -Raw | ConvertFrom-Json
$license = Get-Content (Join-Path $Target "LICENSE-KINETIC.txt") -Raw

if (@($exercises).Count -lt 899) {
    throw "Kinetic snapshot has fewer than 899 exercises; refusing to accept incomplete source data."
}

if (@($muscles).Count -lt 17) {
    throw "Kinetic snapshot has fewer than 17 muscle groups."
}

if (@($equipment).Count -lt 36) {
    throw "Kinetic snapshot has fewer than 36 equipment records."
}

if ($license -notmatch "MIT License") {
    throw "Kinetic license file does not contain the expected MIT License marker."
}

$missingIdentity = @($exercises | Where-Object {
    [string]::IsNullOrWhiteSpace([string]$_.id) -or
    [string]::IsNullOrWhiteSpace([string]$_.name)
})

if ($missingIdentity.Count -gt 0) {
    throw "Kinetic snapshot contains exercise rows without id/name."
}

$manifest = [ordered]@{
    source = "https://github.com/kinetic-place/exercises-db"
    source_branch = "main"
    license = "MIT"
    captured_utc = (Get-Date).ToUniversalTime().ToString("o")
    exercise_count = @($exercises).Count
    muscle_group_count = @($muscles).Count
    equipment_count = @($equipment).Count
    files = [ordered]@{}
}

foreach ($name in $Files.Keys | Sort-Object) {
    $path = Join-Path $Target $name
    $manifest.files[$name] = [ordered]@{
        url = $Files[$name]
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

$manifest |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $Target "snapshot-manifest.json") -Encoding utf8

Write-Host "Kinetic exercise catalog snapshot: GREEN" -ForegroundColor Green
Write-Host "Exercises: $(@($exercises).Count)"
Write-Host "Muscles:   $(@($muscles).Count)"
Write-Host "Equipment: $(@($equipment).Count)"
Write-Host "Runtime API dependency: NONE" -ForegroundColor Green
