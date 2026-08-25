using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Media.Abstractions.Exceptions;
using XiaoZhi.Net.Server.Media.Common.Models;
using XiaoZhi.Net.Server.Media.Decoders;
using XiaoZhi.Net.Server.Media.Exceptions;
using XiaoZhi.Net.Server.Media.Players.Contexts;
using XiaoZhi.Net.Server.Media.Players.WorkPool;
using XiaoZhi.Net.Server.Media.Processors;
using XiaoZhi.Net.Server.Media.Utilities;

namespace XiaoZhi.Net.Server.Media.Players;

internal abstract class AudioPlayerBase<TDecoderType, TLogger> : IAudioPlayer
{
    private readonly IAudioDecodeScheduler _decodeScheduler;
    private readonly CancellationTokenSource _lifetimeCancellationSource = new();
    private readonly object _syncRoot = new();
    private AudioPlaybackContext? _playbackContext;
    private Task? _recoveryTask;
    private bool _decoderRecoveryRequired;
    private bool _disposed;
    private bool _hasPlaybackContextSlot;
    private bool _loading;
    private TimeSpan _recoveryPosition;

    protected AudioPlayerBase(IAudioDecodeScheduler decodeScheduler, ILogger<TLogger> logger)
    {
        this._decodeScheduler = decodeScheduler;
        this.Logger = logger;
        this.VolumeProcessor = new VolumeProcessor { Volume = 1.0f };
    }

    public event Action<PlaybackState>? StateChanged;

    public event Action<TimeSpan>? PositionChanged;

    public event Action<float[], bool, bool>? OnAudioDataAvailable;

    public abstract string AudioPlayerName { get; }

    public bool IsFFmpegInitialized => FFmpegStartup.FFmpegInitialized;

    public bool IsLoaded { get; private set; }

    public TimeSpan Duration { get; private set; }

    public TimeSpan Position { get; private set; }

    public PlaybackState State { get; private set; }

    public bool IsSeeking { get; private set; }

    public float Volume
    {
        get => this.VolumeProcessor.Volume;
        set => this.VolumeProcessor.Volume = this.VerifyVolume(value);
    }

    public ISampleProcessor? CustomSampleProcessor { get; set; }

    protected abstract bool CanRecoverAfterInterrupt { get; }

    protected IAudioDecoder? CurrentDecoder { get; private set; }

    protected abstract int ExpectedFrameBytes { get; }

    protected abstract TimeSpan FrameDuration { get; }

    protected ILogger<TLogger> Logger { get; }

    /// <summary>
    /// 获取当前播放器使用的调度和资源限制配置。
    /// </summary>
    protected AudioPlayerOptions AudioPlayerOptions => this._decodeScheduler.Options;

    protected VolumeProcessor VolumeProcessor { get; }

    public Task<bool> CheckFFmpegInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        if (this.IsFFmpegInitialized)
        {
            return Task.FromResult(true);
        }

        bool checkResult = FFmpegStartup.CheckFFmpegInstalled(out string message);
        if (checkResult)
        {
            this.Logger.LogInformation("Initialized the ffmpeg, version: {Version}", message);
        }
        else
        {
            this.Logger.LogError("FFmpeg is not installed or failed to initialize: {Message}", message);
        }

        return Task.FromResult(checkResult);
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        AudioPlaybackContext? contextToStart = null;
        Action<PlaybackState>? stateChangedHandler = null;
        PlaybackState stateChangedTo = PlaybackState.Idle;
        Task playbackTask;
        bool requiresRecovery = false;

        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();

            if (!this.IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke CheckFFmpegInstalledAsync first.");
            }

            if (!this.IsLoaded)
            {
                this.Logger.LogDebug("No loaded audio for playback.");
                return Task.CompletedTask;
            }

            if (this._playbackContext is not null)
            {
                if (this._playbackContext.IsPaused)
                {
                    this._playbackContext.Resume();
                    stateChangedHandler = this.SetStateLocked(PlaybackState.Playing);
                    stateChangedTo = PlaybackState.Playing;
                    contextToStart = this._playbackContext;
                }

                playbackTask = this._playbackContext.PlaybackTask;
            }
            else if (this._decoderRecoveryRequired)
            {
                requiresRecovery = true;
                playbackTask = Task.CompletedTask;
            }
            else if (this.CurrentDecoder is null)
            {
                this.IsLoaded = false;
                return Task.CompletedTask;
            }
            else
            {
                int frameBufferCapacity = Math.Max(1, (int)Math.Ceiling(this._decodeScheduler.Options.MaxBufferDuration / this.FrameDuration));
                AudioPlaybackContext context = new(
                    this.CurrentDecoder,
                    this._decodeScheduler,
                    frameBufferCapacity,
                    this.FrameDuration,
                    this.ExpectedFrameBytes,
                    this.Position,
                    cancellationToken,
                    this.RunDecodeBatch);
                this._playbackContext = context;
                stateChangedHandler = this.SetStateLocked(PlaybackState.Buffering);
                stateChangedTo = PlaybackState.Buffering;
                contextToStart = context;
                playbackTask = context.PlaybackTask;
            }
        }

        if (requiresRecovery)
        {
            return this.RecoverAndPlayAsync(cancellationToken);
        }

        if (contextToStart is not null)
        {
            if (ReferenceEquals(contextToStart, this._playbackContext) && contextToStart.PlaybackTask == Task.CompletedTask)
            {
                try
                {
                    this._decodeScheduler.Schedule(contextToStart, AudioDecodeWorkPriority.Refill);
                    contextToStart.EngineTask = this.RunEngineAsync(contextToStart);
                    contextToStart.PlaybackTask = this.RunPlaybackAsync(contextToStart);
                    playbackTask = contextToStart.PlaybackTask;
                }
                catch
                {
                    lock (this._syncRoot)
                    {
                        if (ReferenceEquals(this._playbackContext, contextToStart))
                        {
                            this._playbackContext = null;
                            this.SetStateLocked(PlaybackState.Idle);
                        }
                    }

                    contextToStart.Dispose();
                    throw;
                }
            }
            else if (!contextToStart.IsPaused)
            {
                this._decodeScheduler.Schedule(contextToStart, AudioDecodeWorkPriority.Refill);
            }
        }

        this.RaiseStateChanged(stateChangedHandler, stateChangedTo);
        return playbackTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        Action<PlaybackState>? stateChangedHandler = null;

        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();

            if (this._playbackContext is not null && this.State is PlaybackState.Playing or PlaybackState.Buffering)
            {
                this._playbackContext.Pause();
                stateChangedHandler = this.SetStateLocked(PlaybackState.Paused);
            }
        }

        this.RaiseStateChanged(stateChangedHandler, PlaybackState.Paused);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        AudioPlaybackContext? context;

        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();
            context = this._playbackContext;
            context?.Cancel();
        }

        if (context is null)
        {
            return Task.CompletedTask;
        }

        this._decodeScheduler.Schedule(context, AudioDecodeWorkPriority.StopOrDispose);
        return context.CleanupTask.WaitAsync(cancellationToken);
    }

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        AudioPlaybackContext? context;
        AudioSeekRequest? request = null;
        bool requiresRecovery = false;

        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();
            context = this._playbackContext;

            if (context is null)
            {
                requiresRecovery = this._decoderRecoveryRequired;
            }
            else if (!context.IsCancellationRequested && !context.EndOfFile)
            {
                request = new AudioSeekRequest(position, cancellationToken);
                if (!context.SeekRequests.Writer.TryWrite(request))
                {
                    return Task.CompletedTask;
                }

                this.IsSeeking = true;
                context.RequestDecoderInterrupt();
            }
        }

        if (requiresRecovery)
        {
            return this.EnsureDecoderRecoveredAsync(position, cancellationToken);
        }

        if (context is null || request is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            this._decodeScheduler.Schedule(context, AudioDecodeWorkPriority.Seek);
        }
        catch (Exception exception)
        {
            request.Fail(exception);
        }

        return this.WaitForSeekAsync(context, request, cancellationToken);
    }

    public virtual void Dispose()
    {
        IAudioDecoder? decoderToDispose = null;
        bool releaseContextSlot = false;
        AudioPlaybackContext? playbackContext;

        lock (this._syncRoot)
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            this._lifetimeCancellationSource.Cancel();
            playbackContext = this._playbackContext;

            if (playbackContext is not null)
            {
                playbackContext.Cancel();
            }
            else
            {
                decoderToDispose = this.CurrentDecoder;
                this.CurrentDecoder = null;
                releaseContextSlot = this._hasPlaybackContextSlot;
                this._hasPlaybackContextSlot = false;
            }

            this.IsLoaded = false;
        }

        if (playbackContext is not null)
        {
            this._decodeScheduler.Schedule(playbackContext, AudioDecodeWorkPriority.StopOrDispose);
        }

        if (decoderToDispose is not null)
        {
            _ = this.DisposeDecoderAsync(decoderToDispose);
        }

        if (releaseContextSlot)
        {
            this._decodeScheduler.ReleasePlaybackContext();
        }

        this._lifetimeCancellationSource.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract IAudioDecoder CreateDecoder(TDecoderType decoderParam, CancellationToken cancellationToken);

    protected abstract IAudioDecoder? CreateRecoveryDecoder(AudioDecoderResult result, CancellationToken cancellationToken);

    protected async Task<bool> LoadInternalAsync(Func<CancellationToken, IAudioDecoder> decoderFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decoderFactory);

        IAudioDecoder? oldDecoder;
        bool alreadyHasSlot;
        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();

            if (this.State != PlaybackState.Idle || this._loading || this._recoveryTask is not null)
            {
                return false;
            }

            this._loading = true;
            oldDecoder = this.CurrentDecoder;
            this.CurrentDecoder = null;
            alreadyHasSlot = this._hasPlaybackContextSlot;
            this.IsLoaded = false;
            this._decoderRecoveryRequired = false;
        }

        if (oldDecoder is not null)
        {
            await this.DisposeDecoderAsync(oldDecoder);
        }

        IAudioDecoder? newDecoder = null;
        bool acquiredSlot = false;
        using CancellationTokenSource linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            this._lifetimeCancellationSource.Token);

        try
        {
            this.Logger.LogDebug("Loading audio to the player.");
            newDecoder = await this._decodeScheduler.RunAsync(decoderFactory, linkedCancellationSource.Token);

            if (!alreadyHasSlot)
            {
                if (!this._decodeScheduler.TryAcquirePlaybackContext())
                {
                    throw new AudioPlaybackCapacityExceededException(AudioPlaybackCapacityExceededReason.ContextLimit);
                }

                acquiredSlot = true;
            }

            lock (this._syncRoot)
            {
                if (this._disposed)
                {
                    return false;
                }

                this.CurrentDecoder = newDecoder;
                this.Duration = newDecoder.StreamInfo.Duration;
                this.IsLoaded = true;
                this._hasPlaybackContextSlot = true;
                newDecoder = null;
            }

            this.SetAndRaisePositionChanged(TimeSpan.Zero);
            this.Logger.LogDebug("Audio successfully loaded.");
            return true;
        }
        catch (AudioPlaybackCapacityExceededException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (linkedCancellationSource.IsCancellationRequested)
        {
            throw new OperationCanceledException(linkedCancellationSource.Token);
        }
        catch (Exception exception)
        {
            this.Logger.LogDebug("Failed to load audio: {Message}", exception.Message);
            return false;
        }
        finally
        {
            if (newDecoder is not null)
            {
                await this.DisposeDecoderAsync(newDecoder);
            }

            bool releaseSlot = false;
            lock (this._syncRoot)
            {
                if (!this.IsLoaded && (alreadyHasSlot || acquiredSlot))
                {
                    this._hasPlaybackContextSlot = false;
                    releaseSlot = true;
                }

                this._loading = false;
            }

            if (releaseSlot)
            {
                this._decodeScheduler.ReleasePlaybackContext();
            }
        }
    }

    protected virtual void ProcessSampleProcessors(Span<float> samples)
    {
        if (this.CustomSampleProcessor is { IsEnabled: true })
        {
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = this.CustomSampleProcessor.Process(samples[index]);
            }
        }

        if (this.VolumeProcessor.Volume != 1.0f)
        {
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = this.VolumeProcessor.Process(samples[index]);
            }
        }
    }

    protected virtual void SetAndRaisePositionChanged(TimeSpan position)
    {
        Action<TimeSpan>? handler;

        lock (this._syncRoot)
        {
            handler = this.SetPositionLocked(position);
        }

        this.RaisePositionChanged(handler, position);
    }

    private void SetAndRaiseStateChanged(PlaybackState state)
    {
        Action<PlaybackState>? handler;

        lock (this._syncRoot)
        {
            handler = this.SetStateLocked(state);
        }

        this.RaiseStateChanged(handler, state);
    }

    private AudioDecodeBatchResult RunDecodeBatch(AudioPlaybackContext context)
    {
        if (context.IsCancellationRequested)
        {
            return AudioDecodeBatchResult.Completed;
        }

        if (context.InitialSeekPending)
        {
            this.SeekDecoder(context, context.StartPosition, null);
            context.CompleteInitialSeek();
        }

        this.ProcessPendingSeekRequests(context);
        long batchStartTimestamp = TimeProvider.System.GetTimestamp();
        int decodedFrames = 0;

        while (decodedFrames < this._decodeScheduler.Options.MaxFramesPerBatch
            && TimeProvider.System.GetElapsedTime(batchStartTimestamp) < this._decodeScheduler.Options.MaxDecodeBatchDuration)
        {
            if (context.IsCancellationRequested)
            {
                return AudioDecodeBatchResult.Completed;
            }

            if (this.ProcessPendingSeekRequests(context))
            {
                continue;
            }

            if (!context.TryReserveFrame(out int reservedBytes))
            {
                return AudioDecodeBatchResult.Completed;
            }

            int generation = context.Generation;
            AudioDecoderResult result = context.Decoder.DecodeNextFrame();

            if (context.Decoder.InterruptionReason == AudioDecoderInterruptionReason.UrlReadTimeout)
            {
                context.ReleaseReservedFrame(reservedBytes);
                context.Decoder.FlushAfterInterrupt();
                context.MarkTerminalFault();
                throw new TimeoutException("URL 音频源读取超时。");
            }

            if (context.Decoder.WasInterrupted)
            {
                context.ReleaseReservedFrame(reservedBytes);
                context.MarkDecoderInterrupted();

                if (context.IsCancellationRequested)
                {
                    return AudioDecodeBatchResult.Completed;
                }

                if (context.HasPendingSeekRequests)
                {
                    this.ProcessPendingSeekRequests(context);
                    continue;
                }

                this.RecoverDecoder(context, result, this.Position);
                continue;
            }

            if (this.ProcessPendingSeekRequests(context))
            {
                context.ReleaseReservedFrame(reservedBytes);
                continue;
            }

            if (result.Frame is not null)
            {
                if (!context.TryWriteReservedFrame(reservedBytes, new PlaybackAudioFrame(result.Frame, generation)))
                {
                    return AudioDecodeBatchResult.Completed;
                }

                decodedFrames++;
            }
            else
            {
                context.ReleaseReservedFrame(reservedBytes);
            }

            if (result.IsEOF)
            {
                context.MarkEndOfFile();
                return AudioDecodeBatchResult.Completed;
            }

            if (!result.IsSucceeded)
            {
                this.RecoverDecoder(context, result, this.Position);
            }
        }

        return context.CanDecodeMore() ? AudioDecodeBatchResult.Refill : AudioDecodeBatchResult.Completed;
    }

    private async Task RunEngineAsync(AudioPlaybackContext context)
    {
        PlaybackAudioFrame? pendingFrame = null;
        long playbackStartTimestamp = TimeProvider.System.GetTimestamp();
        long totalPauseTicks = 0;
        int activeGeneration = -1;
        bool isFirstAudioFrame = true;
        bool lastEventSent = false;
        float[]? lastProcessedSamples = null;

        async Task ProcessFrameAsync(PlaybackAudioFrame playbackFrame, bool isLastAudioFrame)
        {
            if (playbackFrame.Generation != context.Generation)
            {
                return;
            }

            if (activeGeneration != playbackFrame.Generation)
            {
                if (lastProcessedSamples is not null && !lastEventSent)
                {
                    this.OnAudioDataAvailable?.Invoke(lastProcessedSamples, false, true);
                }

                activeGeneration = playbackFrame.Generation;
                playbackStartTimestamp = TimeProvider.System.GetTimestamp() - ToTimestampTicks(TimeSpan.FromMilliseconds(playbackFrame.Frame.PresentationTime));
                totalPauseTicks = 0;
                isFirstAudioFrame = true;
                lastEventSent = false;
                lastProcessedSamples = null;
            }

            while (context.IsPaused)
            {
                if (lastProcessedSamples is not null && !lastEventSent)
                {
                    this.OnAudioDataAvailable?.Invoke(lastProcessedSamples, false, true);
                    lastEventSent = true;
                }

                long pauseStartTimestamp = TimeProvider.System.GetTimestamp();
                await context.WaitWhilePausedAsync(context.Token);
                totalPauseTicks += TimeProvider.System.GetTimestamp() - pauseStartTimestamp;
                isFirstAudioFrame = true;
                lastEventSent = false;
            }

            if (playbackFrame.Generation != context.Generation)
            {
                return;
            }

            long targetTimestamp = playbackStartTimestamp
                + ToTimestampTicks(TimeSpan.FromMilliseconds(playbackFrame.Frame.PresentationTime))
                + totalPauseTicks;
            TimeSpan delay = TimeProvider.System.GetElapsedTime(TimeProvider.System.GetTimestamp(), targetTimestamp);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, context.Token);
            }

            if (context.IsPaused || playbackFrame.Generation != context.Generation)
            {
                return;
            }

            float[] samplesArray = this.ProcessFrameSamples(playbackFrame.Frame);
            Action<PlaybackState>? stateChangedHandler;
            lock (this._syncRoot)
            {
                if (context.IsPaused)
                {
                    return;
                }

                stateChangedHandler = this.SetStateLocked(PlaybackState.Playing);
            }

            this.RaiseStateChanged(stateChangedHandler, PlaybackState.Playing);
            lastProcessedSamples = samplesArray;
            this.OnAudioDataAvailable?.Invoke(samplesArray, isFirstAudioFrame, isLastAudioFrame);
            lastEventSent = isLastAudioFrame;
            isFirstAudioFrame = false;
            this.SetAndRaisePositionChanged(TimeSpan.FromMilliseconds(playbackFrame.Frame.PresentationTime));
        }

        try
        {
            while (true)
            {
                if (!context.TryReadFrame(out PlaybackAudioFrame? playbackFrame))
                {
                    if (!context.IsPaused && !context.EndOfFile)
                    {
                        this.SetAndRaiseStateChanged(PlaybackState.Buffering);
                    }

                    if (context.ShouldRequestRefill())
                    {
                        this._decodeScheduler.Schedule(context, AudioDecodeWorkPriority.Refill);
                    }

                    if (!await context.Frames.Reader.WaitToReadAsync(context.Token))
                    {
                        break;
                    }

                    continue;
                }

                if (playbackFrame is null)
                {
                    continue;
                }

                if (context.ShouldRequestRefill())
                {
                    this._decodeScheduler.Schedule(context, AudioDecodeWorkPriority.Refill);
                }

                if (playbackFrame.Generation != context.Generation)
                {
                    continue;
                }

                if (pendingFrame is not null && pendingFrame.Generation == context.Generation)
                {
                    await ProcessFrameAsync(pendingFrame, false);
                }

                pendingFrame = playbackFrame;
            }

            if (pendingFrame is not null && pendingFrame.Generation == context.Generation)
            {
                await ProcessFrameAsync(pendingFrame, true);
            }
        }
        finally
        {
            if (context.IsCancellationRequested && lastProcessedSamples is not null && !lastEventSent)
            {
                this.OnAudioDataAvailable?.Invoke(lastProcessedSamples, false, true);
            }
        }
    }

    private async Task RunPlaybackAsync(AudioPlaybackContext context)
    {
        IAudioDecoder? decoderToDispose = null;
        bool releaseContextSlot = false;
        Action<TimeSpan>? positionChangedHandler = null;
        Action<PlaybackState>? stateChangedHandler = null;

        try
        {
            await context.EngineTask;
            await context.DecoderIdleTask;
        }
        catch (OperationCanceledException) when (context.IsCancellationRequested)
        {
            throw new AudioPlaybackCanceledException(context.Token);
        }
        finally
        {
            if (!context.DecoderIdleTask.IsCompleted)
            {
                context.Cancel();
                this._decodeScheduler.Schedule(context, AudioDecodeWorkPriority.StopOrDispose);
            }

            try
            {
                await context.DecoderIdleTask;
            }
            catch (OperationCanceledException)
            {
                // 调度器关闭时只需继续释放本次播放持有的资源。
            }

            this.CompletePendingSeekRequests(context, context.IsCancellationRequested ? new OperationCanceledException(context.Token) : null);
            context.ClearFrames();

            lock (this._syncRoot)
            {
                if (ReferenceEquals(this._playbackContext, context))
                {
                    TimeSpan completedPosition = this.Position;
                    this._playbackContext = null;
                    this.IsSeeking = false;
                    positionChangedHandler = this.SetPositionLocked(TimeSpan.Zero);
                    stateChangedHandler = this.SetStateLocked(PlaybackState.Idle);

                    if (context.WasInterrupted || context.IsFaulted)
                    {
                        this._recoveryPosition = completedPosition;
                        decoderToDispose = context.Decoder;
                        this.CurrentDecoder = null;
                        this._decoderRecoveryRequired = !context.IsTerminalFault
                            && this.CanRecoverAfterInterrupt
                            && !this._disposed;

                        if (!this._decoderRecoveryRequired)
                        {
                            this.IsLoaded = false;
                            releaseContextSlot = this._hasPlaybackContextSlot;
                            this._hasPlaybackContextSlot = false;
                        }
                    }
                    else if (this._disposed)
                    {
                        decoderToDispose = context.Decoder;
                        this.CurrentDecoder = null;
                        this.IsLoaded = false;
                        releaseContextSlot = this._hasPlaybackContextSlot;
                        this._hasPlaybackContextSlot = false;
                    }
                    else
                    {
                        this.CurrentDecoder = context.Decoder;
                    }
                }
            }

            if (decoderToDispose is not null)
            {
                await this.DisposeDecoderAsync(decoderToDispose);
            }

            if (releaseContextSlot)
            {
                this._decodeScheduler.ReleasePlaybackContext();
            }

            this._decodeScheduler.Unregister(context);
            context.Dispose();
            this.RaisePositionChanged(positionChangedHandler, TimeSpan.Zero);
            this.RaiseStateChanged(stateChangedHandler, PlaybackState.Idle);
            context.CleanupCompletionSource.TrySetResult();
        }
    }

    private async Task RecoverAndPlayAsync(CancellationToken cancellationToken)
    {
        TimeSpan position;
        lock (this._syncRoot)
        {
            position = this._recoveryPosition;
        }

        await this.EnsureDecoderRecoveredAsync(position, cancellationToken);
        await this.PlayAsync(cancellationToken);
    }

    private async Task EnsureDecoderRecoveredAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        Task recoveryTask;
        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();

            if (!this._decoderRecoveryRequired)
            {
                return;
            }

            recoveryTask = this._recoveryTask ??= this.RecoverDecoderAsync(position, cancellationToken);
        }

        await recoveryTask;
    }

    private async Task RecoverDecoderAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        IAudioDecoder? decoder = null;
        bool releaseContextSlot = false;

        try
        {
            decoder = await this._decodeScheduler.RunAsync(
                workerCancellationToken =>
                {
                    IAudioDecoder recoveredDecoder = this.CreateRecoveryDecoder(
                        new AudioDecoderResult(null, false, false, "Decoder was interrupted."),
                        workerCancellationToken)
                        ?? throw new FFmpegException("Unable to recreate interrupted audio decoder.");

                    try
                    {
                        using CancellationTokenRegistration interruptRegistration = workerCancellationToken.Register(
                            static state => ((IAudioDecoder)state!).RequestInterrupt(),
                            recoveredDecoder);

                        if (!recoveredDecoder.TrySeek(position, out string? error))
                        {
                            throw new FFmpegException(error ?? "Unable to seek recreated audio decoder.");
                        }

                        return recoveredDecoder;
                    }
                    catch
                    {
                        recoveredDecoder.Dispose();
                        throw;
                    }
                },
                cancellationToken);

            lock (this._syncRoot)
            {
                this.ThrowIfDisposed();
                this.CurrentDecoder = decoder;
                this._decoderRecoveryRequired = false;
                this.SetPositionLocked(position);
                decoder = null;
            }
        }
        finally
        {
            if (decoder is not null)
            {
                await this.DisposeDecoderAsync(decoder);
            }

            lock (this._syncRoot)
            {
                if (this._decoderRecoveryRequired)
                {
                    this.IsLoaded = false;
                    releaseContextSlot = this._hasPlaybackContextSlot;
                    this._hasPlaybackContextSlot = false;
                }

                this._recoveryTask = null;
            }

            if (releaseContextSlot)
            {
                this._decodeScheduler.ReleasePlaybackContext();
            }
        }
    }

    private async Task WaitForSeekAsync(AudioPlaybackContext context, AudioSeekRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await request.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (this._syncRoot)
            {
                if (ReferenceEquals(this._playbackContext, context))
                {
                    this.IsSeeking = false;
                }
            }
        }
    }

    private bool ProcessPendingSeekRequests(AudioPlaybackContext context)
    {
        bool processed = false;

        while (context.SeekRequests.Reader.TryRead(out AudioSeekRequest? request))
        {
            processed = true;

            try
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    request.Cancel();
                    continue;
                }

                this.SeekDecoder(context, request.Position, request.CancellationToken);
                context.IncrementGeneration();
                context.ClearFrames();
                context.StartFillingAfterSeek();
                this.SetAndRaisePositionChanged(request.Position);

                if (!context.IsPaused)
                {
                    this.SetAndRaiseStateChanged(PlaybackState.Buffering);
                }

                request.Complete();
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested && !context.IsCancellationRequested)
            {
                request.Cancel();
            }
            catch (Exception exception)
            {
                request.Fail(exception);
            }
        }

        return processed;
    }

    private void SeekDecoder(AudioPlaybackContext context, TimeSpan position, CancellationToken? seekCancellationToken)
    {
        if (context.Decoder.WasInterrupted)
        {
            context.MarkDecoderInterrupted();
            this.RecoverDecoder(context, new AudioDecoderResult(null, false, false, "Decoder was interrupted."), position);
            return;
        }

        context.ResetDecoderInterrupt();
        using CancellationTokenRegistration seekInterruptRegistration = seekCancellationToken?.Register(
            static state => ((AudioPlaybackContext)state!).RequestDecoderInterrupt(),
            context) ?? default;

        if (!context.Decoder.TrySeek(position, out string? error))
        {
            if (context.Decoder.WasInterrupted)
            {
                context.MarkDecoderInterrupted();
                this.RecoverDecoder(context, new AudioDecoderResult(null, false, false, error), position);
                return;
            }

            this.Logger.LogDebug("Unable to seek audio stream to {Position}: {Error}", position, error);
        }
    }

    private void RecoverDecoder(AudioPlaybackContext context, AudioDecoderResult result, TimeSpan position)
    {
        if (!this.CanRecoverAfterInterrupt)
        {
            throw new FFmpegException(result.ErrorMessage ?? "Unable to recover audio decoder.");
        }

        IAudioDecoder previousDecoder = context.Decoder;
        previousDecoder.FlushAfterInterrupt();
        previousDecoder.Dispose();

        IAudioDecoder? recoveredDecoder = this.CreateRecoveryDecoder(result, context.Token);
        if (recoveredDecoder is null)
        {
            throw new FFmpegException(result.ErrorMessage ?? "Unable to recover audio decoder.");
        }

        context.Decoder = recoveredDecoder;
        context.ResetDecoderInterrupt();
        if (!recoveredDecoder.TrySeek(position, out string? seekError))
        {
            recoveredDecoder.Dispose();
            throw new FFmpegException(seekError ?? "Unable to seek recreated audio decoder.");
        }

        context.ResetDecoderInterrupted();
        context.IncrementGeneration();
        context.ClearFrames();

        lock (this._syncRoot)
        {
            if (ReferenceEquals(this._playbackContext, context))
            {
                this.CurrentDecoder = recoveredDecoder;
            }
        }
    }

    private void CompletePendingSeekRequests(AudioPlaybackContext context, Exception? exception)
    {
        while (context.SeekRequests.Reader.TryRead(out AudioSeekRequest? request))
        {
            if (exception is OperationCanceledException)
            {
                request.Cancel();
            }
            else if (exception is not null)
            {
                request.Fail(exception);
            }
            else
            {
                request.Complete();
            }
        }
    }

    private async Task DisposeDecoderAsync(IAudioDecoder decoder)
    {
        try
        {
            await this._decodeScheduler.RunCleanupAsync(_ => decoder.Dispose());
        }
        catch (ObjectDisposedException)
        {
            decoder.Dispose();
        }
        catch (OperationCanceledException)
        {
            decoder.Dispose();
        }
    }

    private void RaisePositionChanged(Action<TimeSpan>? handler, TimeSpan position)
    {
        if (handler is null)
        {
            return;
        }

        foreach (Action<TimeSpan> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(position);
            }
            catch (Exception exception)
            {
                this.Logger.LogError(exception, "播放器位置事件处理程序执行失败。");
            }
        }
    }

    private void RaiseStateChanged(Action<PlaybackState>? handler, PlaybackState state)
    {
        if (handler is null)
        {
            return;
        }

        foreach (Action<PlaybackState> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(state);
            }
            catch (Exception exception)
            {
                this.Logger.LogError(exception, "播放器状态事件处理程序执行失败。");
            }
        }
    }

    private float[] ProcessFrameSamples(AudioFrame frame)
    {
        Span<float> samples = MemoryMarshal.Cast<byte, float>(frame.Data);
        this.ProcessSampleProcessors(samples);
        return samples.ToArray();
    }

    private Action<TimeSpan>? SetPositionLocked(TimeSpan position)
    {
        if (position == this.Position)
        {
            return null;
        }

        this.Position = position;
        return this.PositionChanged;
    }

    private Action<PlaybackState>? SetStateLocked(PlaybackState state)
    {
        if (state == this.State)
        {
            return null;
        }

        this.State = state;
        return this.StateChanged;
    }

    private float VerifyVolume(float volume)
    {
        return volume switch
        {
            > 1.0f => 1.0f,
            < 0.0f => 0.0f,
            _ => volume
        };
    }

    private static long ToTimestampTicks(TimeSpan duration)
    {
        return (long)(duration.TotalSeconds * TimeProvider.System.TimestampFrequency);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
    }
}
