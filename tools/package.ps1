# 产出四个分发包：内置 .NET 11 桌面运行时 / 框架依赖，各一份压缩包与一份 MSI 安装包
# 版本号取自 src/SnipTrace/SnipTrace.csproj 的 Version，文件名携带版本与处理器架构
# 用法：pwsh -File tools/package.ps1 [-Configuration Release] [-Runtime win-x64] [-Arch x64] [-OutputDir dist\packages]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Arch = 'x64',
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\packages')
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $root 'src\SnipTrace\SnipTrace.csproj'
$publishRoot = Join-Path $root 'dist\publish'
$wxsPath = Join-Path $root 'installer\SnipTrace.wxs'

if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force $OutputDir | Out-Null }
$OutputDir = (Resolve-Path $OutputDir).Path
Get-ChildItem $OutputDir -Filter *.wixpdb -ErrorAction SilentlyContinue | Remove-Item -Force
if (-not (Test-Path $publishRoot)) { New-Item -ItemType Directory -Force $publishRoot | Out-Null }

$version = (& dotnet msbuild $projectPath -getProperty:Version -v:q).Trim()
if (-not $version) { throw '读取版本号失败：检查 SnipTrace.csproj 的 Version' }

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw '未找到 WiX，请先执行：dotnet tool install --global wix --version 5.*'
}

# 安装向导（用户协议 + 安装目录选择）依赖 WixUI 扩展，版本与 WiX 主程序保持一致
$wixVersion = (& wix --version).Split('+')[0].Trim()
if (-not (& wix extension list -g | Select-String 'WixToolset\.UI\.wixext')) {
    Write-Host "安装 WiX UI 扩展 $wixVersion …"
    & wix extension add "WixToolset.UI.wixext/$wixVersion" -g
    if ($LASTEXITCODE -ne 0) { throw "安装 WiX UI 扩展失败：WixToolset.UI.wixext/$wixVersion" }
}
$uiExtDll = Get-ChildItem (Join-Path $env:USERPROFILE '.wix\extensions\WixToolset.UI.wixext') `
    -Recurse -Filter 'WixToolset.UI.wixext.dll' -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $uiExtDll) { throw '未找到 WixToolset.UI.wixext 扩展程序集' }

Write-Host "版本 $version ｜ 运行时 $Runtime ｜ 架构 $Arch"

$variants = @(
    @{ Key = 'selfcontained'; SelfContained = 'true';  Label = '内置 .NET 11 桌面运行时' },
    @{ Key = 'fx-dependent';  SelfContained = 'false'; Label = '需系统安装 .NET 11 桌面运行时' }
)

$results = @()

foreach ($variant in $variants) {
    $publishDir = Join-Path $publishRoot "$Runtime-$($variant.Key)"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    Write-Host "发布 $($variant.Key) …"
    & dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained $variant.SelfContained `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publishDir -v:q --nologo
    if ($LASTEXITCODE -ne 0) { throw "发布失败：$($variant.Key)" }

    $baseName = "SnipTrace-$version-$Runtime-$($variant.Key)"

    # 压缩包：包内顶层目录固定为 SnipTrace，解压即得可直接运行的程序目录
    $stageRoot = Join-Path $publishRoot "stage-$($variant.Key)"
    $stageApp = Join-Path $stageRoot 'SnipTrace'
    if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
    Copy-Item $publishDir $stageApp -Recurse

    $zipPath = Join-Path $OutputDir "$baseName.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stageRoot, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Remove-Item $stageRoot -Recurse -Force

    # 安装包：WiX 直接把发布目录收集进 Program Files\SnipTrace
    $msiPath = Join-Path $OutputDir "$baseName-setup.msi"
    if (Test-Path $msiPath) { Remove-Item $msiPath -Force }
    & wix build $wxsPath -arch $Arch -d "Version=$version" `
        -bindpath "publish=$publishDir" -bindpath "installer=$root\installer" `
        -ext $uiExtDll -o $msiPath
    if ($LASTEXITCODE -ne 0) { throw "生成安装包失败：$($variant.Key)" }

    # WiX 会顺带产出调试库，发布目录里不需要
    $wixpdb = [System.IO.Path]::ChangeExtension($msiPath, '.wixpdb')
    if (Test-Path $wixpdb) { Remove-Item $wixpdb -Force }

    $results += [pscustomobject]@{
        类型   = '压缩包'
        文件   = Split-Path -Leaf $zipPath
        说明   = $variant.Label
        大小MB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    }
    $results += [pscustomobject]@{
        类型   = '安装包'
        文件   = Split-Path -Leaf $msiPath
        说明   = $variant.Label
        大小MB = [math]::Round((Get-Item $msiPath).Length / 1MB, 1)
    }
}

Write-Host ''
$results | Format-Table -AutoSize
Write-Host "已输出到 $OutputDir"
