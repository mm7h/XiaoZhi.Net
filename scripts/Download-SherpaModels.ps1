[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $PSScriptRoot "..\\models"),
    [string[]] $Models,
    [switch] $All,
    [switch] $KeepArchives,
    [switch] $Force,
    [switch] $UseCurl,
    [string] $TarPath = "tar.exe",
    [string] $SevenZipPath = "7z.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$releaseBaseUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download"

# 两个下载入口共用清单；分号分隔资源/映射，逗号分隔候选文件，目录映射以 / 结尾。
$availableModels = @(Import-Csv -LiteralPath (Join-Path $PSScriptRoot "SherpaModels.psv") -Delimiter '|' -Encoding UTF8 | ForEach-Object {
    $entry = $_
    $assets = @($entry.Assets -split ';' | ForEach-Object {
        $parts = $_ -split '=', 2
        [pscustomobject]@{
            Name       = ($parts[0] -split '/')[-1]
            OutputName = if ($parts.Count -gt 1) { $parts[1] } else { "" }
            Url        = "$releaseBaseUrl/$($parts[0])"
        }
    })
    $mappings = @(if ($entry.Mappings -ne '-') {
        $entry.Mappings -split ';' | ForEach-Object {
            $parts = $_ -split '=', 2
            [pscustomobject]@{
                Destination = $parts[0].TrimEnd('/')
                Sources     = @($parts[1] -split ',')
                Directory   = $parts[0].EndsWith('/')
                Optional    = $false
            }
        }
    })
    [pscustomobject]@{
        Name        = $entry.Name
        Category    = $entry.Category
        Directory   = $entry.Directory
        Assets      = $assets
        Mappings    = $mappings
        Required    = @($entry.Required -split ',')
        ManualFiles = @(if ($entry.ManualFiles -ne '-') { $entry.ManualFiles -split ',' })
    }
})

function Test-ModelInstalled {
    param(
        [object] $Model,
        [string] $TargetDirectory
    )

    foreach ($required in $Model.Required) {
        if (-not (Test-Path -LiteralPath (Join-Path $TargetDirectory $required))) {
            return $false
        }
    }

    return $true
}

function Find-ModelItem {
    param(
        [string] $Directory,
        [string[]] $Names,
        [bool] $IsDirectory
    )

    foreach ($name in $Names) {
        $item = Get-ChildItem -LiteralPath $Directory -Recurse -Force | Where-Object {
            $_.Name -ceq $name -and ($IsDirectory -eq $_.PSIsContainer)
        } | Select-Object -First 1
        if ($null -ne $item) {
            return $item
        }
    }

    return $null
}

function Move-ModelItem {
    param(
        [object] $Mapping,
        [string] $TargetDirectory,
        [bool] $Overwrite
    )

    $destination = Join-Path $TargetDirectory $Mapping.Destination
    $source = Find-ModelItem $TargetDirectory $Mapping.Sources $Mapping.Directory
    if ($null -eq $source) {
        if ($Mapping.Optional) {
            return
        }

        throw "未在解压后的文件中找到 $($Mapping.Destination)，候选源文件：$($Mapping.Sources -join ', ')。"
    }

    if ([IO.Path]::GetFullPath($source.FullName) -ceq [IO.Path]::GetFullPath($destination)) {
        return
    }

    if (Test-Path -LiteralPath $destination) {
        if (-not $Overwrite) {
            Write-Verbose "保留已有文件：$destination"
            return
        }

        if ($Mapping.Directory) {
            Write-Warning "保留已有目录：$destination"
            return
        }

        Remove-Item -LiteralPath $destination -Force
    }

    Move-Item -LiteralPath $source.FullName -Destination $destination
}

function Download-DirectAsset {
    param(
        [object] $Asset,
        [string] $TargetDirectory,
        [bool] $Overwrite
    )

    $outputPath = Join-Path $TargetDirectory $Asset.OutputName
    if ((Test-Path -LiteralPath $outputPath) -and -not $Overwrite) {
        Write-Verbose "保留已有文件：$outputPath"
        return
    }

    Write-Host "下载 $($Asset.Name)"
    if ($UseCurl) {
        & curl.exe -L --fail --retry 5 --retry-delay 5 --connect-timeout 30 --output $outputPath $Asset.Url
        if ($LASTEXITCODE -ne 0) {
            throw "下载 $($Asset.Name) 失败。"
        }
    }
    else {
        try {
            Invoke-WebRequest -Uri $Asset.Url -OutFile $outputPath
        }
        catch {
            Write-Warning "PowerShell 下载失败，改用 curl 重试：$($Asset.Name)"
            & curl.exe -L --fail --retry 5 --retry-delay 5 --connect-timeout 30 --output $outputPath $Asset.Url
            if ($LASTEXITCODE -ne 0) {
                throw "下载 $($Asset.Name) 失败。"
            }
        }
    }
}

function Expand-ModelArchive {
    param(
        [object] $Asset,
        [string] $TargetDirectory,
        [bool] $PreserveArchive
    )

    $archivePath = if ($PreserveArchive) {
        $archiveDirectory = Join-Path $Destination ".sherpa-archives"
        New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
        Join-Path $archiveDirectory $Asset.Name
    }
    else {
        Join-Path ([IO.Path]::GetTempPath()) ("xiaozhi-sherpa-" + [Guid]::NewGuid().ToString("N") + "-" + $Asset.Name)
    }

    try {
        Write-Host "下载 $($Asset.Name)"
        if ($UseCurl) {
            & curl.exe -L --fail --retry 5 --retry-delay 5 --connect-timeout 30 --output $archivePath $Asset.Url
            if ($LASTEXITCODE -ne 0) {
                throw "下载 $($Asset.Name) 失败。"
            }
        }
        else {
            try {
                Invoke-WebRequest -Uri $Asset.Url -OutFile $archivePath
            }
            catch {
                Write-Warning "PowerShell 下载失败，改用 curl 重试：$($Asset.Name)"
                & curl.exe -L --fail --retry 5 --retry-delay 5 --connect-timeout 30 --output $archivePath $Asset.Url
                if ($LASTEXITCODE -ne 0) {
                    throw "下载 $($Asset.Name) 失败。"
                }
            }
        }
        Write-Host "解压 $($Asset.Name)"
        if ($archiveTool -eq "tar") {
            & $TarPath -xf $archivePath -C $TargetDirectory --strip-components=1
            if ($LASTEXITCODE -ne 0) { throw "无法解压 $($Asset.Name)。" }
        }
        else {
            # 7-Zip 分两步解开 bzip2 和 tar，随后由既有映射整理文件，不依赖 tar/bzip2。
            $extractDirectory = Join-Path ([IO.Path]::GetTempPath()) ("xiaozhi-sherpa-" + [Guid]::NewGuid().ToString("N"))
            New-Item -ItemType Directory -Path $extractDirectory | Out-Null
            $tarFile = Join-Path $extractDirectory ([IO.Path]::GetFileNameWithoutExtension($archivePath))
            try {
                & $SevenZipPath e $archivePath "-o$extractDirectory" -y | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "无法解开 $($Asset.Name) 的 bzip2 压缩。" }
                & $SevenZipPath x $tarFile "-o$TargetDirectory" -y | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "无法解开 $($Asset.Name) 的 tar 归档。" }
            }
            finally {
                if (Test-Path -LiteralPath $tarFile -PathType Leaf) { Remove-Item -LiteralPath $tarFile -Force }
            }
        }
    }
    finally {
        if (-not $PreserveArchive -and (Test-Path -LiteralPath $archivePath)) {
            Remove-Item -LiteralPath $archivePath -Force
        }
    }
}

$requestedModelNames = @($Models | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if (-not $All -and $requestedModelNames.Count -eq 0) {
    Write-Host "请使用 -Models 或 -All 选择要下载的模型。"
    $availableModels | Sort-Object Category, Name | Format-Table Category, Name, Directory -AutoSize
    Write-Host "示例："
    Write-Host "  pwsh -File .\\scripts\\Download-SherpaModels.ps1 -Models Silero,OnlineParaformer,Kokoro"
    Write-Host "  pwsh -File .\\scripts\\Download-SherpaModels.ps1 -All"
    return
}

$selectedModels = if ($All) {
    $availableModels
}
else {
    $unknownModels = @($requestedModelNames | Where-Object { $_ -notin $availableModels.Name })
    if ($unknownModels.Count -gt 0) {
        throw "未知模型：$($unknownModels -join ', ')。不带参数运行脚本可查看完整列表。"
    }

    @($availableModels | Where-Object { $_.Name -in $requestedModelNames })
}

$archiveTool = ""
if (@($selectedModels.Assets | Where-Object { [string]::IsNullOrEmpty($_.OutputName) }).Count -gt 0) {
    # 先验证实际解压能力，避免下载大文件后才发现平台自带 tar 不支持 bzip2。
    $probeArchive = Join-Path $PSScriptRoot "tar-bzip2-probe.tar.bz2"
    if (Get-Command $TarPath -ErrorAction SilentlyContinue) {
        & $TarPath -tf $probeArchive 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $archiveTool = "tar" }
    }
    if (-not $archiveTool) {
        if (-not (Get-Command $SevenZipPath -ErrorAction SilentlyContinue) -and $SevenZipPath -eq "7z.exe") {
            $installedSevenZip = Join-Path ${env:ProgramFiles} "7-Zip/7z.exe"
            if (Test-Path -LiteralPath $installedSevenZip) { $SevenZipPath = $installedSevenZip }
        }
        if (Get-Command $SevenZipPath -ErrorAction SilentlyContinue) {
            & $SevenZipPath t $probeArchive 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { $archiveTool = "7zip" }
        }
    }
    if (-not $archiveTool) {
        throw "没有可用的 .tar.bz2 解压工具。请安装 7-Zip（可用 -SevenZipPath 指定路径），或提供支持 bzip2 的 tar（-TarPath）；无需 Python。"
    }
}

$destinationPath = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null

foreach ($model in $selectedModels) {
    $targetDirectory = Join-Path $destinationPath $model.Directory
    $installed = Test-ModelInstalled $model $targetDirectory
    if ($installed -and -not $Force) {
        Write-Host "已就绪，跳过 $($model.Name)：$targetDirectory"
    }
    else {
        if ((Test-Path -LiteralPath $targetDirectory) -and -not $installed -and -not $Force) {
            throw "目标目录不完整：$targetDirectory。请检查已有内容，确认需要覆盖后加 -Force。"
        }

        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Write-Host "处理 $($model.Name) -> $targetDirectory"
        foreach ($asset in $model.Assets) {
            if ([string]::IsNullOrEmpty($asset.OutputName)) {
                Expand-ModelArchive $asset $targetDirectory $KeepArchives.IsPresent
            }
            else {
                Download-DirectAsset $asset $targetDirectory $Force.IsPresent
            }
        }

        foreach ($mapping in $model.Mappings) {
            Move-ModelItem $mapping $targetDirectory $Force.IsPresent
        }

        if (-not (Test-ModelInstalled $model $targetDirectory)) {
            throw "$($model.Name) 安装后缺少项目所需文件。"
        }
    }

    if ($model.ManualFiles.Count -gt 0) {
        Write-Warning "$($model.Name) 还需要由你提供：$($model.ManualFiles -join ', ')。"
    }
}
