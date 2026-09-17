$ErrorActionPreference = "Stop"

$projectRoot = "C:\RecompAzure\web_recomp_v1003"
$cssPath = Join-Path $projectRoot "src\WebRecomp.Web\wwwroot\css\site.css"
$patchCssPath = Join-Path $PSScriptRoot "responsive-nav.css"

if (-not (Test-Path $cssPath)) {
    throw "Could not find site.css at $cssPath"
}

$css = Get-Content $cssPath -Raw
$marker = "/* ===== v1003 responsive authenticated navigation ===== */"

if ($css.Contains($marker)) {
    Write-Host "Responsive navigation CSS already present - no duplicate appended."
}
else {
    Add-Content -Path $cssPath -Value "`r`n"
    Add-Content -Path $cssPath -Value (Get-Content $patchCssPath -Raw)
    Write-Host "Responsive navigation CSS appended."
}

Write-Host "Patch install complete."
