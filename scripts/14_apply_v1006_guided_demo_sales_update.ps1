[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Replace-MarkerBlock {
    param(
        [string]$RelativePath,
        [string]$Marker,
        [string]$Replacement
    )

    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path $path)) {
        Write-Host "SKIP: View not found: $RelativePath" -ForegroundColor Yellow
        return
    }

    $content = Get-Content $path -Raw
    $escapedMarker = [regex]::Escape("<!-- $Marker -->")
    $pattern = '(?s)' + $escapedMarker + '.*?(?=@\{)'

    if (-not [regex]::IsMatch($content, $pattern)) {
        throw "Expected marker block $Marker was not found in $RelativePath."
    }

    $content = [regex]::Replace(
        $content,
        $pattern,
        $Replacement + "`r`n",
        1
    )

    Set-Content -Path $path -Value $content -Encoding utf8
    Write-Host "PASS: $RelativePath" -ForegroundColor Green
}

function Replace-DashboardCallout {
    param(
        [string]$AriaLabel,
        [string]$Heading,
        [string]$Body
    )

    $path = Join-Path $repoRoot 'src\WebRecomp.Web\Views\Dashboard\Index.cshtml'
    $content = Get-Content $path -Raw
    $escaped = [regex]::Escape($AriaLabel)

    $pattern =
        '(?s)<aside class="guided-demo-callout" role="note" aria-label="' +
        $escaped +
        '">.*?</aside>'

    if (-not [regex]::IsMatch($content, $pattern)) {
        throw "Dashboard callout not found: $AriaLabel"
    }

    $replacement = @"
<aside class="guided-demo-callout" role="note" aria-label="$AriaLabel">
                    <strong>$Heading</strong>
                    <span>$Body</span>
                </aside>
"@

    $content = [regex]::Replace(
        $content,
        $pattern,
        $replacement,
        1
    )

    Set-Content -Path $path -Value $content -Encoding utf8
}

function Replace-DashboardMuscleCallout {
    $path = Join-Path $repoRoot 'src\WebRecomp.Web\Views\Dashboard\Index.cshtml'
    $content = Get-Content $path -Raw

    $marker = '<!-- V1006_GUIDED_MUSCLE_CALLOUT -->'
    $start = $content.IndexOf($marker)
    if ($start -lt 0) {
        throw "Dashboard muscle callout marker not found."
    }

    $nextSection = $content.IndexOf(
        '<div class="dash-primary-grid">',
        $start
    )

    if ($nextSection -lt 0) {
        throw "Dashboard primary grid marker not found."
    }

    $replacement = @'
<!-- V1006_GUIDED_MUSCLE_CALLOUT -->
    @await Html.PartialAsync(
        "_GuidedDemoCallout",
        new[]
        {
            "Why is 12 Week Recomp different?",
            "Regular dieting can reduce muscle along with fat. Recomp combines resistance training, high protein, and controlled calories to lose fat while protecting - and when possible building - lean muscle."
        })


'@

    $content =
        $content.Substring(0, $start) +
        $replacement +
        $content.Substring($nextSection)

    Set-Content -Path $path -Value $content -Encoding utf8
    Write-Host "PASS: Dashboard value-proposition callout updated." -ForegroundColor Green
}

function Add-GuidedDemoNavLink {
    $path = Join-Path $repoRoot 'src\WebRecomp.Web\Views\Shared\_Layout.cshtml'

    if (-not (Test-Path $path)) {
        throw "Layout not found: $path"
    }

    $content = Get-Content $path -Raw

    if ($content.Contains('guided-demo-nav-link')) {
        Write-Host "SKIP: Guided Demo nav link already present." -ForegroundColor Yellow
        return
    }

    $link = @'
<a class="guided-demo-nav-link"
   asp-controller="GuidedDemo"
   asp-action="Index">Guided Demo</a>
'@

    # Public navigation: place after Create Account where possible.
    $publicPattern =
        '(?s)(<a\b(?:(?!</a>).)*Create Account(?:(?!</a>).)*</a>)'

    if ([regex]::IsMatch($content, $publicPattern)) {
        $content = [regex]::Replace(
            $content,
            $publicPattern,
            '$1' + "`r`n" + $link,
            1
        )
    }
    else {
        # Fallback: place it after Evidence in the public navigation.
        $evidencePattern =
            '(?s)(<a\b(?:(?!</a>).)*Evidence(?:(?!</a>).)*</a>)'

        if ([regex]::IsMatch($content, $evidencePattern)) {
            $content = [regex]::Replace(
                $content,
                $evidencePattern,
                '$1' + "`r`n" + $link,
                1
            )
        }
        else {
            throw "Could not find a safe public-nav insertion point in _Layout.cshtml."
        }
    }

    # Authenticated navigation may be a separate branch. Add a second link
    # after Daily Nutrition only if the first inserted link is not already in
    # that branch.
    $nutritionPattern =
        '(?s)(<a\b(?:(?!</a>).)*Daily Nutrition(?:(?!</a>).)*</a>)'

    if ([regex]::IsMatch($content, $nutritionPattern)) {
        $authLink = @'
<a class="guided-demo-nav-link"
   asp-controller="GuidedDemo"
   asp-action="Index">Guided Demo</a>
'@
        $content = [regex]::Replace(
            $content,
            $nutritionPattern,
            '$1' + "`r`n" + $authLink,
            1
        )
    }

    Set-Content -Path $path -Value $content -Encoding utf8
    Write-Host "PASS: Guided Demo top-navigation link installed." -ForegroundColor Green
}

$shellOnly = @'
<!-- V1006_GUIDED_SHELL -->
@await Html.PartialAsync("_GuidedDemoShell")
'@

$schedule = @'
<!-- V1006_GUIDED_SCHEDULE_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Your weekly game plan",
        "We build the week in advance around balanced strength, conditioning, and recovery. Recomp checks that structure against your plan so you know what to do each day - and why."
    })

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Why build in recovery?",
        "Progress happens when training and recovery work together. Easier days help muscle rebuild, restore energy, and keep the next hard workout productive."
    })
'@

$workout = @'
<!-- V1006_GUIDED_WORKOUT_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")
@await Html.PartialAsync("_GuidedDailyIntro")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "What do I enter here?",
        "Just enter the workout you actually completed today. Recomp compares it with the weekly plan and uses the difference to personalize recovery, fueling, and tomorrow's guidance."
    })

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "What is Rate of Perceived Exertion (RPE)?",
        "RPE is simply how hard the session felt: 8-9 = very hard training, 5 = light cardio or moderate effort, and 2-3 = a recovery day, easy movement, or chores."
    })
'@

$nutrition = @'
<!-- V1006_GUIDED_NUTRITION_CALLOUTS -->
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
        "We keep protein high to protect lean muscle while you lose fat. On harder training days your muscles use more glycogen, so extra carbs help restore that fuel and keep your energy, strength, and recovery high."
    })
'@

$daily = @'
<!-- V1006_GUIDED_DAILY_CALLOUTS -->
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

$profile = @'
<!-- V1006_GUIDED_PROFILE_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "This is your strategy",
        "Your Primary Plan, training experience, goals, and available days shape how Recomp trains and fuels you. The system is built around your real schedule instead of forcing everyone into the same program."
    })
'@

$intake = @'
<!-- V1006_GUIDED_INTAKE_CALLOUTS -->
@await Html.PartialAsync("_GuidedDemoShell")

@await Html.PartialAsync(
    "_GuidedDemoCallout",
    new[]
    {
        "Your starting point",
        "A few setup answers create your initial 12-week strategy. From there, your daily inputs teach Recomp what you actually do so the analytical engine can keep refining your guidance."
    })
'@

Replace-MarkerBlock `
    -RelativePath 'src\WebRecomp.Web\Views\Dashboard\Index.cshtml' `
    -Marker 'V1006_GUIDED_SHELL' `
    -Replacement $shellOnly

Replace-DashboardMuscleCallout

Replace-DashboardCallout `
    -AriaLabel 'Workout decision explanation' `
    -Heading 'What am I supposed to do today?' `
    -Body 'Your workout week is planned in advance. Recomp checks today''s plan against training balance, recovery, readiness, and safety signals, then confirms the workout or explains the adjustment here.'

Replace-DashboardCallout `
    -AriaLabel 'Nutrition target explanation' `
    -Heading 'Why these macros?' `
    -Body 'Protein stays high to protect muscle while you lose fat. Harder training days use more muscle glycogen, so Recomp gives you more carbs to restore that fuel and keep energy, strength, and recovery high.'

Replace-DashboardCallout `
    -AriaLabel 'Coach recommendation explanation' `
    -Heading 'Your virtual coach' `
    -Body 'Recomp calls out what is unique about today, explains what matters, and gives you a clear, motivating next step. On a hard training day like this, if it fits your targets, enjoy a sweet treat after the workout.'

Replace-DashboardCallout `
    -AriaLabel 'Tomorrow plan explanation' `
    -Heading 'A look ahead' `
    -Body 'See what is likely coming tomorrow so you can plan your day. It is preliminary - tomorrow''s check-in and new data can still change the recommendation.'

Replace-DashboardCallout `
    -AriaLabel 'Fuel and glycogen explanation' `
    -Heading 'Output from the analytical engine' `
    -Body 'This is where Recomp turns your recent training and nutrition into an actual decision. The engine measures workout demand, glycogen refill, recovery, and readiness, then uses that analysis to shape today''s fuel and the next recommendation.'

Replace-MarkerBlock `
    -RelativePath 'src\WebRecomp.Web\Views\Schedule\Index.cshtml' `
    -Marker 'V1006_GUIDED_SCHEDULE_CALLOUTS' `
    -Replacement $schedule

Replace-MarkerBlock `
    -RelativePath 'src\WebRecomp.Web\Views\Workout\Index.cshtml' `
    -Marker 'V1006_GUIDED_WORKOUT_CALLOUTS' `
    -Replacement $workout

Replace-MarkerBlock `
    -RelativePath 'src\WebRecomp.Web\Views\Nutrition\Index.cshtml' `
    -Marker 'V1006_GUIDED_NUTRITION_CALLOUTS' `
    -Replacement $nutrition

Replace-MarkerBlock `
    -RelativePath 'src\WebRecomp.Web\Views\Daily\Index.cshtml' `
    -Marker 'V1006_GUIDED_DAILY_CALLOUTS' `
    -Replacement $daily

Replace-MarkerBlock `
    -RelativePath 'src\WebRecomp.Web\Views\Profile\Index.cshtml' `
    -Marker 'V1006_GUIDED_PROFILE_CALLOUTS' `
    -Replacement $profile

Replace-MarkerBlock `
    -RelativePath 'src\WebRecomp.Web\Views\Intake\Index.cshtml' `
    -Marker 'V1006_GUIDED_INTAKE_CALLOUTS' `
    -Replacement $intake

Add-GuidedDemoNavLink

Write-Host ""
Write-Host "PASS: v1006 Guided Demo client-focused update applied." -ForegroundColor Green
Write-Host "Mobile callouts, Daily-page guidance, analytical-engine messaging, and Guided Demo navigation are installed." -ForegroundColor Green
