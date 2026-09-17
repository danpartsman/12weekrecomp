param(
    [string]$RepoRoot = $null,
    [string]$ResourceGroupName = "recomp_group",
    [string]$WebAppName = "recomp-test",

    [string]$Name = "ReferenceMale",

    [ValidateSet("MALE","FEMALE")]
    [string]$Sex = "MALE",

    [ValidateRange(18,100)]
    [int]$Age = 40,

    [ValidateRange(48,90)]
    [decimal]$HeightIn = 69,

    [ValidateRange(80,500)]
    [decimal]$WeightLb = 180,

    [ValidateRange(3,69)]
    [decimal]$BodyFatPct = 20,

    [ValidateSet("MINIMAL","LIGHT","MODERATE")]
    [string]$Lifestyle = "MINIMAL",

    [string]$PlanCode = "PLAN_2",

    [string]$OccupationName = "Office",

    [string]$OccupationActivityCode = "SEDENTARY",

    [ValidateRange(0,100)]
    [decimal]$OccupationHoursPerWeek = 40,

    [ValidateRange(0,2)]
    [decimal]$OccupationActivityFraction = 0.20,

    [ValidateSet("FIXED","PROFILE_DEFAULT")]
    [string]$DurationMode = "FIXED",

    [ValidateRange(1,240)]
    [decimal]$FixedDurationMin = 60,

    [Nullable[decimal]]$RpeOverride = $null,

    [string]$ActionDate = "2026-09-07"
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

function Require-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required file not found: $Path"
    }
}

$Utf8NoBom =
    New-Object System.Text.UTF8Encoding($false)

function Write-Utf8(
    [string]$Path,
    [string]$Text
) {
    [System.IO.File]::WriteAllText(
        $Path,
        $Text,
        $Utf8NoBom)
}

Section "12WR TEST - ENGINE 3.0 PARAMETERIZED CALIBRATION"

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

if ($WebAppName -ne "recomp-test") {
    throw "TEST safety stop: Web App must be recomp-test."
}

$appProject =
    Join-Path $RepoRoot "src\WebRecomp.Web\WebRecomp.Web.csproj"

$testProject =
    Join-Path $RepoRoot "tests\WebRecomp.Web.Tests\WebRecomp.Web.Tests.csproj"

$harnessTest =
    Join-Path $RepoRoot "tests\WebRecomp.Web.Tests\Engine30CalibrationHarnessTests.cs"

Require-File $appProject
Require-File $testProject
Require-File $harnessTest

$appProjectText =
    [System.IO.File]::ReadAllText(
        $appProject,
        [System.Text.Encoding]::UTF8)

$escapedExpectedAppVersion = [regex]::Escape($ExpectedAppVersion)

if ($appProjectText -notmatch
    "<InformationalVersion>\s*$escapedExpectedAppVersion\s*</InformationalVersion>")
{
    throw "Version safety stop: repository folder is $ExpectedAppVersion but project metadata does not match."
}

if ($appProjectText -notmatch '<RecompEngineVersion>\s*3\.0\s*</RecompEngineVersion>') {
    throw "Engine safety stop: project is not Engine 3.0."
}

[DateTime]$parsedActionDate = [DateTime]::MinValue

if (-not [DateTime]::TryParseExact(
    $ActionDate,
    "yyyy-MM-dd",
    [System.Globalization.CultureInfo]::InvariantCulture,
    [System.Globalization.DateTimeStyles]::None,
    [ref]$parsedActionDate))
{
    throw "ActionDate must use yyyy-MM-dd."
}

$hasRpeOverride =
    $null -ne $RpeOverride

if ($hasRpeOverride -and
    ([decimal]$RpeOverride -lt 0 -or [decimal]$RpeOverride -gt 10))
{
    throw "RpeOverride must be between 0 and 10."
}

Set-Location $RepoRoot

Section "1 - READ ACTIVE CFG-3000.0 WORKOUT PROFILES FROM TEST"

$entraUser =
    az account show `
        --query "user.name" `
        --output tsv `
        --only-show-errors

if ([string]::IsNullOrWhiteSpace($entraUser)) {
    throw "Azure CLI is not signed in. Run az login first."
}

$connectionString =
    az webapp config appsettings list `
        --resource-group $ResourceGroupName `
        --name $WebAppName `
        --query "[?name=='ConnectionStrings__RecompDatabase'].value | [0]" `
        --output tsv `
        --only-show-errors

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "Could not read recomp-test database connection."
}

$serverMatch =
    [regex]::Match(
        $connectionString,
        '(?i)(?:Server|Data Source)\s*=\s*(?:tcp:)?([^,;]+)')

$dbMatch =
    [regex]::Match(
        $connectionString,
        '(?i)(?:Initial Catalog|Database)\s*=\s*([^;]+)')

if (-not $serverMatch.Success -or -not $dbMatch.Success) {
    throw "Could not determine TEST SQL server/database."
}

$server =
    $serverMatch.Groups[1].Value.Trim()

$dbName =
    $dbMatch.Groups[1].Value.Trim()

Write-Host "Server:   $server"
Write-Host "Database: $dbName"
Write-Host "Entra:    $entraUser"

$sql = @"
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;

WITH WorkoutProfiles AS
(
    SELECT
        w.[workout_type_code],
        REPLACE(
            REPLACE(
                w.[display_name],
                '|',
                '/'
            ),
            CHAR(13) + CHAR(10),
            ' '
        ) AS [display_name],
        w.[workout_category_code],
        w.[default_minutes],
        w.[default_rest_sec],
        w.[display_order],
        ISNULL(p.[version_code], '') AS [profile_version_code],
        p.[version_number] AS [profile_version_number],
        ISNULL(
            CASE
                WHEN p.[profile_json] IS NULL THEN ''
                ELSE
                    (
                        SELECT
                            CAST(N'' AS XML).value(
                                'xs:base64Binary(sql:column("profile_bytes"))',
                                'varchar(max)'
                            )
                        FROM
                        (
                            SELECT
                                CONVERT(
                                    varbinary(max),
                                    p.[profile_json]
                                ) AS profile_bytes
                        ) AS q
                    )
            END,
            ''
        ) AS [profile_json_base64]
    FROM [config].[WorkoutTypeDefinitions] AS w
    INNER JOIN [config].[ConfigVersions] AS c
        ON c.[config_version_id] = w.[config_version_id]
    OUTER APPLY
    (
        SELECT TOP (1)
            v.[version_code],
            v.[version_number],
            v.[profile_json]
        FROM [config].[Engine30WorkoutProfileVersions] AS v
        WHERE
            v.[workout_type_definition_id] =
                w.[workout_type_definition_id]
            AND v.[is_active] = 1
        ORDER BY v.[version_number] DESC
    ) AS p
    WHERE
        c.[version_code] = 'CFG-3000.0'
        AND w.[is_active] = 1
),
ChunkNumbers AS
(
    SELECT TOP (1000)
        ROW_NUMBER() OVER
        (
            ORDER BY (SELECT NULL)
        ) - 1 AS chunk_number
    FROM sys.all_objects AS a
    CROSS JOIN sys.all_objects AS b
)
SELECT
    wp.[workout_type_code],
    wp.[display_name],
    wp.[workout_category_code],
    CONVERT(varchar(30), wp.[default_minutes]),
    CONVERT(varchar(10), wp.[default_rest_sec]),
    wp.[profile_version_code],
    ISNULL(
        CONVERT(varchar(10), wp.[profile_version_number]),
        ''
    ),
    CONVERT(varchar(10), n.[chunk_number]),
    SUBSTRING(
        wp.[profile_json_base64],
        n.[chunk_number] * 7000 + 1,
        7000
    )
FROM WorkoutProfiles AS wp
INNER JOIN ChunkNumbers AS n
    ON n.[chunk_number] <=
        CASE
            WHEN LEN(wp.[profile_json_base64]) = 0
                THEN 0
            ELSE
                (LEN(wp.[profile_json_base64]) - 1) / 7000
        END
ORDER BY
    wp.[display_order],
    wp.[display_name],
    n.[chunk_number];
"@

$stamp =
    Get-Date -Format "yyyyMMdd_HHmmss"

$safeName =
    ($Name -replace '[^A-Za-z0-9_-]', '_')

$runDir =
    Join-Path `
        $RepoRoot `
        "artifacts\engine30-calibration\$stamp`_$safeName"

New-Item `
    -ItemType Directory `
    -Path $runDir `
    -Force | Out-Null

$sqlFile =
    Join-Path $runDir "read_active_profiles.sql"

$sqlOutput =
    Join-Path $runDir "active_profiles_raw.txt"

Write-Utf8 $sqlFile $sql

& sqlcmd `
    -S "tcp:$server,1433" `
    -d $dbName `
    -G `
    -U $entraUser `
    -b `
    -r 1 `
    -h -1 `
    -s "|" `
    -w 65535 `
    -y 8000 `
    -i $sqlFile `
    -o $sqlOutput

if ($LASTEXITCODE -ne 0) {
    if (Test-Path -LiteralPath $sqlOutput) {
        Get-Content -LiteralPath $sqlOutput
    }

    throw "Could not read active CFG-3000.0 profiles from TEST SQL."
}

$fixtureMap = @{}
$fixtureOrder =
    New-Object System.Collections.Generic.List[string]

foreach ($line in Get-Content -LiteralPath $sqlOutput) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $trimmed =
        $line.Trim()

    if ($trimmed -match '^[\-\|\s]+$') {
        continue
    }

    $parts =
        $line -split '\|', 9

    if ($parts.Count -ne 9) {
        throw "Unexpected SQL output: $line"
    }

    $code =
        $parts[0].Trim()

    if ([string]::IsNullOrWhiteSpace($code)) {
        continue
    }

    if (-not $fixtureMap.ContainsKey($code)) {
        $versionNumber = $null

        if (-not [string]::IsNullOrWhiteSpace($parts[6])) {
            $versionNumber =
                [int]$parts[6].Trim()
        }

        $entry =
            [pscustomobject]@{
                WorkoutTypeCode      = $code
                DisplayName          = $parts[1].Trim()
                WorkoutCategoryCode  = $parts[2].Trim()
                DefaultMinutes       = [decimal]$parts[3].Trim()
                DefaultRestSec       = [int]$parts[4].Trim()
                ProfileVersionCode   = $parts[5].Trim()
                ProfileVersionNumber = $versionNumber
                Chunks               =
                    [System.Collections.Generic.SortedDictionary[int,string]]::new()
            }

        $fixtureMap[$code] = $entry
        [void]$fixtureOrder.Add($code)
    }

    $chunkIndex =
        [int]$parts[7].Trim()

    $fixtureMap[$code].Chunks[$chunkIndex] =
        $parts[8].Trim()
}

$fixtures = @()

foreach ($code in $fixtureOrder) {
    $entry =
        $fixtureMap[$code]

    $builder =
        New-Object System.Text.StringBuilder

    foreach ($chunk in $entry.Chunks.GetEnumerator()) {
        [void]$builder.Append(
            $chunk.Value)
    }

    $fixtures +=
        [pscustomobject]@{
            WorkoutTypeCode      = $entry.WorkoutTypeCode
            DisplayName          = $entry.DisplayName
            WorkoutCategoryCode  = $entry.WorkoutCategoryCode
            DefaultMinutes       = $entry.DefaultMinutes
            DefaultRestSec       = $entry.DefaultRestSec
            ProfileVersionCode   = $entry.ProfileVersionCode
            ProfileVersionNumber = $entry.ProfileVersionNumber
            ProfileJsonBase64    = $builder.ToString()
        }
}

if ($fixtures.Count -ne 27) {
    throw "Expected 27 active workout definitions; found $($fixtures.Count)."
}

$activeProfiles =
    @(
        $fixtures |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace(
                $_.ProfileVersionCode)
        }
    ).Count

Write-Host "Workout definitions:      $($fixtures.Count)" -ForegroundColor Green
Write-Host "Persisted active profiles: $activeProfiles" -ForegroundColor Green
Write-Host "Baseline fallbacks:        $($fixtures.Count - $activeProfiles)" -ForegroundColor Green

$fixturePath =
    Join-Path $runDir "active_workout_profiles.json"

$fixtureJson =
    $fixtures |
    ConvertTo-Json -Depth 8

Write-Utf8 `
    $fixturePath `
    $fixtureJson

Section "2 - WRITE CALIBRATION REQUEST"

$reportPath =
    Join-Path $runDir "Engine30_Calibration_Report.txt"

$csvPath =
    Join-Path $runDir "Engine30_Calibration_Workouts.csv"

$requestPath =
    Join-Path $runDir "Engine30_Calibration_Request.json"

$request =
    [ordered]@{
        Name                       = $Name
        ActionDate                 = $ActionDate
        SexCode                    = $Sex
        AgeYears                   = $Age
        HeightIn                   = $HeightIn
        WeightLb                   = $WeightLb
        BodyFatPct                 = $BodyFatPct
        LifestyleActivityCode      = $Lifestyle
        PlanCode                   = $PlanCode
        OccupationName             = $OccupationName
        OccupationActivityCode     = $OccupationActivityCode
        OccupationHoursPerWeek     = $OccupationHoursPerWeek
        OccupationActivityFraction = $OccupationActivityFraction
        DurationMode               = $DurationMode
        FixedDurationMinutes       = $FixedDurationMin
        RpeOverride                =
            if ($hasRpeOverride) {
                [decimal]$RpeOverride
            }
            else {
                $null
            }
        FixturePath                = $fixturePath
        ReportPath                 = $reportPath
        CsvPath                    = $csvPath
    }

$requestJson =
    $request |
    ConvertTo-Json -Depth 6

Write-Utf8 `
    $requestPath `
    $requestJson

Write-Host "Subject: $Name"
Write-Host "Sex/Age: $Sex / $Age"
Write-Host "Height/Weight/BF: $HeightIn in / $WeightLb lb / $BodyFatPct%"
Write-Host "Lifestyle: $Lifestyle"
Write-Host "Duration: $DurationMode $(if ($DurationMode -eq 'FIXED') { "$FixedDurationMin min" } else { '' })"
Write-Host "RPE: $(if ($hasRpeOverride) { [decimal]$RpeOverride } else { 'profile default' })"

Section "3 - BUILD CURRENT TEST PROJECT"

& dotnet build `
    $testProject `
    --configuration Release `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "TEST PROJECT BUILD FAILED."
}

Section "4 - RUN PARAMETERIZED 27-WORKOUT CALIBRATION"

$previousRequestPath =
    $env:ENGINE30_CALIBRATION_REQUEST_PATH

try {
    $env:ENGINE30_CALIBRATION_REQUEST_PATH =
        $requestPath

    & dotnet test `
        $testProject `
        --configuration Release `
        --no-build `
        --filter "FullyQualifiedName~Engine30CalibrationHarnessTests" `
        --logger "console;verbosity=minimal"

    $testExit =
        $LASTEXITCODE
}
finally {
    $env:ENGINE30_CALIBRATION_REQUEST_PATH =
        $previousRequestPath
}

if (Test-Path -LiteralPath $reportPath) {
    Section "ENGINE 3.0 CALIBRATION REPORT"
    Get-Content -LiteralPath $reportPath
}

if ($testExit -ne 0) {
    Write-Host ""
    Write-Host "Calibration generated RED hard failures." -ForegroundColor Red
    Write-Host "Report: $reportPath"
    Write-Host "CSV:    $csvPath"
    exit $testExit
}

Section "CALIBRATION GREEN"

Write-Host "Parameterized Engine 3.0 calibration completed." -ForegroundColor Green
Write-Host "Report:  $reportPath" -ForegroundColor Green
Write-Host "CSV:     $csvPath" -ForegroundColor Green
Write-Host "Request: $requestPath"
Write-Host "Profiles:$fixturePath"
Write-Host ""
Write-Host "No database writes, Azure deployment, LIVE changes, or Git operations occurred." -ForegroundColor Green
