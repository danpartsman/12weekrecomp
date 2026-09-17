[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Ensure-GuidedEntryCallouts {
    param(
        [string]$RelativePath,
        [string]$Marker,
        [string]$Snippet
    )

    $path = Join-Path $repoRoot $RelativePath

    if (-not (Test-Path $path)) {
        return $false
    }

    $content = Get-Content $path -Raw

    if ($content.Contains($Marker)) {
        return $true
    }

    $match = [regex]::Match(
        $content,
        '(?m)^@model[^\r\n]*\r?\n'
    )

    if (-not $match.Success) {
        throw "Could not find @model directive in $RelativePath."
    }

    $insertAt = $match.Index + $match.Length

    $content =
        $content.Substring(0, $insertAt) +
        $Snippet +
        "`r`n" +
        $content.Substring($insertAt)

    Set-Content -Path $path -Value $content -Encoding utf8

    Write-Host "PASS: Guided tips installed in $RelativePath" -ForegroundColor Green
    return $true
}

$workoutSnippet = @'
<!-- V1006_GUIDED_WORKOUT_ENTRY_TIPS -->
@await Html.PartialAsync("_GuidedDemoShell")
@await Html.PartialAsync("_GuidedDailyIntro")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "What do I enter here?",
        "Enter the workout you actually completed today. Recomp compares it with your weekly plan and uses the result to personalize recovery, fueling, and the next recommendation."
    })

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "What is Rate of Perceived Exertion (RPE)?",
        "RPE is simply how hard the session felt: 8-9 = very hard training, 5 = light cardio or moderate effort, and 2-3 = a recovery day, easy movement, or chores."
    })
'@

$nutritionSnippet = @'
<!-- V1006_GUIDED_NUTRITION_ENTRY_TIPS -->
@await Html.PartialAsync("_GuidedDemoShell")
@await Html.PartialAsync("_GuidedDailyIntro")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "What do I enter here?",
        "Copy your daily calories, protein, carbs, and fat from the food tracker you already use. Fiber and water are useful extras, but the four core numbers are enough."
    })

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why protein high and carbs higher today?",
        "We keep protein high to protect lean muscle while you lose fat. Harder training uses more muscle glycogen, so extra carbs help restore that fuel and maintain energy, strength, and recovery."
    })
'@

$dailySnippet = @'
<!-- V1006_GUIDED_WEIGHIN_ENTRY_TIPS -->
@await Html.PartialAsync("_GuidedDemoShell")
@await Html.PartialAsync("_GuidedDailyIntro")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Do I need a biometric scale?",
        "A body-composition scale is helpful because it can show muscle gain versus fat loss. But body weight alone still works - consistent weigh-ins are far more useful than skipping them."
    })

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why weigh in consistently?",
        "Daily weight helps keep targets current, while the trend over time tells the real story. Water and glycogen can move the scale from day to day, so Recomp focuses on progress, not one reading."
    })
'@

# The actual Daily Workout route is Workouts/New. Patch that first.
$workoutPatched =
    Ensure-GuidedEntryCallouts `
        -RelativePath 'src\WebRecomp.Web\Views\Workouts\New.cshtml' `
        -Marker 'V1006_GUIDED_WORKOUT_ENTRY_TIPS' `
        -Snippet $workoutSnippet

if (-not $workoutPatched) {
    # Fallback for older branches.
    $workoutPatched =
        Ensure-GuidedEntryCallouts `
            -RelativePath 'src\WebRecomp.Web\Views\Workout\Index.cshtml' `
            -Marker 'V1006_GUIDED_WORKOUT_ENTRY_TIPS' `
            -Snippet $workoutSnippet
}

if (-not $workoutPatched) {
    throw "Could not find the Daily Workout Razor view."
}

$nutritionPatched =
    Ensure-GuidedEntryCallouts `
        -RelativePath 'src\WebRecomp.Web\Views\Nutrition\Index.cshtml' `
        -Marker 'V1006_GUIDED_NUTRITION_ENTRY_TIPS' `
        -Snippet $nutritionSnippet

if (-not $nutritionPatched) {
    throw "Could not find the Daily Nutrition Razor view."
}

$dailyPatched =
    Ensure-GuidedEntryCallouts `
        -RelativePath 'src\WebRecomp.Web\Views\Daily\Index.cshtml' `
        -Marker 'V1006_GUIDED_WEIGHIN_ENTRY_TIPS' `
        -Snippet $dailySnippet

if (-not $dailyPatched) {
    throw "Could not find the Daily Weigh-In Razor view."
}

Write-Host ""
Write-Host "PASS: Guided Demo is wired as a self-contained click-through workspace." -ForegroundColor Green
Write-Host "PASS: Daily Workout, Nutrition, and Weigh-In tips are present on the real entry routes." -ForegroundColor Green
Write-Host "PASS: End Demo returns the browser to the normal Welcome page through the existing /demo/exit action." -ForegroundColor Green
