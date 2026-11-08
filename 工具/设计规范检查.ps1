$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src'
$checks = [System.Collections.Generic.List[string]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

function Check([bool]$condition, [string]$name) {
    if ($condition) { $checks.Add($name) }
    else { $failures.Add($name) }
}

$uiFiles = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
    Where-Object { $_.Extension -in '.xaml', '.cs' -and $_.FullName -notmatch '\\(bin|obj)\\' }
$uiText = $uiFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
$joined = $uiText -join "`n"

Check (Test-Path -LiteralPath (Join-Path $projectRoot 'DESIGN_SYSTEM.md')) 'design system document exists'
Check (Test-Path -LiteralPath (Join-Path $projectRoot 'DESIGN_RESEARCH.md')) 'design research document exists'
Check (Test-Path -LiteralPath (Join-Path $projectRoot 'AGENTS.md')) 'agent delivery rules exist'
Check ($joined -notmatch 'FontSize\s*=\s*(?:10|11)(?:\.0)?\b' -and $joined -notmatch 'FontSize="(?:10|11)"') 'no 10-11 DIP text sizes'
Check ($joined -notmatch 'MinHeight\s*=\s*29\b' -and $joined -notmatch 'MinHeight="29"') 'no 29 DIP action targets'
Check ($joined -notmatch 'Content\s*=\s*"(?:×|⋯|←|→|▶)"') 'no text glyph button substitutes'

$mainXaml = Get-Content -LiteralPath (Join-Path $sourceRoot 'MainWindow.xaml') -Raw
Check ($mainXaml -match 'x:Name="DuplicateButton"' -and $mainXaml -match 'Click="Duplicates_Click"') 'main window exposes duplicate cleanup'
Check ($mainXaml -match 'AutomationProperties.Name="重复照片"') 'duplicate cleanup has an accessible name'
Check ($mainXaml -match 'ToolTip="快速查找重复照片"') 'duplicate cleanup has a descriptive tooltip'
Check ($mainXaml -match 'x:Name="ModeSwitch" Grid.ColumnSpan="3"') 'year-month-day switch is centered across the full header'

$settings = Get-Content -LiteralPath (Join-Path $sourceRoot 'SettingsWindow.cs') -Raw
Check ($settings -match '重复照片' -and $settings -match '检查重复') 'settings exposes duplicate cleanup'
Check ($settings -match '回收站' -and $settings -match 'main.OpenTrash') 'settings exposes recoverable deletion'

$dialogs = Get-Content -LiteralPath (Join-Path $sourceRoot 'Dialogs.cs') -Raw
Check ($dialogs -match 'class DuplicateWindow' -and $dialogs -match 'class TrashWindow') 'cleanup and recovery windows exist'
Check ($dialogs -match 'Delete.*最近删除' -or $dialogs -match '移入最近删除') 'cleanup action explains recoverable deletion'
Check ($dialogs -match 'Style.*ChoiceCheck') 'custom themed selection control is used'

$mainText = Get-Content -LiteralPath (Join-Path $sourceRoot 'MainWindow.xaml') -Raw
Check ($mainText -notmatch '#[0-9A-Fa-f]{6,8}') 'main window contains no raw color values'
$resources = Get-Content -LiteralPath (Join-Path $sourceRoot 'Resources.xaml') -Raw
$controls = Get-Content -LiteralPath (Join-Path $sourceRoot 'Controls.xaml') -Raw
$metrics = Get-Content -LiteralPath (Join-Path $sourceRoot 'UiMetrics.cs') -Raw
Check ($controls -match 'UiMetrics.Control' -and $controls -match 'UiMetrics.Navigation' -and $metrics -match 'Control = 32, Navigation = 36, Segment = 28') 'shared controls use centralized compact desktop dimensions'
Check ($controls -match 'PART_ToolBarOverflowPanel' -and $mainXaml -match 'CompactToolBar') 'toolbars implement native overflow'
Check ($settings -match 'new SettingRow') 'settings use the shared compact setting row'
Check ($mainXaml -match 'NoticeBar' -and $mainXaml -match 'ModeSwitch_KeyDown') 'feedback and segment keyboard behavior are wired'

$report = [pscustomobject]@{
    Passed = $failures.Count -eq 0
    Count = $checks.Count
    Checks = $checks
    Failures = $failures
}
$reportPath = Join-Path $projectRoot '验证\design-static-results.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding utf8
if ($failures.Count -gt 0) {
    throw ('设计规范静态检查失败：' + ($failures -join '；'))
}
Write-Output ('设计规范静态检查通过：' + $checks.Count + ' 项')
