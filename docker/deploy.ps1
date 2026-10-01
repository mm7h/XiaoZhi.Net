#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Up', 'Down', 'Status', 'Logs', 'Check')]
    [string] $Action = 'Up',
    [string] $PublicHost,
    [ValidateRange(1, 65535)] [int] $WebSocketPort = 4530,
    [ValidateRange(1, 65535)] [int] $OtaPort = 4531,
    [string] $ResourceSource,
    [string] $RuntimeDirectory = (Join-Path $PSScriptRoot 'runtime'),
    [ValidatePattern('^[a-z0-9][a-z0-9_-]*$')]
    [string] $ProjectName = 'xiaozhi-samples',
    [ValidateRange(30, 3600)] [int] $WaitTimeout = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$RuntimeDirectory = [IO.Path]::GetFullPath($RuntimeDirectory)
$settingsPath = Join-Path $RuntimeDirectory 'deployment.json'
$composePath = Join-Path $PSScriptRoot 'compose.yaml'
$savedEnvironment = @{}
$composeReady = $false
$startupAttempted = $false

function Invoke-Docker {
    param([string[]] $Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed (exit $LASTEXITCODE): docker $($Arguments -join ' ')" }
}

function Invoke-Compose {
    param([string[]] $Arguments)
    Invoke-Docker -Arguments (@('compose', '--project-name', $ProjectName, '--file', $composePath) + $Arguments)
}

function Read-JsonObject {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required file missing: $Path" }
    try { $value = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable }
    catch { throw "Invalid JSON configuration: $Path" }
    if ($value -isnot [System.Collections.IDictionary]) { throw "JSON root must be an object: $Path" }
    return $value
}

function Merge-Configuration {
    param([System.Collections.IDictionary] $Target, [System.Collections.IDictionary] $Source)
    foreach ($key in $Source.Keys) {
        if ($Target[$key] -is [System.Collections.IDictionary] -and $Source[$key] -is [System.Collections.IDictionary]) {
            Merge-Configuration -Target $Target[$key] -Source $Source[$key]
        }
        else { $Target[$key] = $Source[$key] }
    }
}

function Get-EffectiveConfiguration {
    $directory = Join-Path $RuntimeDirectory 'Configs'
    $config = Read-JsonObject (Join-Path $directory 'config.json')
    # Match the server's OrdinalIgnoreCase fragment ordering.
    [string[]] $fragments = @(Get-ChildItem -LiteralPath $directory -File -Filter 'config_*.json' | ForEach-Object Name)
    [Array]::Sort($fragments, [StringComparer]::OrdinalIgnoreCase)
    foreach ($fragment in $fragments) {
        Merge-Configuration $config (Read-JsonObject (Join-Path $directory $fragment))
    }
    $environmentPath = Join-Path $directory 'config.Production.json'
    if (Test-Path -LiteralPath $environmentPath) { Merge-Configuration $config (Read-JsonObject $environmentPath) }
    return $config
}

function Initialize-Resources {
    $configDirectory = Join-Path $RuntimeDirectory 'Configs'
    if (Test-Path -LiteralPath (Join-Path $configDirectory 'config.json')) {
        Write-Host "Keeping existing resources and configuration: $RuntimeDirectory"
        return
    }
    if (-not $ResourceSource) {
        $script:ResourceSource = Join-Path $repositoryRoot 'demo/XiaoZhi.Net.Sample.Server/bin/Debug/net8.0'
    }
    $source = [IO.Path]::GetFullPath($ResourceSource)
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Resource directory missing: $source. Supply -ResourceSource with Configs, models and optional musics directories."
    }
    $sourceConfigs = @(Get-ChildItem -LiteralPath $source -Directory | Where-Object Name -IEQ 'Configs')
    if ($sourceConfigs.Count -ne 1) { throw "Expected one Configs directory in $source (case-insensitive)." }
    $null = Read-JsonObject (Join-Path $sourceConfigs[0].FullName 'config.json')
    if (-not (Test-Path -LiteralPath (Join-Path $source 'models') -PathType Container)) {
        throw "Model directory missing: $(Join-Path $source 'models')"
    }
    # Copy into a fresh staging directory so an interrupted import never looks complete.
    $staging = Join-Path $RuntimeDirectory ('.import-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Copy-Item -LiteralPath $sourceConfigs[0].FullName -Destination (Join-Path $staging 'Configs') -Recurse
    Copy-Item -LiteralPath (Join-Path $source 'models') -Destination (Join-Path $staging 'models') -Recurse
    if (Test-Path -LiteralPath (Join-Path $source 'musics') -PathType Container) {
        Copy-Item -LiteralPath (Join-Path $source 'musics') -Destination (Join-Path $staging 'musics') -Recurse
    }
    else { New-Item -ItemType Directory -Path (Join-Path $staging 'musics') | Out-Null }

    $productionPath = Join-Path $staging 'Configs/config.Production.json'
    $production = if (Test-Path -LiteralPath $productionPath) { Read-JsonObject $productionPath } else { @{} }
    Merge-Configuration $production @{
        WebSocketServerOption = @{ IP = '0.0.0.0'; Port = 4530; Path = '/xiaozhi/v1/' }
    }
    $production | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $productionPath -Encoding utf8NoBOM
    foreach ($name in @('models', 'musics', 'Configs')) {
        $destination = Join-Path $RuntimeDirectory $name
        if (Test-Path -LiteralPath $destination) {
            throw "Partial or manually prepared resource directory exists: $destination. Complete it or choose a fresh -RuntimeDirectory. Import is retained at $staging."
        }
    }
    foreach ($name in @('models', 'musics', 'Configs')) {
        Move-Item -LiteralPath (Join-Path $staging $name) -Destination (Join-Path $RuntimeDirectory $name)
    }
    # Only remove the now-empty, explicitly created staging directory.
    Remove-Item -LiteralPath $staging -Force
    Write-Host "Imported configuration, models and music into $RuntimeDirectory"
}

function Test-Resources {
    $config = Get-EffectiveConfiguration
    foreach ($name in @('models', 'musics', 'Configs/assets')) {
        if (-not (Test-Path -LiteralPath (Join-Path $RuntimeDirectory $name) -PathType Container)) {
            throw "Required resource directory missing: $name"
        }
    }
    $socket = $config['WebSocketServerOption']
    if ($null -eq $socket -or $socket['IP'] -ne '0.0.0.0' -or [int]$socket['Port'] -ne 4530 -or $socket['Path'] -ne '/xiaozhi/v1/') {
        throw 'Container configuration must listen on 0.0.0.0:4530/xiaozhi/v1/. Set this in Configs/config.Production.json; host ports are controlled by script parameters.'
    }
    foreach ($provider in @('VAD', 'ASR', 'LLM', 'TTS', 'Intent')) {
        if (-not $config['SelectedSettings'] -or -not $config['SelectedSettings'][$provider]) {
            throw "Missing SelectedSettings.$provider"
        }
    }
    if ($config['SelectedSettings']['VAD'] -eq 'SileroNative') {
        if (-not (Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'models/vad/silero-native/model.onnx') -PathType Leaf)) {
            throw 'Required VAD model missing: models/vad/silero-native/model.onnx'
        }
    }
    foreach ($entry in @('BindCodePromptFilePath', 'BindNotFoundFilePath', 'BindCodeDigitFolderPath')) {
        if ($config['DeviceBindSetting'] -and $config['DeviceBindSetting'][$entry]) {
            $path = [string]$config['DeviceBindSetting'][$entry]
            if ($path -notmatch '^Configs/assets/') { throw "DeviceBindSetting.$entry must use a Linux-relative Configs/assets/ path." }
            if (-not (Test-Path -LiteralPath (Join-Path $RuntimeDirectory $path))) { throw "Prompt resource missing: $path" }
        }
    }
}

function Test-Interfaces {
    $otaBase = "http://127.0.0.1:$OtaPort"
    $health = Invoke-RestMethod "$otaBase/health" -TimeoutSec 10
    if ($health.status -ne 'ok') { throw 'OTA health check failed.' }
    $ota = Invoke-RestMethod "$otaBase/xiaozhi/ota" -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 10
    $expectedUrl = "ws://${PublicHost}:${WebSocketPort}/xiaozhi/v1/"
    if ($ota.websocket.url -ne $expectedUrl) { throw "OTA returned an unexpected WebSocket URL (expected $expectedUrl)." }
    $config = Invoke-RestMethod "$otaBase/api/config" -TimeoutSec 10
    if ($config.web_socket_server_option.port -ne 4530) { throw 'OTA configuration endpoint returned an unexpected container port.' }
    $client = [Net.WebSockets.ClientWebSocket]::new()
    $timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(10))
    try {
        $client.Options.SetRequestHeader('device-id', 'docker-deployment-check')
        $client.Options.SetRequestHeader('client-id', 'docker-deployment-check')
        if ($ota.websocket.token) { $client.Options.SetRequestHeader('Authorization', "Bearer $($ota.websocket.token)") }
        $null = $client.ConnectAsync([Uri]"ws://127.0.0.1:$WebSocketPort/xiaozhi/v1/", $timeout.Token).GetAwaiter().GetResult()
        if ($client.State -ne [Net.WebSockets.WebSocketState]::Open) { throw 'WebSocket handshake failed.' }
        $null = $client.CloseOutputAsync([Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'deployment check', $timeout.Token).GetAwaiter().GetResult()
    }
    finally { $client.Dispose(); $timeout.Dispose() }
    Write-Host "Checks passed. OTA: http://${PublicHost}:${OtaPort}/xiaozhi/ota; WebSocket: $expectedUrl"
}

try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker CLI is not installed.' }
    $engine = & docker info --format '{{.OSType}} {{.Architecture}}' 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Docker engine is unavailable. Start Docker Desktop with Linux containers, or start the Linux Docker daemon.' }
    if ("$engine" -ne 'linux x86_64') { throw "Expected a Linux x64 Docker engine; found: $engine" }
    $null = Invoke-Docker -Arguments @('compose', 'version')

    $settings = if (Test-Path -LiteralPath $settingsPath) { Read-JsonObject $settingsPath } else { @{} }
    if (-not $PSBoundParameters.ContainsKey('PublicHost') -and $settings['PublicHost']) { $PublicHost = $settings['PublicHost'] }
    if (-not $PSBoundParameters.ContainsKey('WebSocketPort') -and $settings['WebSocketPort']) { $WebSocketPort = [int]$settings['WebSocketPort'] }
    if (-not $PSBoundParameters.ContainsKey('OtaPort') -and $settings['OtaPort']) { $OtaPort = [int]$settings['OtaPort'] }
    if (-not $PSBoundParameters.ContainsKey('ProjectName') -and $settings['ProjectName']) { $ProjectName = $settings['ProjectName'] }
    if (-not $PublicHost) { throw 'Specify -PublicHost with the host LAN IP or DNS name on first deployment.' }
    $hostName = $PublicHost.Trim('[', ']')
    $hostType = [Uri]::CheckHostName($hostName)
    if ($hostType -eq [UriHostNameType]::Unknown -or $PublicHost -match '[\s/@?#$]') { throw '-PublicHost must contain only an IP address or DNS name, without scheme, port or path.' }
    if ($hostType -eq [UriHostNameType]::IPv6) { $PublicHost = "[$hostName]" }
    if ($WebSocketPort -lt 1 -or $WebSocketPort -gt 65535 -or $OtaPort -lt 1 -or $OtaPort -gt 65535 -or $WebSocketPort -eq $OtaPort) {
        throw 'WebSocketPort and OtaPort must be distinct ports between 1 and 65535.'
    }
    if ($ProjectName -notmatch '^[a-z0-9][a-z0-9_-]*$') { throw 'Invalid saved ProjectName.' }
    $variables = @{
        XIAOZHI_BUILD_CONTEXT = $repositoryRoot
        XIAOZHI_RUNTIME_DIR = $RuntimeDirectory
        XIAOZHI_PUBLIC_HOST = $PublicHost
        XIAOZHI_WS_PORT = "$WebSocketPort"
        XIAOZHI_OTA_PORT = "$OtaPort"
        XIAOZHI_PROJECT_NAME = $ProjectName
    }
    foreach ($key in $variables.Keys) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $variables[$key], 'Process')
    }
    $composeReady = $true
    switch ($Action) {
        'Up' {
            New-Item -ItemType Directory -Path $RuntimeDirectory -Force | Out-Null
            Initialize-Resources
            Test-Resources
            Invoke-Compose -Arguments @('config', '--quiet')
            $settings = @{ PublicHost = $PublicHost; WebSocketPort = $WebSocketPort; OtaPort = $OtaPort; ProjectName = $ProjectName }
            $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM
            Write-Host 'Building sample-server and ota-server images (first FFmpeg build may take several minutes).'
            Invoke-Compose -Arguments @('build')
            $startupAttempted = $true
            Invoke-Compose -Arguments @('up', '--detach', '--wait', '--wait-timeout', "$WaitTimeout")
            Test-Interfaces
        }
        'Down' { Invoke-Compose -Arguments @('down') }
        'Status' { Invoke-Compose -Arguments @('ps', '--all') }
        'Logs' { Invoke-Compose -Arguments @('logs', '--tail', '100', '--follow') }
        'Check' {
            Test-Resources
            $statusJson = & docker compose --project-name $ProjectName --file $composePath ps --all --format json
            if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Compose services.' }
            $services = @($statusJson | Where-Object { $_ } | ForEach-Object { ConvertFrom-Json $_ } | ForEach-Object { $_ })
            foreach ($name in @('sample-server', 'ota-server')) {
                $service = @($services | Where-Object Service -EQ $name)
                if ($service.Count -ne 1 -or $service[0].State -ne 'running' -or $service[0].Health -ne 'healthy') {
                    throw "Service $name is not running and healthy."
                }
            }
            Test-Interfaces
        }
    }
}
catch {
    [Console]::Error.WriteLine("Deployment failed: $($_.Exception.Message)")
    if ($composeReady -and $startupAttempted) {
        & docker compose --project-name $ProjectName --file $composePath ps --all
        & docker compose --project-name $ProjectName --file $composePath logs --tail 60
    }
    exit 1
}
finally {
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
}
