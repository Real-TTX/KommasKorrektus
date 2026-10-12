#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Copies the MatUi toolkit (CSS, script, icon sprite) from the sibling repository MatDesign into src/KommasKorrektus/wwwroot/ui.
.DESCRIPTION
    Apps copy MatUi, they never load it from another site (rule CODE-03). The ten CSS parts are bundled into one matui.css in the
    order MatDesign serves them. Run it when MatDesign publishes a new toolkit version and commit the result.
.PARAMETER Source
    The MatDesign repository (default: the sibling directory MatDesign).
#>
param(
    [string]$Source = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'MatDesign')
)

$ErrorActionPreference = 'Stop'
$ui = Join-Path $Source 'src/MatUi/wwwroot'
if (-not (Test-Path $ui)) { throw "MatUi not found at $ui" }

$target = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/KommasKorrektus/wwwroot/ui'
New-Item -ItemType Directory -Force $target | Out-Null

$parts = 'matui.tokens.css', 'matui.base.css', 'matui.shell.css', 'matui.controls.css', 'matui.forms.css', 'matui.pickers.css', 'matui.lists.css', 'matui.dialogs.css', 'matui.layout.css', 'matui.apps.css'
$css = foreach ($p in $parts) { [IO.File]::ReadAllText((Join-Path $ui "css/$p")) }
$header = "/* MatUi - bundled from $($parts -join ', ') (copied from MatDesign by scripts/sync-matui.ps1) */`n"
$text = ($header + ($css -join "`n")) -replace "`r`n", "`n"
[IO.File]::WriteAllText((Join-Path $target 'matui.css'), $text, (New-Object Text.UTF8Encoding($false)))

foreach ($f in @(@('js/matui.js', 'matui.js'), @('js/matui.boot.js', 'matui.boot.js'), @('img/matui-icons.svg', 'matui-icons.svg'))) {
    $content = ([IO.File]::ReadAllText((Join-Path $ui $f[0]))) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $target $f[1]), $content, (New-Object Text.UTF8Encoding($false)))
}

Write-Host "MatUi copied to $target" -ForegroundColor Green
