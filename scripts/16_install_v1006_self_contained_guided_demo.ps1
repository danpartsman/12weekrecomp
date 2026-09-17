[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$css = Join-Path $repoRoot 'src\WebRecomp.Web\wwwroot\css\guided-demo.css'
$append = Join-Path $repoRoot 'guided-demo-self-contained.css.append.txt'
$patch = Join-Path $repoRoot 'scripts\15_apply_v1006_self_contained_guided_demo.ps1'

if (-not (Test-Path $css)) {
    throw "guided-demo.css not found: $css"
}

if (-not (Test-Path $append)) {
    throw "CSS append file not found: $append"
}

$content = Get-Content $css -Raw

if (-not $content.Contains('v1006 self-contained Guided Demo shell')) {
    Add-Content `
        -Path $css `
        -Value ("`r`n" + (Get-Content $append -Raw)) `
        -Encoding utf8

    Write-Host "PASS: Self-contained Guided Demo CSS installed." -ForegroundColor Green
}
else {
    Write-Host "SKIP: Self-contained Guided Demo CSS already installed." -ForegroundColor Yellow
}

powershell -ExecutionPolicy Bypass -File $patch

if ($LASTEXITCODE -ne 0) {
    throw "Guided Demo entry-page patch failed."
}

Write-Host ""
Write-Host "PASS: v1006 self-contained Guided Demo update complete." -ForegroundColor Green
