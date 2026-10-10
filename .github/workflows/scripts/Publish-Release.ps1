#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]*$')] [string] $Version,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../../../artifacts/release')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$archiveName = "XiaoZhi.Net-$Version-$RuntimeIdentifier.zip"
$archivePath = Join-Path $OutputDirectory $archiveName
if (Test-Path -LiteralPath $archivePath) { throw "运行包已存在，请选择新的输出目录：$archivePath" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '请先安装 .NET 8 SDK。' }
$profiles = @{
    'win-x64' = 'WinX64'; 'win-arm64' = 'WinArm64'
    'linux-x64' = 'LinuxX64'; 'linux-arm64' = 'LinuxArm64'
    'osx-x64' = 'OsxX64'; 'osx-arm64' = 'OsxArm64'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$buildDirectory = Join-Path $OutputDirectory ('.build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildDirectory | Out-Null
$packageDirectory = Join-Path $buildDirectory 'package'
$serverDirectory = Join-Path $packageDirectory 'server'
$otaDirectory = Join-Path $packageDirectory 'ota'
& dotnet publish (Join-Path $repositoryRoot 'demo/XiaoZhi.Net.Sample.Server/XiaoZhi.Net.Sample.Server.csproj') `
    -c Release -r $RuntimeIdentifier "-p:PublishProfile=$($profiles[$RuntimeIdentifier])" `
    "-p:InformationalVersion=$Version" -o $serverDirectory
if ($LASTEXITCODE -ne 0) { throw '主服务发布失败。' }
& dotnet publish (Join-Path $repositoryRoot 'demo/XiaoZhi.Net.Sample.OTA.Server/XiaoZhi.Net.Sample.OTA.Server.csproj') `
    -c Release -r $RuntimeIdentifier --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:PublishReadyToRun=false `
    -p:DebugType=embedded "-p:InformationalVersion=$Version" -o $otaDirectory
if ($LASTEXITCODE -ne 0) { throw 'OTA 服务发布失败。' }

# 两个示例项目按现有规则发布自己的配置，其余资源使用明确的文件清单。
# 资源目录保留在程序外；发布阶段不下载 FFmpeg、模型或复制用户数据。
foreach ($directory in @('models', 'ffmpeg', 'musics', 'data', 'logs')) {
    New-Item -ItemType Directory -Path (Join-Path $serverDirectory $directory) | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $packageDirectory 'LICENSE')
New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'docker'), (Join-Path $packageDirectory 'scripts'), (Join-Path $packageDirectory 'docs') | Out-Null
foreach ($file in @('deploy.sh', 'deploy.ps1', 'compose.yaml', 'Dockerfile.release', 'Dockerfile.release.dockerignore')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "docker/$file") -Destination (Join-Path $packageDirectory 'docker')
}
foreach ($file in @('Download-SherpaModels.ps1', 'Download-SherpaModels.sh', 'SherpaModels.psv', 'tar-bzip2-probe.tar.bz2')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts/$file") -Destination (Join-Path $packageDirectory 'scripts')
}
foreach ($file in @('00.configuration-file.md', '01.extend-custom-model.md', '02.sherpa-models.md', '03.server-development.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs/$file") -Destination (Join-Path $packageDirectory 'docs')
}
@{
    Version = $Version; RuntimeIdentifier = $RuntimeIdentifier
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageDirectory 'package.json') -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $packageDirectory 'README.md') -Encoding utf8NoBOM -Value @'
# XiaoZhi.Net 运行包

部署步骤见 [服务部署](docs/03.server-development.md)。

建议将 server/Configs 的 JSON 复制到外部目录后填写密钥，启动时传入 --config <config.json路径>。
models、ffmpeg、musics、data、logs 保留为空目录；用户需自行下载所选模型和对应平台的 FFmpeg 动态库。
FFmpeg 资源：https://github.com/mm7h/XiaoZhi.Net/releases#release-resources
从 server/ 启动主服务，从 ota/ 启动 OTA，并让 OTA 指向同一配置目录。
Linux/macOS 解压后请执行：chmod +x server/XiaoZhi.Net.Sample.Server ota/XiaoZhi.Net.Sample.OTA.Server
Docker 仅支持 linux-x64 包，从本目录执行 sh docker/deploy.sh --public-host <服务器IP>，无需安装 PowerShell。
'@
$extension = if ($RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }
foreach ($file in @("$serverDirectory/XiaoZhi.Net.Sample.Server$extension", "$otaDirectory/XiaoZhi.Net.Sample.OTA.Server$extension")) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "发布目录缺少单文件程序：$file" }
}
foreach ($file in @("$serverDirectory/Configs/config.json", "$otaDirectory/appsettings.json")) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "示例项目未发布配置文件：$file" }
}
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archivePath
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $archiveName" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding utf8NoBOM
Write-Host "已生成：$archivePath"
