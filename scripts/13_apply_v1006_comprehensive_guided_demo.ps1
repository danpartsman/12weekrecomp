[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Add-AfterModel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [Parameter(Mandatory = $true)]
        [string]$Marker,

        [Parameter(Mandatory = $true)]
        [string]$Snippet
    )

    $path = Join-Path $repoRoot $RelativePath

    if (-not (Test-Path $path)) {
        Write-Host "SKIP: View not found: $RelativePath" -ForegroundColor Yellow
        return
    }

    $content = Get-Content $path -Raw

    if ($content.Contains($Marker)) {
        Write-Host "SKIP: Already patched: $RelativePath" -ForegroundColor Yellow
        return
    }

    $match = [regex]::Match(
        $content,
        '(?m)^@model[^\r\n]*(?:\r?\n)'
    )

    if (-not $match.Success) {
        throw "Could not find @model directive in $RelativePath. No change made to that file."
    }

    $insert = $match.Value + $Snippet + "`r`n"
    $content =
        $content.Substring(0, $match.Index) +
        $insert +
        $content.Substring($match.Index + $match.Length)

    Set-Content -Path $path -Value $content -Encoding utf8

    Write-Host "PASS: $RelativePath" -ForegroundColor Green
}

function Add-DashboardMuscleCallout {
    $relativePath = 'src\WebRecomp.Web\Views\Dashboard\Index.cshtml'
    $path = Join-Path $repoRoot $relativePath

    if (-not (Test-Path $path)) {
        throw "Dashboard view not found: $relativePath"
    }

    $content = Get-Content $path -Raw
    $marker = 'V1006_GUIDED_MUSCLE_CALLOUT'

    if ($content.Contains($marker)) {
        Write-Host "SKIP: Dashboard muscle callout already installed." -ForegroundColor Yellow
        return
    }

    $headerClose = '</header>'
    $index = $content.IndexOf($headerClose)

    if ($index -lt 0) {
        throw "Dashboard </header> marker not found. No Dashboard muscle callout added."
    }

    $snippet = @'

    <!-- V1006_GUIDED_MUSCLE_CALLOUT -->
    @await Html.PartialAsync(
        "_GuidedDemoCallout",
        new[]
        {
            "Why protect muscle?",
            "Regular dieting can reduce muscle as well as fat. Recomp combines resistance training, high protein, and a controlled calorie deficit to prioritize fat loss while preserving - and when possible building - lean tissue."
        })

'@

    $position = $index + $headerClose.Length

    $content =
        $content.Substring(0, $position) +
        $snippet +
        $content.Substring($position)

    Set-Content -Path $path -Value $content -Encoding utf8

    Write-Host "PASS: Dashboard muscle-retention callout installed." -ForegroundColor Green
}

$shell = @'
<!-- V1006_GUIDED_SHELL -->
@await Html.PartialAsync("_GuidedDemoShell")
'@

$dashboardSnippet = $shell

$scheduleSnippet = @'
<!-- V1006_GUIDED_SCHEDULE_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why this weekly structure?",
        "Recomp spreads major strength across Push, Pull, and Legs, adds conditioning when the plan calls for it, and protects recovery days. The goal is balanced weekly stimulus - not simply more workouts."
    })

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why recovery days?",
        "Recovery lets trained muscle rebuild and helps the next hard session stay productive. Recomp protects recovery instead of treating every open day as another hard workout."
    })
'@

$workoutSnippet = @'
<!-- V1006_GUIDED_WORKOUT_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why log the workout?",
        "Duration, effort, and rest help Recomp estimate actual training stress, muscle-region load, calories, and glycogen demand. Actual work can differ from the original plan."
    })

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "What is Rate of Perceived Exertion (RPE)?",
        "RPE rates effort from 1 to 10. RPE 10 is maximum effort; 9 means about 1 rep left, 8 about 2 reps left, and 7 about 3 reps left."
    })
'@

$nutritionSnippet = @'
<!-- V1006_GUIDED_NUTRITION_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why keep protein high?",
        "A calorie deficit can cost lean tissue. High protein plus resistance training helps preserve muscle and supports recovery and growth while fat is being lost."
    })

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why do carbs change?",
        "Carbs refill muscle glycogen and support hard training. Recomp raises or lowers carbs with workout demand and recent recovery while keeping total calories controlled."
    })
'@

$dailySnippet = @'
<!-- V1006_GUIDED_DAILY_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why enter morning weight?",
        "Current weight helps set today's nutrition targets. One reading can move with water and glycogen, so Recomp uses the daily input while judging progress from the longer trend."
    })
'@

$profileSnippet = @'
<!-- V1006_GUIDED_PROFILE_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why does setup matter?",
        "Your Primary Plan changes the training and fueling strategy - not just the label. Training experience, availability, and goals shape the weekly structure Recomp recommends."
    })
'@

$intakeSnippet = @'
<!-- V1006_GUIDED_INTAKE_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "What is Recomp setting up?",
        "Your baseline, goal, training experience, and available training days establish the starting 12-week strategy. Recomp then adapts daily recommendations from what you actually do."
    })
'@

Add-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Dashboard\Index.cshtml' `
    -Marker 'V1006_GUIDED_SHELL' `
    -Snippet $dashboardSnippet

Add-DashboardMuscleCallout

Add-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Schedule\Index.cshtml' `
    -Marker 'V1006_GUIDED_SCHEDULE_CALLOUTS' `
    -Snippet $scheduleSnippet

Add-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Workout\Index.cshtml' `
    -Marker 'V1006_GUIDED_WORKOUT_CALLOUTS' `
    -Snippet $workoutSnippet

Add-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Nutrition\Index.cshtml' `
    -Marker 'V1006_GUIDED_NUTRITION_CALLOUTS' `
    -Snippet $nutritionSnippet

Add-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Daily\Index.cshtml' `
    -Marker 'V1006_GUIDED_DAILY_CALLOUTS' `
    -Snippet $dailySnippet

Add-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Profile\Index.cshtml' `
    -Marker 'V1006_GUIDED_PROFILE_CALLOUTS' `
    -Snippet $profileSnippet

Add-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Intake\Index.cshtml' `
    -Marker 'V1006_GUIDED_INTAKE_CALLOUTS' `
    -Snippet $intakeSnippet

Write-Host ""
Write-Host "PASS: Comprehensive v1006 Guided Demo teaching layer applied." -ForegroundColor Green
Write-Host "Includes Guided-only dark mode and page-specific red callouts." -ForegroundColor Green
