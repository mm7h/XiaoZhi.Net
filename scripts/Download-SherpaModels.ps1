[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $PSScriptRoot "..\\models"),
    [string[]] $Models,
    [switch] $All,
    [switch] $KeepArchives,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$releaseBaseUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download"

function New-Asset {
    param(
        [string] $Release,
        [string] $Name,
        [string] $OutputName = ""
    )

    [pscustomobject]@{
        Name       = $Name
        OutputName = $OutputName
        Url        = "$releaseBaseUrl/$Release/$Name"
    }
}

function New-Mapping {
    param(
        [string] $Destination,
        [string[]] $Sources,
        [switch] $Directory,
        [switch] $Optional
    )

    [pscustomobject]@{
        Destination = $Destination
        Sources     = $Sources
        Directory   = $Directory.IsPresent
        Optional    = $Optional.IsPresent
    }
}

function New-Model {
    param(
        [string] $Name,
        [string] $Category,
        [string] $Directory,
        [object[]] $Assets,
        [object[]] $Mappings,
        [string[]] $Required,
        [string[]] $ManualFiles = @()
    )

    [pscustomobject]@{
        Name        = $Name
        Category    = $Category
        Directory   = $Directory
        Assets      = $Assets
        Mappings    = $Mappings
        Required    = $Required
        ManualFiles = $ManualFiles
    }
}

$availableModels = @(
    (New-Model "Silero" "VAD" "vad/silero" `
        @(New-Asset "asr-models" "silero_vad.onnx" "model.onnx") @() @("model.onnx")),
    (New-Model "TenVad" "VAD" "vad/ten-vad" `
        @(New-Asset "asr-models" "ten-vad.onnx" "model.onnx") @() @("model.onnx")),

    (New-Model "SenseVoice" "离线 ASR" "asr/sense-voice" `
        @(New-Asset "asr-models" "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "Paraformer" "离线 ASR" "asr/paraformer" `
        @(New-Asset "asr-models" "sherpa-onnx-paraformer-zh-int8-2025-10-07.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "OfflineTransducer" "离线 ASR" "asr/offline-transducer" `
        @(New-Asset "asr-models" "icefall-asr-multidataset-pruned_transducer_stateless7-2023-05-04.tar.bz2") `
        @(
            (New-Mapping "encoder.onnx" @("encoder-epoch-30-avg-4.int8.onnx", "encoder-epoch-30-avg-4.onnx")),
            (New-Mapping "decoder.onnx" @("decoder-epoch-30-avg-4.onnx", "decoder-epoch-30-avg-4.int8.onnx")),
            (New-Mapping "joiner.onnx" @("joiner-epoch-30-avg-4.int8.onnx", "joiner-epoch-30-avg-4.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt")),
    (New-Model "NemoCtc" "离线 ASR" "asr/nemo-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-nemo-ctc-en-conformer-small.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "Whisper" "离线 ASR" "asr/whisper" `
        @(New-Asset "asr-models" "sherpa-onnx-whisper-tiny.tar.bz2") `
        @(
            (New-Mapping "encoder.onnx" @("tiny-encoder.int8.onnx", "tiny-encoder.onnx")),
            (New-Mapping "decoder.onnx" @("tiny-decoder.int8.onnx", "tiny-decoder.onnx")),
            (New-Mapping "tokens.txt" @("tiny-tokens.txt", "tokens.txt"))
        ) @("encoder.onnx", "decoder.onnx", "tokens.txt")),
    (New-Model "Tdnn" "离线 ASR" "asr/tdnn" `
        @(New-Asset "asr-models" "sherpa-onnx-tdnn-yesno.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "TeleSpeechCtc" "离线 ASR" "asr/tele-speech-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-telespeech-ctc-int8-zh-2024-06-04.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "Moonshine" "离线 ASR" "asr/moonshine" `
        @(New-Asset "asr-models" "sherpa-onnx-moonshine-tiny-en-quantized-2026-02-27.tar.bz2") `
        @(
            (New-Mapping "encoder.onnx" @("encoder_model.ort", "encoder.onnx")),
            (New-Mapping "merged-decoder.onnx" @("decoder_model_merged.ort", "merged-decoder.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("encoder.onnx", "merged-decoder.onnx", "tokens.txt")),
    (New-Model "FireRedAsr" "离线 ASR" "asr/fire-red-asr" `
        @(New-Asset "asr-models" "sherpa-onnx-fire-red-asr-large-zh_en-2025-02-16.tar.bz2") `
        @(
            (New-Mapping "encoder.onnx" @("encoder.int8.onnx", "encoder.onnx")),
            (New-Mapping "decoder.onnx" @("decoder.int8.onnx", "decoder.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("encoder.onnx", "decoder.onnx", "tokens.txt")),
    (New-Model "Dolphin" "离线 ASR" "asr/dolphin" `
        @(New-Asset "asr-models" "sherpa-onnx-dolphin-base-ctc-multi-lang-int8-2025-04-02.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "ZipformerCtc" "离线 ASR" "asr/zipformer-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-zipformer-ctc-small-zh-int8-2025-07-16.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "Canary" "离线 ASR" "asr/canary" `
        @(New-Asset "asr-models" "sherpa-onnx-nemo-canary-180m-flash-en-es-de-fr-int8.tar.bz2") `
        @(
            (New-Mapping "encoder.onnx" @("encoder.int8.onnx", "encoder.onnx")),
            (New-Mapping "decoder.onnx" @("decoder.int8.onnx", "decoder.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("encoder.onnx", "decoder.onnx", "tokens.txt")),
    (New-Model "WenetCtc" "离线 ASR" "asr/wenet-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-zh-wenet-aishell.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "OmnilingualAsrCtc" "离线 ASR" "asr/omnilingual-asr-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-omnilingual-asr-1600-languages-300M-ctc-int8-2025-11-12.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "MedAsrCtc" "离线 ASR" "asr/med-asr-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-medasr-ctc-en-int8-2025-12-25.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "FunAsrNano" "离线 ASR" "asr/fun-asr-nano" `
        @(New-Asset "asr-models" "sherpa-onnx-funasr-nano-int8-2025-12-30.tar.bz2") `
        @(
            (New-Mapping "encoder-adaptor.onnx" @("encoder_adaptor.int8.onnx", "encoder_adaptor.onnx")),
            (New-Mapping "llm.onnx" @("llm.int8.onnx", "llm.onnx")),
            (New-Mapping "embedding.onnx" @("embedding.int8.onnx", "embedding.onnx")),
            (New-Mapping "tokenizer" @("Qwen3-0.6B", "tokenizer") -Directory)
        ) @("encoder-adaptor.onnx", "llm.onnx", "embedding.onnx", "tokenizer")),
    (New-Model "FireRedAsrCtc" "离线 ASR" "asr/fire-red-asr-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-fire-red-asr2-ctc-zh_en-int8-2026-02-25.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "Qwen3Asr" "离线 ASR" "asr/qwen3-asr" `
        @(New-Asset "asr-models" "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2") `
        @(
            (New-Mapping "conv-frontend.onnx" @("conv_frontend.onnx", "conv-frontend.onnx")),
            (New-Mapping "encoder.onnx" @("encoder.int8.onnx", "encoder.onnx")),
            (New-Mapping "decoder.onnx" @("decoder.int8.onnx", "decoder.onnx")),
            (New-Mapping "tokenizer" @("tokenizer") -Directory)
        ) @("conv-frontend.onnx", "encoder.onnx", "decoder.onnx", "tokenizer")),
    (New-Model "CohereTranscribe" "离线 ASR" "asr/cohere-transcribe" `
        @(New-Asset "asr-models" "sherpa-onnx-cohere-transcribe-14-lang-int8-2026-04-01.tar.bz2") `
        @(
            (New-Mapping "encoder.onnx" @("encoder.int8.onnx", "encoder.onnx")),
            (New-Mapping "decoder.onnx" @("decoder.int8.onnx", "decoder.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("encoder.onnx", "decoder.onnx", "tokens.txt")),

    (New-Model "OnlineTransducer" "流式 ASR" "asr/online-transducer" `
        @(New-Asset "asr-models" "sherpa-onnx-streaming-zipformer-small-bilingual-zh-en-2023-02-16.tar.bz2") `
        @(
            (New-Mapping "encoder.onnx" @("encoder-epoch-99-avg-1.int8.onnx", "encoder-epoch-99-avg-1.onnx")),
            (New-Mapping "decoder.onnx" @("decoder-epoch-99-avg-1.onnx", "decoder-epoch-99-avg-1.int8.onnx")),
            (New-Mapping "joiner.onnx" @("joiner-epoch-99-avg-1.int8.onnx", "joiner-epoch-99-avg-1.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt")),
    (New-Model "OnlineParaformer" "流式 ASR" "asr/online-paraformer" `
        @(New-Asset "asr-models" "sherpa-onnx-streaming-paraformer-bilingual-zh-en.tar.bz2") `
        @(
            (New-Mapping "encoder.onnx" @("encoder.int8.onnx", "encoder.onnx")),
            (New-Mapping "decoder.onnx" @("decoder.int8.onnx", "decoder.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("encoder.onnx", "decoder.onnx", "tokens.txt")),
    (New-Model "OnlineZipformer2Ctc" "流式 ASR" "asr/online-zipformer2-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-streaming-zipformer-ctc-multi-zh-hans-2023-12-13.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("ctc-epoch-20-avg-1-chunk-16-left-128.int8.onnx", "ctc-epoch-20-avg-1-chunk-16-left-128.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "OnlineNemoCtc" "流式 ASR" "asr/online-nemo-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-nemo-streaming-fast-conformer-ctc-en-80ms.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "OnlineToneCtc" "流式 ASR" "asr/online-tone-ctc" `
        @(New-Asset "asr-models" "sherpa-onnx-streaming-t-one-russian-2025-09-08.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.int8.onnx", "model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),

    (New-Model "Kokoro" "TTS" "tts/kokoro" `
        @(New-Asset "tts-models" "kokoro-multi-lang-v1_0.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.onnx")),
            (New-Mapping "voices.bin" @("voices.bin")),
            (New-Mapping "tokens.txt" @("tokens.txt")),
            (New-Mapping "espeak-ng-data" @("espeak-ng-data") -Directory),
            (New-Mapping "dict" @("dict") -Directory)
        ) @("model.onnx", "voices.bin", "tokens.txt", "espeak-ng-data", "dict")),
    (New-Model "Vits" "TTS" "tts/vits" `
        @(New-Asset "tts-models" "vits-icefall-zh-aishell3.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("model.onnx", "tokens.txt")),
    (New-Model "Matcha" "TTS" "tts/matcha" `
        @(
            (New-Asset "tts-models" "matcha-icefall-zh-baker.tar.bz2"),
            (New-Asset "vocoder-models" "vocos-22khz-univ.onnx" "vocoder.onnx")
        ) `
        @(
            (New-Mapping "acoustic-model.onnx" @("model-steps-3.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt"))
        ) @("acoustic-model.onnx", "vocoder.onnx", "tokens.txt")),
    (New-Model "Kitten" "TTS" "tts/kitten" `
        @(New-Asset "tts-models" "kitten-nano-en-v0_1-fp16.tar.bz2") `
        @(
            (New-Mapping "model.onnx" @("model.fp16.onnx", "model.onnx")),
            (New-Mapping "voices.bin" @("voices.bin")),
            (New-Mapping "tokens.txt" @("tokens.txt")),
            (New-Mapping "espeak-ng-data" @("espeak-ng-data") -Directory)
        ) @("model.onnx", "voices.bin", "tokens.txt", "espeak-ng-data")),
    (New-Model "ZipVoice" "TTS" "tts/zip-voice" `
        @(
            (New-Asset "tts-models" "sherpa-onnx-zipvoice-distill-int8-zh-en-emilia.tar.bz2"),
            (New-Asset "vocoder-models" "vocos_24khz.onnx" "vocoder.onnx")
        ) `
        @(
            (New-Mapping "encoder.onnx" @("encoder.int8.onnx", "encoder.onnx")),
            (New-Mapping "decoder.onnx" @("decoder.int8.onnx", "decoder.onnx")),
            (New-Mapping "tokens.txt" @("tokens.txt")),
            (New-Mapping "lexicon.txt" @("lexicon.txt")),
            (New-Mapping "espeak-ng-data" @("espeak-ng-data") -Directory)
        ) @("encoder.onnx", "decoder.onnx", "vocoder.onnx", "tokens.txt", "lexicon.txt", "espeak-ng-data") @("reference.wav", "reference.txt")),
    (New-Model "Pocket" "TTS" "tts/pocket" `
        @(New-Asset "tts-models" "sherpa-onnx-pocket-tts-int8-2026-01-26.tar.bz2") `
        @(
            (New-Mapping "lm-flow.onnx" @("lm_flow.int8.onnx", "lm_flow.onnx")),
            (New-Mapping "lm-main.onnx" @("lm_main.int8.onnx", "lm_main.onnx")),
            (New-Mapping "encoder.onnx" @("encoder.int8.onnx", "encoder.onnx")),
            (New-Mapping "decoder.onnx" @("decoder.int8.onnx", "decoder.onnx")),
            (New-Mapping "text-conditioner.onnx" @("text_conditioner.int8.onnx", "text_conditioner.onnx")),
            (New-Mapping "vocab.json" @("vocab.json")),
            (New-Mapping "token-scores.json" @("token_scores.json"))
        ) @("lm-flow.onnx", "lm-main.onnx", "encoder.onnx", "decoder.onnx", "text-conditioner.onnx", "vocab.json", "token-scores.json") @("reference.wav")),
    (New-Model "Supertonic" "TTS" "tts/supertonic" `
        @(New-Asset "tts-models" "sherpa-onnx-supertonic-3-tts-int8-2026-05-11.tar.bz2") `
        @(
            (New-Mapping "duration-predictor.onnx" @("duration_predictor.int8.onnx", "duration_predictor.onnx")),
            (New-Mapping "text-encoder.onnx" @("text_encoder.int8.onnx", "text_encoder.onnx")),
            (New-Mapping "vector-estimator.onnx" @("vector_estimator.int8.onnx", "vector_estimator.onnx")),
            (New-Mapping "vocoder.onnx" @("vocoder.int8.onnx", "vocoder.onnx")),
            (New-Mapping "tts.json" @("tts.json")),
            (New-Mapping "unicode-indexer.bin" @("unicode_indexer.bin", "unicode-indexer.bin")),
            (New-Mapping "voice.bin" @("voice.bin"))
        ) @("duration-predictor.onnx", "text-encoder.onnx", "vector-estimator.onnx", "vocoder.onnx", "tts.json", "unicode-indexer.bin", "voice.bin"))
)

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
    Invoke-WebRequest -Uri $Asset.Url -OutFile $outputPath
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
        Invoke-WebRequest -Uri $Asset.Url -OutFile $archivePath
        Write-Host "解压 $($Asset.Name)"
        & tar.exe -xf $archivePath -C $TargetDirectory --strip-components=1
        if ($LASTEXITCODE -ne 0) {
            throw "tar.exe 无法解压 $($Asset.Name)。"
        }
    }
    finally {
        if (-not $PreserveArchive -and (Test-Path -LiteralPath $archivePath)) {
            Remove-Item -LiteralPath $archivePath -Force
        }
    }
}

$requestedModelNames = @($Models | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if (-not $All -and $requestedModelNames.Count -eq 0) {
    Write-Host "请使用 -Models 或 -All 选择要下载的模型。"
    $availableModels | Sort-Object Category, Name | Format-Table Category, Name, Directory -AutoSize
    Write-Host "示例："
    Write-Host "  pwsh -File .\\scripts\\Download-SherpaModels.ps1 -Models Silero,OnlineParaformer,Kokoro"
    Write-Host "  pwsh -File .\\scripts\\Download-SherpaModels.ps1 -All"
    return
}

if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
    throw "未找到 tar.exe。请安装 Windows 自带的 tar 或提供兼容的 tar.exe。"
}

$destinationPath = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null

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
