#requires -Version 7.0
param([switch]$CoreOnly, [string]$UpdateArchive)

$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskVersion = ([xml](Get-Content (Join-Path $taskRoot 'src\Shiguang.csproj') -Raw)).Project.PropertyGroup.Version
$taskExe = Join-Path $taskRoot ('成品\运行程序-' + $taskVersion + '\拾光照片.exe')
if (-not (Test-Path -LiteralPath $taskExe)) { throw '请先运行 构建.ps1 生成程序。' }
$taskRun = Join-Path $taskRoot ('验证\自测-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
& (Join-Path $taskRoot '工具\设计规范检查.ps1')
$taskUpdateArgs = @('--update-test')
if ($UpdateArchive) { $taskUpdateArgs += @('--update-archive', (Resolve-Path -LiteralPath $UpdateArchive).Path) }
$taskCases = @(
    @{ Name='浏览与多选键盘'; Args=@('--demo','--gallery-smoke'); Result='gallery-results.json' },
    @{ Name='幻灯片与背景音乐'; Args=@('--slideshow-smoke'); Result='slideshow-results.json' },
    @{ Name='文件安全边界'; Args=@('--safety-test'); Result='safety-results.json' },
    @{ Name='异常恢复入口'; Args=@('--recovery-smoke'); Result='recovery-results.json' },
    @{ Name='后台任务与托盘'; Args=@('--tray-smoke'); Result='tray-results.json' },
    @{ Name='后台退出后重开'; DataCase='后台任务与托盘'; Args=@('--tray-verify'); Result='tray-restart-results.json' },
    @{ Name='紧凑控件与撤销'; Args=@('--demo','--component-smoke'); Result='component-results.json' },
    @{ Name='媒体库迁移'; Args=@('--package-test'); Result='package-results.json' },
    @{ Name='软件更新'; Args=$taskUpdateArgs; Result='update-results.json' },
    @{ Name='完整流程'; Args=@('--flow-smoke'); Result='flow-test-results.json' },
    @{ Name='核心去重与恢复'; Args=@('--self-test','--test-root',(Join-Path $taskRun '核心去重与恢复\fixture')); Result='self-test-results.json' },
    @{ Name='媒体索引'; Args=@('--viewer-test'); Result='viewer-test-results.json' },
    @{ Name='浏览界面'; Args=@('--demo','--ui-smoke'); Result='ui-smoke-results.json' },
    @{ Name='外观设置'; Args=@('--demo','--settings-smoke'); Result='settings-test-results.json' },
    @{ Name='设计规范验收'; Args=@('--demo','--design-smoke'); Result='design-acceptance-results.json' },
    @{ Name='旧库升级'; Args=@('--migration-test'); Result='migration-results.json' },
    @{ Name='十万索引'; Args=@('--scale-test'); Result='scale-results.json' },
    @{ Name='视频控制'; Args=@('--video-smoke',(Join-Path $taskRoot ('成品\运行程序-' + $taskVersion + '\Assets\Demo\flower.mp4'))); Result='video-playback-results.json' }
)
if ($CoreOnly) { $taskCases = @($taskCases | Where-Object { $_.Name -in @('文件安全边界','媒体库迁移','软件更新','核心去重与恢复','媒体索引','旧库升级') }) }
$taskResults = @()
foreach ($taskCase in $taskCases) {
    $taskData = Join-Path $taskRun ($(if ($taskCase.ContainsKey('DataCase')) { $taskCase.DataCase } else { $taskCase.Name }) + '\data')
    $taskStart = [Diagnostics.ProcessStartInfo]::new($taskExe)
    $taskStart.UseShellExecute = $false
    $taskStart.WorkingDirectory = Split-Path -LiteralPath $taskExe
    foreach ($taskArgument in ($taskCase.Args + @('--data',$taskData))) { $taskStart.ArgumentList.Add($taskArgument) }
    $taskProcess = [Diagnostics.Process]::Start($taskStart)
    $taskTimedOut = -not $taskProcess.WaitForExit(180000)
    if ($taskTimedOut) { $taskProcess.Kill($true); $taskProcess.WaitForExit() }
    $taskFile = Join-Path $taskData $taskCase.Result
    $taskPassed = $false
    if (Test-Path -LiteralPath $taskFile) { $taskPassed = (Get-Content -LiteralPath $taskFile -Raw | ConvertFrom-Json).Passed -eq $true }
    $taskResults += [pscustomobject]@{ Case=$taskCase.Name; Passed=($taskPassed -and $taskProcess.ExitCode -eq 0 -and -not $taskTimedOut); ExitCode=$taskProcess.ExitCode; TimedOut=$taskTimedOut; Evidence=$taskFile }
    $taskResults | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
    Write-Output ($taskCase.Name + ': ' + $taskResults[-1].Passed)
    if (-not $taskResults[-1].Passed) { throw ('检查失败，保留证据：' + $taskFile) }
}
Write-Output ('全部自动检查通过：' + $taskRun)
Write-Output '此脚本使用独立生成的测试库，不扫描正式来源。原生选择器与实际鼠标/键盘交互另按验收记录检查。'
