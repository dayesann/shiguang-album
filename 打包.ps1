#requires -Version 7.0
param()

$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'src\Shiguang.csproj') -Raw)).Project.PropertyGroup.Version
$taskSource = Join-Path $taskRoot ('成品\运行程序-' + $taskVersion)
$taskExecutable = Join-Path $taskSource '拾光照片.exe'
if (-not (Test-Path -LiteralPath $taskExecutable)) { throw '请先运行 构建.ps1。' }
$taskBuiltVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($taskExecutable).ProductVersion.Split('+')[0]
if ($taskBuiltVersion -ne $taskVersion) { throw '成品版本与项目版本不一致，请重新构建。' }

$taskOutput = Join-Path $taskRoot ('成品\发布-' + $taskVersion)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskStage = Join-Path $taskOutput ('package-' + [Guid]::NewGuid().ToString('N'))
$taskPackageRoot = Join-Path $taskStage ('ShiguangAlbum-' + $taskVersion + '-win-x64')
$taskProgram = Join-Path $taskPackageRoot ('运行程序-' + $taskVersion)
New-Item -ItemType Directory -Path $taskProgram -Force | Out-Null
$taskZip = Join-Path $taskOutput ('ShiguangAlbum-' + $taskVersion + '-win-x64.zip')
$taskTemporaryZip = $taskZip + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'

try {
    foreach ($name in @('拾光照片.exe','拾光照片.dll','拾光照片.deps.json','拾光照片.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $taskSource $name) -Destination $taskProgram
    }
    foreach ($name in @('.runtime','Assets')) {
        Copy-Item -LiteralPath (Join-Path $taskSource $name) -Destination $taskProgram -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot '使用说明.md') -Destination $taskPackageRoot
    Copy-Item -LiteralPath (Join-Path $taskRoot ('docs\RELEASE-' + $taskVersion + '.md')) -Destination (Join-Path $taskPackageRoot '版本说明.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\THIRD_PARTY_NOTICES.md') -Destination $taskPackageRoot
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $taskPackageRoot
    # Distribute the icon and demo media notices at the package root as well.
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src\Assets\Fluent\LICENSE.txt') -Destination (Join-Path $taskPackageRoot 'FLUENT-LICENSE.txt')
    $taskQuickStart = @"
时光相册 $taskVersion

适用于 Windows 10/11 x64，已包含 .NET 10 桌面运行时。

1. 完整解压此文件夹。
2. 打开 运行程序-$taskVersion 文件夹，双击 拾光照片.exe。
3. 点击“添加来源目录”选择照片或视频文件夹。

日常单击照片只移动焦点，双击或 Enter 打开预览。
整理时先点击“多选”，勾选后可加入相册、收藏或移入回收站。
Delete 打开删除确认，Esc 取消或退出多选。
方向键移动，Page Up / Page Down 翻页。

从 0.2.13 起，也能在“设置 → 关于 → 软件更新”检查、下载和重启更新。
从更旧版本首次升级时，通过应用菜单或托盘退出旧版。
把新版运行程序文件夹放在原有 数据 文件夹的同级目录，再启动新版。
也可从“设置 → 备份与迁移 → 打开媒体库”选择 library.sqlite。

使用说明.md 提供详细操作与备份方式。
项目主页 https://github.com/dayesann/shiguang-album
"@
    [IO.File]::WriteAllText((Join-Path $taskPackageRoot '开始使用.txt'), $taskQuickStart, [Text.UTF8Encoding]::new($false))
    $taskUnexpected = Get-ChildItem -LiteralPath $taskPackageRoot -File -Recurse -Force |
        Where-Object { $_.Extension -in @('.sqlite','.db','.key','.pfx') -or $_.Name -in @('session.lock','.env','window-preferences.json') }
    if ($taskUnexpected) { throw '发布目录含非程序数据，请检查构建产物。' }
    [IO.Compression.ZipFile]::CreateFromDirectory($taskStage, $taskTemporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    Move-Item -LiteralPath $taskTemporaryZip -Destination $taskZip -Force
    $taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskChecksum = Join-Path $taskOutput 'SHA256SUMS.txt'
    [IO.File]::WriteAllText($taskChecksum, $taskHash + '  ' + [IO.Path]::GetFileName($taskZip) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Output ('便携包已生成 ' + $taskZip)
    Write-Output ('SHA-256 ' + $taskHash)
}
finally {
    $taskResolvedStage = [IO.Path]::GetFullPath($taskStage)
    $taskResolvedOutput = [IO.Path]::GetFullPath($taskOutput).TrimEnd('\') + '\'
    if (-not $taskResolvedStage.StartsWith($taskResolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
        throw '临时打包目录超出发布目录。'
    }
    if (Test-Path -LiteralPath $taskResolvedStage) { Remove-Item -LiteralPath $taskResolvedStage -Recurse -Force }
    if (Test-Path -LiteralPath $taskTemporaryZip) { Remove-Item -LiteralPath $taskTemporaryZip -Force }
}
