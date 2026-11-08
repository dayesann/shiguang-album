param([string]$DotnetPath)

$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskLocalSdk = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
$taskSdk = if ($DotnetPath) { (Resolve-Path -LiteralPath $DotnetPath).Path } elseif (Test-Path -LiteralPath $taskLocalSdk) { $taskLocalSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$taskDotnetRoot = Split-Path -Parent $taskSdk
$taskVersion = ([xml](Get-Content (Join-Path $taskRoot 'src\Shiguang.csproj') -Raw)).Project.PropertyGroup.Version
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $taskSdk restore (Join-Path $taskRoot 'src\Shiguang.csproj') --configfile (Join-Path $taskRoot 'NuGet.Config') -p:NuGetAudit=false --nologo
if ($LASTEXITCODE -ne 0) { throw '还原失败' }
$taskOutput = Join-Path $taskRoot ('成品\运行程序-' + $taskVersion)
& $taskSdk publish (Join-Path $taskRoot 'src\Shiguang.csproj') -c Release --no-restore --nologo -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw '构建失败，请先关闭正在运行的程序' }
$runtime = Join-Path $taskOutput '.runtime'
function Copy-RuntimeTree([string]$Source, [string]$Destination) {
    foreach ($runtimeFile in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        $runtimeRelative = [IO.Path]::GetRelativePath($Source, $runtimeFile.FullName)
        $runtimeTarget = Join-Path $Destination $runtimeRelative
        if (Test-Path -LiteralPath $runtimeTarget) {
            $runtimeExisting = Get-Item -LiteralPath $runtimeTarget
            if ($runtimeExisting.Length -eq $runtimeFile.Length -and (Get-FileHash -LiteralPath $runtimeTarget).Hash -eq (Get-FileHash -LiteralPath $runtimeFile.FullName).Hash) { continue }
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $runtimeTarget) -Force | Out-Null
        Copy-Item -LiteralPath $runtimeFile.FullName -Destination $runtimeTarget -Force
    }
}
New-Item -ItemType Directory -Path (Join-Path $runtime 'shared'),(Join-Path $runtime 'host') -Force | Out-Null
foreach ($framework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
    $frameworkRoot = Join-Path $taskDotnetRoot "shared\$framework"
    $frameworkVersion = Get-ChildItem -LiteralPath $frameworkRoot -Directory | Where-Object Name -Match '^10\.0\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $frameworkVersion) { throw ('缺少 .NET 10 Windows 桌面运行时：' + $framework) }
    $frameworkTarget = Join-Path $runtime "shared\$framework"
    New-Item -ItemType Directory -Path $frameworkTarget -Force | Out-Null
    Copy-RuntimeTree $frameworkVersion.FullName (Join-Path $frameworkTarget $frameworkVersion.Name)
}
$taskHostVersion = Get-ChildItem -LiteralPath (Join-Path $taskDotnetRoot 'host\fxr') -Directory | Where-Object Name -Match '^10\.0\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $taskHostVersion) { throw '缺少 .NET 10 hostfxr。请安装 Windows x64 SDK。' }
$taskHostTarget = Join-Path $runtime 'host\fxr'
New-Item -ItemType Directory -Path $taskHostTarget -Force | Out-Null
Copy-RuntimeTree $taskHostVersion.FullName (Join-Path $taskHostTarget $taskHostVersion.Name)
foreach ($file in @('dotnet.exe','LICENSE.txt','ThirdPartyNotices.txt')) {
    Copy-Item -LiteralPath (Join-Path $taskDotnetRoot $file) -Destination $runtime -Force
}
Write-Output ('完成：' + (Join-Path $taskOutput '拾光照片.exe'))
