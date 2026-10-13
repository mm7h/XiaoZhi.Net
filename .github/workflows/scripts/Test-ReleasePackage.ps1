#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ArchivePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('xiaozhi-package-' + [Guid]::NewGuid().ToString('N'))
$process = $null
try {
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $directory
    $metadata = Get-Content (Join-Path $directory 'package.json') -Raw | ConvertFrom-Json
    $extension = if ($metadata.RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }
    $executable = Join-Path $directory "ota/XiaoZhi.Net.Sample.OTA.Server$extension"
    if (-not $extension) {
        & chmod +x $executable
        if ($LASTEXITCODE -ne 0) { throw '无法设置 OTA 可执行权限。' }
    }
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.WorkingDirectory = Join-Path $directory 'ota'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add('--urls')
    $start.ArgumentList.Add("http://127.0.0.1:$port")
    $start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $directory '.net'
    $process = [Diagnostics.Process]::Start($start)
    $healthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "OTA 启动失败，退出码：$($process.ExitCode)" }
        try {
            $result = Invoke-RestMethod "http://127.0.0.1:$port/health" -TimeoutSec 2
            if ($result.status -eq 'ok') { $healthy = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $healthy) { throw 'OTA /health 检查超时。' }
    Write-Host "OTA 解压启动检查通过：$($metadata.RuntimeIdentifier)"
} finally {
    if ($process -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    if (Test-Path -LiteralPath $directory) {
        $resolved = (Resolve-Path -LiteralPath $directory).Path
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'xiaozhi-package-*') {
            throw "拒绝清理临时目录之外的路径：$resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
