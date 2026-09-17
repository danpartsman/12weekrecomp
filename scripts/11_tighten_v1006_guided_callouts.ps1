[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dashboard = Join-Path $repoRoot 'src\WebRecomp.Web\Views\Dashboard\Index.cshtml'

if (-not (Test-Path $dashboard)) {
    throw "Dashboard view not found: $dashboard"
}

$content = Get-Content $dashboard -Raw

# Make Guided Demo callouts visually distinct from normal white Dashboard cards.
$content = $content.Replace(
    'background: #fff5f5;',
    'background: #ffe8e8;'
)

$content = $content.Replace(
    'border: 2px solid #dc2626;',
    'border: 2px solid #dc2626;'
)

$content = $content.Replace(
    'border-left-width: 6px;',
    'border-left-width: 8px;'
)

$content = $content.Replace(
    'box-shadow: 0 2px 8px rgba(127, 29, 29, 0.08);',
    'box-shadow: 0 4px 14px rgba(127, 29, 29, 0.14);'
)

# Turn the callout heading into an obvious red badge.
$oldStrong = @'
        .guided-demo-callout strong,
        .guided-demo-banner strong {
            display: block;
            margin-bottom: 0.3rem;
            color: #b91c1c;
            font-size: 0.82rem;
            letter-spacing: 0.045em;
            text-transform: uppercase;
        }
'@

$newStrong = @'
        .guided-demo-callout strong,
        .guided-demo-banner strong {
            display: inline-block;
            margin-bottom: 0.45rem;
            padding: 0.18rem 0.5rem;
            border-radius: 999px;
            background: #dc2626;
            color: #ffffff;
            font-size: 0.76rem;
            letter-spacing: 0.045em;
            text-transform: uppercase;
        }
'@

if ($content.Contains($oldStrong)) {
    $content = $content.Replace($oldStrong, $newStrong)
}

# Tighten the top banner.
$content = $content.Replace(
    'Guided Demo â€” Follow the red callouts',
    'Guided Demo: Red callouts'
)
$content = $content.Replace(
    'Guided Demo — Follow the red callouts',
    'Guided Demo: Red callouts'
)

$content = $content.Replace(
    @'
            These teaching callouts appear only in the Guided Demo. They explain what
            Recomp is evaluating and why each Dashboard section matters. Normal customer
            accounts do not display them.
'@,
    @'
            Red boxes explain the decisions that matter. They appear only in the Guided Demo.
'@
)

# Workout callout.
$content = $content.Replace(
    'Why the workout decision matters',
    'Why this workout?'
)
$content = $content.Replace(
    @'
                        Recomp starts with the workout you planned, then checks recent
                        muscle-region load, recovery, pain or illness flags, and fuel
                        readiness. It keeps the plan when those signals support it and
                        changes the session only when the evidence says that is smarter.
'@,
    @'
                        Recomp checks your plan against recovery, recent muscle load,
                        safety flags, and fuel. It changes the workout only when those
                        signals justify it.
'@
)

# Nutrition callout.
$content = $content.Replace(
    "Why today's nutrition target matters",
    'Why these macros?'
)
$content = $content.Replace(
    @'
                        Protein stays deliberately high. Carbohydrate is more adaptive:
                        the base target reflects the plan and today's workout, then Recomp
                        can add recovery carbohydrate when recent training demand has not
                        been fully replaced. Calories are recalculated from the final
                        protein, carbohydrate, and fat targets.
'@,
    @'
                        Protein stays high. Carbs adapt to today's training and recent
                        glycogen recovery. Calories update from the final macros.
'@
)

# Coach callout.
$content = $content.Replace(
    'Why the Coach section matters',
    'What the Coach does'
)
$content = $content.Replace(
    @'
                        This is the plain-language layer over the engine. It combines the
                        workout decision with fuel readiness, sleep, energy, soreness,
                        pain or illness flags, and weekly structure so the user gets a
                        practical action instead of a page of raw calculations.
'@,
    @'
                        This turns recovery, fueling, sleep, soreness, and safety signals
                        into one practical action.
'@
)

# Tomorrow callout.
$content = $content.Replace(
    'Why tomorrow is shown now',
    'Why show tomorrow?'
)
$content = $content.Replace(
    @'
                    Tomorrow is a preliminary planning view, not a final prescription.
                    Recomp uses the workout already on the Weekly Plan and the information
                    available today to show the likely workload and fuel target. Tomorrow's
                    check-in and new data can still change the recommendation.
'@,
    @'
                    This is a preview based on today's data. Tomorrow's check-in and new
                    information can still change the recommendation.
'@
)

# Fuel callout.
$content = $content.Replace(
    'This is the core Recomp fuel logic',
    'How fueling carries over'
)
$content = $content.Replace(
    @'
                    Read this left to right: yesterday's carbohydrate intake is compared
                    with yesterday's estimated workout demand, that produces restoration
                    credit, and the result rolls into the four-day demand-versus-recovery
                    ledger. If today's workout needs more recovery than the ledger shows,
                    Recomp can reserve additional carbohydrate and identify a pre-workout
                    portion instead of automatically cancelling the hard session.
'@,
    @'
                    Yesterday's carbs create refill credit against workout demand.
                    Any shortfall rolls into today's carb and pre-workout targets.
'@
)

# Clean the mojibake currently visible in this Dashboard view.
$content = $content.Replace('â€¢', '&bull;')
$content = $content.Replace('â†’', '&rarr;')
$content = $content.Replace('â€“', '&ndash;')
$content = $content.Replace('â€™', '&rsquo;')
$content = $content.Replace('â€”', '&mdash;')

Set-Content `
    -Path $dashboard `
    -Value $content `
    -Encoding utf8

Write-Host "PASS: Guided Demo callouts shortened and restyled." -ForegroundColor Green
Write-Host "PASS: Dashboard encoding artifacts cleaned." -ForegroundColor Green
