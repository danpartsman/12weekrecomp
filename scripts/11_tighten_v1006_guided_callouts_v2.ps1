[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dashboard = Join-Path $repoRoot 'src\WebRecomp.Web\Views\Dashboard\Index.cshtml'

if (-not (Test-Path $dashboard)) {
    throw "Dashboard view not found: $dashboard"
}

$content = Get-Content $dashboard -Raw

# Stronger visual separation from normal white Dashboard cards.
$content = $content.Replace('background: #fff5f5;', 'background: #ffe3e3;')
$content = $content.Replace('border-left-width: 6px;', 'border-left-width: 8px;')
$content = $content.Replace(
    'box-shadow: 0 2px 8px rgba(127, 29, 29, 0.08);',
    'box-shadow: 0 4px 14px rgba(127, 29, 29, 0.14);'
)

# Make each callout heading look like a clear red label.
$strongPattern = '(?s)\.guided-demo-callout strong,\s*\.guided-demo-banner strong\s*\{.*?\}'
$strongReplacement = @'
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
$content = [regex]::Replace($content, $strongPattern, $strongReplacement)

function Replace-Callout {
    param(
        [string]$AriaLabel,
        [string]$Heading,
        [string]$Body
    )

    $escaped = [regex]::Escape($AriaLabel)
    $pattern = '(?s)<aside class="guided-demo-callout" role="note" aria-label="' +
               $escaped +
               '">.*?</aside>'

    $replacement = @"
<aside class="guided-demo-callout" role="note" aria-label="$AriaLabel">
                    <strong>$Heading</strong>
                    <span>$Body</span>
                </aside>
"@

    $script:content = [regex]::Replace($script:content, $pattern, $replacement)
}

# Short, scan-friendly teaching copy.
Replace-Callout `
    -AriaLabel 'Workout decision explanation' `
    -Heading 'Why this workout?' `
    -Body 'Recomp checks your plan against recovery, recent muscle load, safety flags, and fuel. It changes the workout only when those signals justify it.'

Replace-Callout `
    -AriaLabel 'Nutrition target explanation' `
    -Heading 'Why these macros?' `
    -Body 'Protein stays high. Carbs adapt to today''s training and recent glycogen recovery. Calories update from the final macros.'

Replace-Callout `
    -AriaLabel 'Coach recommendation explanation' `
    -Heading 'What the Coach does' `
    -Body 'This turns recovery, fueling, sleep, soreness, and safety signals into one practical action.'

Replace-Callout `
    -AriaLabel 'Tomorrow plan explanation' `
    -Heading 'Why show tomorrow?' `
    -Body 'This is a preview based on today''s data. Tomorrow''s check-in and new information can still change the recommendation.'

Replace-Callout `
    -AriaLabel 'Fuel and glycogen explanation' `
    -Heading 'How fueling carries over' `
    -Body 'Yesterday''s carbs create refill credit against workout demand. Any shortfall rolls into today''s carb and pre-workout targets.'

# Shorten the Guided Demo banner without depending on any Unicode dash character.
$bannerPattern = '(?s)<aside class="guided-demo-banner" role="note" aria-label="Guided Demo explanation">.*?</aside>'
$bannerReplacement = @'
<aside class="guided-demo-banner" role="note" aria-label="Guided Demo explanation">
        <strong>Guided Demo: Red callouts</strong>
        <span>Red boxes explain the decisions that matter. They appear only in the Guided Demo.</span>
    </aside>
'@
$content = [regex]::Replace($content, $bannerPattern, $bannerReplacement)

Set-Content `
    -Path $dashboard `
    -Value $content `
    -Encoding utf8

Write-Host "PASS: Guided Demo callouts shortened and restyled." -ForegroundColor Green
