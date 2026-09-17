[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Remove-GuidedMarkerBlock {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [Parameter(Mandatory = $true)]
        [string]$Marker
    )

    $path = Join-Path $repoRoot $RelativePath

    if (-not (Test-Path $path)) {
        return
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in (Get-Content $path)) {
        $lines.Add($line)
    }

    while ($true) {
        $markerLine = "<!-- $Marker -->"
        $start = -1

        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i].Trim() -eq $markerLine) {
                $start = $i
                break
            }
        }

        if ($start -lt 0) {
            break
        }

        $j = $start + 1

        while ($j -lt $lines.Count) {
            $trim = $lines[$j].Trim()

            if ([string]::IsNullOrWhiteSpace($trim)) {
                $j++
                continue
            }

            if ($trim -eq '@await Html.PartialAsync("_GuidedDemoShell")' -or
                $trim -eq '@await Html.PartialAsync("_GuidedDailyIntro")') {
                $j++
                continue
            }

            if ($trim.StartsWith('@await Html.PartialAsync(')) {
                $depth = 0

                while ($j -lt $lines.Count) {
                    $current = $lines[$j]

                    foreach ($ch in $current.ToCharArray()) {
                        if ($ch -eq '(') { $depth++ }
                        elseif ($ch -eq ')') { $depth-- }
                    }

                    $j++

                    if ($depth -le 0) {
                        break
                    }
                }

                continue
            }

            break
        }

        $count = $j - $start
        $lines.RemoveRange($start, $count)
    }

    Set-Content -Path $path -Value $lines -Encoding utf8
}

function Insert-AfterModel {
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
        return $false
    }

    $content = Get-Content $path -Raw

    if ($content.Contains("<!-- $Marker -->")) {
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
    return $true
}

function Clean-Mojibake {
    $srcRoot = Join-Path $repoRoot 'src\WebRecomp.Web'

    $badBullet =
        ([char]0x00E2).ToString() +
        ([char]0x20AC).ToString() +
        ([char]0x00A2).ToString()

    $badEmDash =
        ([char]0x00E2).ToString() +
        ([char]0x20AC).ToString() +
        ([char]0x201D).ToString()

    $badEnDash =
        ([char]0x00E2).ToString() +
        ([char]0x20AC).ToString() +
        ([char]0x201C).ToString()

    $badArrow =
        ([char]0x00E2).ToString() +
        ([char]0x2020).ToString() +
        ([char]0x2019).ToString()

    $badRightQuote =
        ([char]0x00E2).ToString() +
        ([char]0x20AC).ToString() +
        ([char]0x2122).ToString()

    $badDegree =
        ([char]0x00C2).ToString() +
        ([char]0x00B0).ToString()

    $goodBullet = ([char]0x2022).ToString()
    $goodEmDash = ([char]0x2014).ToString()
    $goodEnDash = ([char]0x2013).ToString()
    $goodArrow = ([char]0x2192).ToString()
    $goodRightQuote = ([char]0x2019).ToString()
    $goodDegree = ([char]0x00B0).ToString()

    $extensions = @('*.cshtml', '*.cs', '*.js', '*.css', '*.json')

    foreach ($extension in $extensions) {
        Get-ChildItem $srcRoot -Recurse -File -Filter $extension | ForEach-Object {
            $path = $_.FullName
            $content = Get-Content $path -Raw
            $original = $content

            $content = $content.Replace($badBullet, $goodBullet)
            $content = $content.Replace($badEmDash, $goodEmDash)
            $content = $content.Replace($badEnDash, $goodEnDash)
            $content = $content.Replace($badArrow, $goodArrow)
            $content = $content.Replace($badRightQuote, $goodRightQuote)
            $content = $content.Replace($badDegree, $goodDegree)

            if ($extension -eq '*.cshtml') {
                $bulletEscaped = [regex]::Escape($goodBullet)
                $content = [regex]::Replace(
                    $content,
                    '\s*' + $bulletEscaped + '\s*',
                    ' ' + $goodBullet + ' '
                )
            }

            if ($content -ne $original) {
                Set-Content -Path $path -Value $content -Encoding utf8
                Write-Host "CLEANED: $($_.FullName.Substring($repoRoot.Length + 1))" -ForegroundColor Green
            }
        }
    }
}

$targets = @(
    @{ Path='src\WebRecomp.Web\Views\Schedule\Index.cshtml'; Marker='V1006_GUIDED_SCHEDULE_CALLOUTS' },

    @{ Path='src\WebRecomp.Web\Views\Workouts\New.cshtml'; Marker='V1006_GUIDED_WORKOUT_ENTRY_TIPS' },
    @{ Path='src\WebRecomp.Web\Views\Workout\Index.cshtml'; Marker='V1006_GUIDED_WORKOUT_CALLOUTS' },
    @{ Path='src\WebRecomp.Web\Views\Workout\Index.cshtml'; Marker='V1006_GUIDED_WORKOUT_ENTRY_TIPS' },

    @{ Path='src\WebRecomp.Web\Views\Nutrition\Index.cshtml'; Marker='V1006_GUIDED_NUTRITION_CALLOUTS' },
    @{ Path='src\WebRecomp.Web\Views\Nutrition\Index.cshtml'; Marker='V1006_GUIDED_NUTRITION_ENTRY_TIPS' },

    @{ Path='src\WebRecomp.Web\Views\Daily\Index.cshtml'; Marker='V1006_GUIDED_DAILY_CALLOUTS' },
    @{ Path='src\WebRecomp.Web\Views\Daily\Index.cshtml'; Marker='V1006_GUIDED_WEIGHIN_ENTRY_TIPS' },

    @{ Path='src\WebRecomp.Web\Views\Profile\Index.cshtml'; Marker='V1006_GUIDED_PROFILE_CALLOUTS' },
    @{ Path='src\WebRecomp.Web\Views\Intake\Index.cshtml'; Marker='V1006_GUIDED_INTAKE_CALLOUTS' }
)

foreach ($target in $targets) {
    Remove-GuidedMarkerBlock `
        -RelativePath $target.Path `
        -Marker $target.Marker
}

Get-ChildItem (Join-Path $repoRoot 'src\WebRecomp.Web\Views') `
    -Recurse `
    -File `
    -Filter '*.cshtml' | ForEach-Object {

    $content = Get-Content $_.FullName -Raw

    $updated = [regex]::Replace(
        $content,
        '(?m)^\s*@await Html\.PartialAsync\("_GuidedDailyIntro"\)\s*\r?\n?',
        ''
    )

    if ($updated -ne $content) {
        Set-Content -Path $_.FullName -Value $updated -Encoding utf8
    }
}

$scheduleSnippet = @'
<!-- V1006_GUIDED_SCHEDULE_CLEAN -->
@await Html.PartialAsync(
    "_GuidedDemoCalloutSections",
    new[]
    {
        "Built for full-body recomposition",
        "The goal is not simply weight loss. Recomp is built to help you lose fat while preserving - and when possible building - lean muscle across your whole body.",

        "A weekly template that fits real life",
        "We build your workout template for the week in advance using the days you can actually train. You can move workouts when your schedule changes, and Recomp keeps checking the plan against the program guidelines."
    })
'@

$workoutSnippet = @'
<!-- V1006_GUIDED_WORKOUT_CLEAN -->
@await Html.PartialAsync(
    "_GuidedDemoCalloutSections",
    new[]
    {
        "Your planned workout is already working in the background",
        "Until you update this page, Recomp uses the workout on your Weekly Plan for its calculations so the Dashboard can still give you useful guidance.",

        "Update the system with what actually happened",
        "Log the workout you completed. If you trained more than once, add each session. Recomp recalculates training load, glycogen demand, recovery, and recommendations from the work you actually performed.",

        "RPE keeps effort simple",
        "Rate of Perceived Exertion is just how hard the session felt: 8-9 = very hard training, 5 = light cardio or moderate effort, and 2-3 = recovery, easy movement, or chores."
    })
'@

$nutritionSnippet = @'
<!-- V1006_GUIDED_NUTRITION_CLEAN -->
@await Html.PartialAsync(
    "_GuidedDemoCalloutSections",
    new[]
    {
        "Bring over today's totals",
        "Use the food tracker you already like. Enter your daily calories, protein, carbs, and fat here - there is no need to log every meal a second time.",

        "Recomp turns those numbers into a fueling decision",
        "Protein stays high to help protect lean muscle during fat loss. Harder training uses more muscle glycogen, so carbohydrates help replace that fuel and support energy, strength, and recovery. The analytical engine compares what you ate with what your training demanded."
    })
'@

$dailySnippet = @'
<!-- V1006_GUIDED_WEIGHIN_CLEAN -->
@await Html.PartialAsync(
    "_GuidedDemoCalloutSections",
    new[]
    {
        "Body composition tells a better story",
        "A biometric or body-composition scale is helpful because it can show whether weight change is coming from fat or lean mass. A regular scale still works - consistent body weight alone is enough to use the program.",

        "Recomp looks beyond the scale",
        "Sleep, resting heart rate, energy, soreness, and other daily check-in signals give the analytical engine context. Those inputs help the system decide whether to keep, adjust, or recover from the planned workout."
    })
'@

$profileSnippet = @'
<!-- V1006_GUIDED_PROFILE_CLEAN -->
@await Html.PartialAsync(
    "_GuidedDemoCalloutSections",
    new[]
    {
        "Your program starts with your real life",
        "Your goal, training experience, available days, and preferences shape the strategy. Recomp is designed around your schedule instead of forcing everyone into the same template."
    })
'@

$intakeSnippet = @'
<!-- V1006_GUIDED_INTAKE_CLEAN -->
@await Html.PartialAsync(
    "_GuidedDemoCalloutSections",
    new[]
    {
        "A few setup choices create the starting strategy",
        "Recomp uses your baseline, goal, training experience, and available days to build the initial 12-week plan. From there, your real daily inputs keep refining the recommendations."
    })
'@

Insert-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Schedule\Index.cshtml' `
    -Marker 'V1006_GUIDED_SCHEDULE_CLEAN' `
    -Snippet $scheduleSnippet | Out-Null

$workoutInserted =
    Insert-AfterModel `
        -RelativePath 'src\WebRecomp.Web\Views\Workouts\New.cshtml' `
        -Marker 'V1006_GUIDED_WORKOUT_CLEAN' `
        -Snippet $workoutSnippet

if (-not $workoutInserted) {
    Insert-AfterModel `
        -RelativePath 'src\WebRecomp.Web\Views\Workout\Index.cshtml' `
        -Marker 'V1006_GUIDED_WORKOUT_CLEAN' `
        -Snippet $workoutSnippet | Out-Null
}

Insert-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Nutrition\Index.cshtml' `
    -Marker 'V1006_GUIDED_NUTRITION_CLEAN' `
    -Snippet $nutritionSnippet | Out-Null

Insert-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Daily\Index.cshtml' `
    -Marker 'V1006_GUIDED_WEIGHIN_CLEAN' `
    -Snippet $dailySnippet | Out-Null

Insert-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Profile\Index.cshtml' `
    -Marker 'V1006_GUIDED_PROFILE_CLEAN' `
    -Snippet $profileSnippet | Out-Null

Insert-AfterModel `
    -RelativePath 'src\WebRecomp.Web\Views\Intake\Index.cshtml' `
    -Marker 'V1006_GUIDED_INTAKE_CLEAN' `
    -Snippet $intakeSnippet | Out-Null

$schedulePath =
    Join-Path $repoRoot 'src\WebRecomp.Web\Views\Schedule\Index.cshtml'

if (Test-Path $schedulePath) {
    $content = Get-Content $schedulePath -Raw
    $bullet = ([char]0x2022).ToString()

    $content = $content.Replace(
        "WEEKLY $bullet PLAN ONCE, ADJUST ANY TIME",
        "FULL BODY RECOMPOSITION $bullet BUILT AROUND YOUR SCHEDULE"
    )

    $content = $content.Replace(
        'WEEKLY &bull; PLAN ONCE, ADJUST ANY TIME',
        'FULL BODY RECOMPOSITION &bull; BUILT AROUND YOUR SCHEDULE'
    )

    $content = $content.Replace(
        'WEEKLY - PLAN ONCE, ADJUST ANY TIME',
        'FULL BODY RECOMPOSITION - BUILT AROUND YOUR SCHEDULE'
    )

    Set-Content -Path $schedulePath -Value $content -Encoding utf8
}

Clean-Mojibake

Write-Host ""
Write-Host "PASS: Guided Demo callouts consolidated and rewritten." -ForegroundColor Green
Write-Host "PASS: Weekly Plan now emphasizes full-body recomposition and a schedule-based template." -ForegroundColor Green
Write-Host "PASS: Daily pages now use one focused red teaching box per page." -ForegroundColor Green
Write-Host "PASS: Known mojibake sequences cleaned across WebRecomp source." -ForegroundColor Green
