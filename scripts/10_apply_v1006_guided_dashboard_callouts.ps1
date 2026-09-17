[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dashboard = Join-Path $repoRoot 'src\WebRecomp.Web\Views\Dashboard\Index.cshtml'
$layout = Join-Path $repoRoot 'src\WebRecomp.Web\Views\Shared\_Layout.cshtml'

if (-not (Test-Path $dashboard)) {
    throw "Dashboard view not found: $dashboard"
}

$content = Get-Content $dashboard -Raw

if ($content.Contains('guided-demo-callout')) {
    Write-Host "Guided Demo Dashboard callouts are already installed." -ForegroundColor Yellow
}
else {
    $nl = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }

    $titleLine = '    ViewData["Title"] = "Dashboard";'
    if (-not $content.Contains($titleLine)) {
        throw "Expected Dashboard title line was not found. No changes made."
    }

    $identityBlock = @(
        $titleLine,
        '    var isGuidedDemo =',
        '        User.FindFirst("recomp_demo")?.Value == "true"',
        '        && User.FindFirst("recomp_demo_type")?.Value == "GUIDED";'
    ) -join $nl

    $content = $content.Replace($titleLine, $identityBlock)

    $dashboardStart = '<div class="recomp-dashboard">'
    if (-not $content.Contains($dashboardStart)) {
        throw "Expected Dashboard root element was not found. No changes made."
    }

    $intro = @'
@if (isGuidedDemo)
{
    <style>
        .guided-demo-banner,
        .guided-demo-callout {
            border: 2px solid #dc2626;
            border-left-width: 6px;
            border-radius: 10px;
            background: #fff5f5;
            color: #7f1d1d;
            box-shadow: 0 2px 8px rgba(127, 29, 29, 0.08);
        }

        .guided-demo-banner {
            margin: 0 0 1.25rem 0;
            padding: 1rem 1.1rem;
        }

        .guided-demo-callout {
            margin-top: 1rem;
            padding: 0.9rem 1rem;
        }

        .guided-demo-callout strong,
        .guided-demo-banner strong {
            display: block;
            margin-bottom: 0.3rem;
            color: #b91c1c;
            font-size: 0.82rem;
            letter-spacing: 0.045em;
            text-transform: uppercase;
        }

        .guided-demo-callout span,
        .guided-demo-banner span {
            display: block;
            line-height: 1.45;
        }
    </style>

    <aside class="guided-demo-banner" role="note" aria-label="Guided Demo explanation">
        <strong>Guided Demo — Follow the red callouts</strong>
        <span>
            These teaching callouts appear only in the Guided Demo. They explain what
            Recomp is evaluating and why each Dashboard section matters. Normal customer
            accounts do not display them.
        </span>
    </aside>
}
'@ -replace "`r?`n", $nl

    $content = $content.Replace(
        $dashboardStart,
        $intro + $nl + $nl + $dashboardStart)

    # WORKOUT callout: insert inside the workout panel after the planned/recommended/actual stack.
    $workoutPattern =
        '(?s)(\s*</div>\r?\n)(\s*</section>\r?\n\r?\n\s*<section class="dash-panel dash-nutrition">)'

    $workoutRx = [regex]::new($workoutPattern)
    if ($workoutRx.Matches($content).Count -lt 1) {
        throw "Workout panel insertion point was not found. No changes made."
    }

    $workoutCallout = @'
            @if (isGuidedDemo)
            {
                <aside class="guided-demo-callout" role="note" aria-label="Workout decision explanation">
                    <strong>Why the workout decision matters</strong>
                    <span>
                        Recomp starts with the workout you planned, then checks recent
                        muscle-region load, recovery, pain or illness flags, and fuel
                        readiness. It keeps the plan when those signals support it and
                        changes the session only when the evidence says that is smarter.
                    </span>
                </aside>
            }
'@ -replace "`r?`n", $nl

    $content = $workoutRx.Replace(
        $content,
        {
            param($m)
            $m.Groups[1].Value +
            $workoutCallout + $nl +
            $m.Groups[2].Value
        },
        1)

    # NUTRITION callout: insert before the nutrition panel closes.
    $nutritionPattern =
        '(\r?\n\s*</section>\r?\n\r?\n\s*<section class="dash-panel dash-coach">)'

    $nutritionRx = [regex]::new($nutritionPattern)
    if ($nutritionRx.Matches($content).Count -lt 1) {
        throw "Nutrition panel insertion point was not found. No changes made."
    }

    $nutritionCallout = @'
            @if (isGuidedDemo)
            {
                <aside class="guided-demo-callout" role="note" aria-label="Nutrition target explanation">
                    <strong>Why today's nutrition target matters</strong>
                    <span>
                        Protein stays deliberately high. Carbohydrate is more adaptive:
                        the base target reflects the plan and today's workout, then Recomp
                        can add recovery carbohydrate when recent training demand has not
                        been fully replaced. Calories are recalculated from the final
                        protein, carbohydrate, and fat targets.
                    </span>
                </aside>
            }
'@ -replace "`r?`n", $nl

    $content = $nutritionRx.Replace(
        $content,
        {
            param($m)
            $nl + $nutritionCallout + $m.Groups[1].Value
        },
        1)

    # COACH callout: insert before the coach panel closes.
    $coachPattern =
        '(\r?\n\s*</section>\r?\n\s*</div>\r?\n\r?\n\s*<section class="dash-tomorrow-repeat-section">)'

    $coachRx = [regex]::new($coachPattern)
    if ($coachRx.Matches($content).Count -lt 1) {
        throw "Coach panel insertion point was not found. No changes made."
    }

    $coachCallout = @'
            @if (isGuidedDemo)
            {
                <aside class="guided-demo-callout" role="note" aria-label="Coach recommendation explanation">
                    <strong>Why the Coach section matters</strong>
                    <span>
                        This is the plain-language layer over the engine. It combines the
                        workout decision with fuel readiness, sleep, energy, soreness,
                        pain or illness flags, and weekly structure so the user gets a
                        practical action instead of a page of raw calculations.
                    </span>
                </aside>
            }
'@ -replace "`r?`n", $nl

    $content = $coachRx.Replace(
        $content,
        {
            param($m)
            $nl + $coachCallout + $m.Groups[1].Value
        },
        1)

    # TOMORROW callout: add to the end of the Tomorrow section.
    $tomorrowPattern =
        '(\r?\n\s*}\r?\n)(\s*</section>\r?\n\r?\n\s*<section class="dash-section dash-fuel-detail">)'

    $tomorrowRx = [regex]::new($tomorrowPattern)
    if ($tomorrowRx.Matches($content).Count -lt 1) {
        throw "Tomorrow section insertion point was not found. No changes made."
    }

    $tomorrowCallout = @'
        @if (isGuidedDemo)
        {
            <aside class="guided-demo-callout" role="note" aria-label="Tomorrow plan explanation">
                <strong>Why tomorrow is shown now</strong>
                <span>
                    Tomorrow is a preliminary planning view, not a final prescription.
                    Recomp uses the workout already on the Weekly Plan and the information
                    available today to show the likely workload and fuel target. Tomorrow's
                    check-in and new data can still change the recommendation.
                </span>
            </aside>
        }
'@ -replace "`r?`n", $nl

    $content = $tomorrowRx.Replace(
        $content,
        {
            param($m)
            $m.Groups[1].Value +
            $tomorrowCallout + $nl +
            $m.Groups[2].Value
        },
        1)

    # FUEL/GLYCOGEN callout: place immediately before the three-step fuel flow.
    $fuelStart = '        <div class="dash-fuel-flow">'
    if (-not $content.Contains($fuelStart)) {
        throw "Fuel/glycogen flow insertion point was not found. No changes made."
    }

    $fuelCallout = @'
        @if (isGuidedDemo)
        {
            <aside class="guided-demo-callout" role="note" aria-label="Fuel and glycogen explanation">
                <strong>This is the core Recomp fuel logic</strong>
                <span>
                    Read this left to right: yesterday's carbohydrate intake is compared
                    with yesterday's estimated workout demand, that produces restoration
                    credit, and the result rolls into the four-day demand-versus-recovery
                    ledger. If today's workout needs more recovery than the ledger shows,
                    Recomp can reserve additional carbohydrate and identify a pre-workout
                    portion instead of automatically cancelling the hard session.
                </span>
            </aside>
        }

'@ -replace "`r?`n", $nl

    $content = $content.Replace(
        $fuelStart,
        $fuelCallout + $fuelStart)

    Set-Content `
        -Path $dashboard `
        -Value $content `
        -Encoding utf8

    Write-Host "PASS: Guided Demo red Dashboard callouts installed." -ForegroundColor Green
}

# Also clean the visible footer typo found during the first v1006 test.
if (Test-Path $layout) {
    $layoutContent = Get-Content $layout -Raw
    $newLayoutContent = $layoutContent.Replace(
        'Web Recomp v1006a',
        'Web Recomp v1006')

    if ($newLayoutContent -ne $layoutContent) {
        Set-Content `
            -Path $layout `
            -Value $newLayoutContent `
            -Encoding utf8

        Write-Host "PASS: Footer changed from v1006a to v1006." -ForegroundColor Green
    }
}
