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
    private const int FrameBufferCapacity = 128;
    private readonly IAudioDecoderWorkPool _decoderWorkPool;
    private readonly CancellationTokenSource _lifetimeCancellationSource = new();
    private readonly object _syncRoot = new();
    private AudioPlaybackContext? _playbackContext;
    private bool _disposed;
    private bool _loading;

    protected AudioPlayerBase(IAudioDecoderWorkPool decoderWorkPool, ILogger<TLogger> logger)
    {
        this._decoderWorkPool = decoderWorkPool;
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

    protected IAudioDecoder? CurrentDecoder { get; private set; }

    protected ILogger<TLogger> Logger { get; }

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
        Action<PlaybackState>? stateChangedHandler = null;
        TaskCompletionSource? engineStartSource = null;
        Task playbackTask;

        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();

            if (!this.IsFFmpegInitialized)
            {
                throw new FFmpegException("FFmpeg is not initialized yet, please invoke CheckFFmpegInstalledAsync first.");
            }

            if (!this.IsLoaded || this.CurrentDecoder is null)
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
                }

                playbackTask = this._playbackContext.PlaybackTask;
            }
            else
            {
                AudioPlaybackContext context = new(this.CurrentDecoder, FrameBufferCapacity, cancellationToken);
                TimeSpan startPosition = this.Position;

                try
                {
                    context.DecoderTask = this._decoderWorkPool.RunAsync(
                        workerCancellationToken => this.RunDecoder(context, startPosition, workerCancellationToken),
                        context.Token);
                }
                catch
                {
                    context.Dispose();
                    throw;
                }

                this._playbackContext = context;
                stateChangedHandler = this.SetStateLocked(PlaybackState.Playing);
                engineStartSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.EngineTask = this.RunEngineAsync(context, engineStartSource.Task);
                context.PlaybackTask = this.RunPlaybackAsync(context);
                playbackTask = context.PlaybackTask;
            }
        }

        this.RaiseStateChanged(stateChangedHandler, PlaybackState.Playing);
        engineStartSource?.TrySetResult();
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

            if (context is null)
            {
                return Task.CompletedTask;
            }

            context.Cancel();
            this.RequestDecoderInterrupt(context.Decoder);
        }

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

        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();
            context = this._playbackContext;

            if (!this.IsLoaded
                || context is null
                || context.IsCancellationRequested
                || context.DecoderTask.IsCompleted
                || !context.AcceptsSeekRequests)
            {
                return Task.CompletedTask;
            }

            request = new AudioSeekRequest(position, cancellationToken);
            if (!context.SeekRequests.Writer.TryWrite(request))
            {
                return Task.CompletedTask;
            }

            this.IsSeeking = true;
            this.RequestDecoderInterrupt(context.Decoder);
        }

        return this.WaitForSeekAsync(context, request, cancellationToken);
    }

    public virtual void Dispose()
    {
        IAudioDecoder? decoderToDispose = null;

        lock (this._syncRoot)
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            this._lifetimeCancellationSource.Cancel();

            if (this._playbackContext is not null)
            {
                this._playbackContext.Cancel();
                this.RequestDecoderInterrupt(this._playbackContext.Decoder);
            }
            else
            {
                decoderToDispose = this.CurrentDecoder;
                this.CurrentDecoder = null;
            }

            this.IsLoaded = false;
        }

        decoderToDispose?.Dispose();
        this._lifetimeCancellationSource.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract IAudioDecoder CreateDecoder(TDecoderType decoderParam, CancellationToken cancellationToken);

    protected abstract IAudioDecoder? CreateRecoveryDecoder(AudioDecoderResult result, CancellationToken cancellationToken);

    protected async Task<bool> LoadInternalAsync(Func<CancellationToken, IAudioDecoder> decoderFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decoderFactory);

        IAudioDecoder? oldDecoder;
        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();

            if (this.State != PlaybackState.Idle || this._loading)
            {
                return false;
            }

            this._loading = true;
            oldDecoder = this.CurrentDecoder;
            this.CurrentDecoder = null;
            this.IsLoaded = false;
        }

        oldDecoder?.Dispose();
        IAudioDecoder? newDecoder = null;

        using CancellationTokenSource linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            this._lifetimeCancellationSource.Token);

        try
        {
            this.Logger.LogDebug("Loading audio to the player.");
            newDecoder = await this._decoderWorkPool.RunAsync(
                decoderFactory,
                linkedCancellationSource.Token).ConfigureAwait(false);

            lock (this._syncRoot)
            {
                if (this._disposed)
                {
                    newDecoder.Dispose();
                    newDecoder = null;
                    return false;
                }

                this.CurrentDecoder = newDecoder;
                this.Duration = newDecoder.StreamInfo.Duration;
                this.IsLoaded = true;
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
            newDecoder?.Dispose();
            this.Logger.LogDebug("Failed to load audio: {Message}", exception.Message);
            return false;
        }
        finally
        {
            lock (this._syncRoot)
            {
                this._loading = false;
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

    protected virtual void SetAndRaiseStateChanged(PlaybackState state)
    {
        Action<PlaybackState>? handler;

        lock (this._syncRoot)
        {
            handler = this.SetStateLocked(state);
        }

        this.RaiseStateChanged(handler, state);
    }

    private void RunDecoder(AudioPlaybackContext context, TimeSpan startPosition, CancellationToken cancellationToken)
    {
        Exception? completionException = null;
        using CancellationTokenRegistration decoderInterruptRegistration = cancellationToken.Register(
            static state => ((AudioPlaybackContext)state!).RequestDecoderInterrupt(),
            context);

        try
        {
            this.ResetDecoderInterrupt(context.Decoder, cancellationToken);
            if (!context.Decoder.TrySeek(startPosition, out string? seekError))
            {
                this.Logger.LogDebug("Unable to seek audio stream to {Position}: {Error}", startPosition, seekError);
            }

            Task<bool> seekAvailableTask = context.SeekRequests.Reader.WaitToReadAsync().AsTask();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.ProcessPendingSeekRequests(context, cancellationToken, ref seekAvailableTask);

                int generation = context.Generation;
                AudioDecoderResult result = context.Decoder.DecodeNextFrame();
                cancellationToken.ThrowIfCancellationRequested();

                if (this.ProcessPendingSeekRequests(context, cancellationToken, ref seekAvailableTask))
                {
                    continue;
                }

                if (result.Frame is not null)
                {
                    this.WriteFrame(context, new PlaybackAudioFrame(result.Frame, generation), cancellationToken, ref seekAvailableTask);
                }

                if (result.IsEOF)
                {
                    if (this.ShouldFinishAtEndOfFile(context))
                    {
                        break;
                    }

                    this.ProcessPendingSeekRequests(context, cancellationToken, ref seekAvailableTask);
                    continue;
                }

                if (!result.IsSucceeded)
                {
                    this.RecoverDecoder(context, result, cancellationToken);
                }
            }
        }
        catch (Exception exception)
        {
            completionException = exception;
            throw;
        }
        finally
        {
            lock (this._syncRoot)
            {
                context.AcceptsSeekRequests = false;
                context.SeekRequests.Writer.TryComplete();
            }

            this.CompletePendingSeekRequests(context, completionException);
            context.Frames.Writer.TryComplete(completionException);
        }
    }

    private async Task RunEngineAsync(AudioPlaybackContext context, Task engineStartTask)
    {
        await engineStartTask.WaitAsync(context.Token).ConfigureAwait(false);

        PlaybackAudioFrame? pendingFrame = null;
        DateTime playbackStartTime = DateTime.UtcNow;
        TimeSpan totalPauseDuration = TimeSpan.Zero;
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
                playbackStartTime = DateTime.UtcNow - TimeSpan.FromMilliseconds(playbackFrame.Frame.PresentationTime);
                totalPauseDuration = TimeSpan.Zero;
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

                DateTime pauseStartTime = DateTime.UtcNow;
                await context.WaitWhilePausedAsync(context.Token).ConfigureAwait(false);
                totalPauseDuration += DateTime.UtcNow - pauseStartTime;
                isFirstAudioFrame = true;
                lastEventSent = false;
            }

            if (playbackFrame.Generation != context.Generation)
            {
                return;
            }

            DateTime targetPlayTime = playbackStartTime
                .AddMilliseconds(playbackFrame.Frame.PresentationTime)
                .Add(totalPauseDuration);
            TimeSpan delay = targetPlayTime - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, context.Token).ConfigureAwait(false);
            }

            if (context.IsPaused)
            {
                return;
            }

            if (playbackFrame.Generation != context.Generation)
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
                if (!context.Frames.Reader.TryRead(out PlaybackAudioFrame? playbackFrame))
                {
                    if (!context.IsPaused)
                    {
                        this.SetAndRaiseStateChanged(PlaybackState.Buffering);
                    }

                    if (!await context.Frames.Reader.WaitToReadAsync(context.Token).ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }

                if (playbackFrame.Generation != context.Generation)
                {
                    continue;
                }

                if (pendingFrame is not null && pendingFrame.Generation == context.Generation)
                {
                    await ProcessFrameAsync(pendingFrame, false).ConfigureAwait(false);
                }

                pendingFrame = playbackFrame;
            }

            if (pendingFrame is not null && pendingFrame.Generation == context.Generation)
            {
                await ProcessFrameAsync(pendingFrame, true).ConfigureAwait(false);
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
        Action<TimeSpan>? positionChangedHandler = null;
        Action<PlaybackState>? stateChangedHandler = null;

        try
        {
            await context.EngineTask.ConfigureAwait(false);
            await context.DecoderTask.ConfigureAwait(false);
        }
        catch (AudioPlaybackCanceledException)
        {
            throw;
        }
        catch (OperationCanceledException) when (context.IsCancellationRequested)
        {
            throw new AudioPlaybackCanceledException(context.Token);
        }
        finally
        {
            if (!context.DecoderTask.IsCompleted)
            {
                context.Cancel();
                this.RequestDecoderInterrupt(context.Decoder);

                try
                {
                    await context.DecoderTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 原始播放异常由上方任务传播，这里只等待解码工作归还线程池槽位。
                }
            }

            try
            {
                lock (this._syncRoot)
                {
                    if (ReferenceEquals(this._playbackContext, context))
                    {
                        this._playbackContext = null;
                        this.IsSeeking = false;
                        positionChangedHandler = this.SetPositionLocked(TimeSpan.Zero);
                        stateChangedHandler = this.SetStateLocked(PlaybackState.Idle);

                        if (this._disposed)
                        {
                            decoderToDispose = context.Decoder;
                            this.CurrentDecoder = null;
                        }
                        else
                        {
                            this.CurrentDecoder = context.Decoder;
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    decoderToDispose?.Dispose();
                }
                catch (Exception exception)
                {
                    this.Logger.LogError(exception, "释放音频解码器失败。");
                }

                try
                {
                    context.Dispose();
                }
                catch (Exception exception)
                {
                    this.Logger.LogError(exception, "释放音频播放上下文失败。");
                }

                this.RaisePositionChanged(positionChangedHandler, TimeSpan.Zero);
                this.RaiseStateChanged(stateChangedHandler, PlaybackState.Idle);
                context.CleanupCompletionSource.TrySetResult();
            }
        }
    }

    private async Task WaitForSeekAsync(AudioPlaybackContext context, AudioSeekRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private bool ProcessPendingSeekRequests(
        AudioPlaybackContext context,
        CancellationToken cancellationToken,
        ref Task<bool> seekAvailableTask)
    {
        bool processed = false;

        while (context.SeekRequests.Reader.TryRead(out AudioSeekRequest? request))
        {
            processed = true;

            try
            {
                this.ResetDecoderInterrupt(context.Decoder, cancellationToken);

                if (request.CancellationToken.IsCancellationRequested)
                {
                    request.Cancel();
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                using CancellationTokenRegistration seekInterruptRegistration = request.CancellationToken.Register(
                    static state => ((IAudioDecoder)state!).RequestInterrupt(),
                    context.Decoder);

                if (context.Decoder.TrySeek(request.Position, out string? error))
                {
                    context.IncrementGeneration();
                    this.ClearFrames(context);
                    this.SetAndRaisePositionChanged(request.Position);

                    if (!context.IsPaused)
                    {
                        this.SetAndRaiseStateChanged(PlaybackState.Buffering);
                    }

                    this.Logger.LogDebug("Successfully seeks to {Position}.", request.Position);
                }
                else
                {
                    this.Logger.LogDebug("Unable to seek audio stream: {Error}", error);
                }

                if (request.CancellationToken.IsCancellationRequested)
                {
                    request.Cancel();
                    this.ResetDecoderInterrupt(context.Decoder, cancellationToken);
                    continue;
                }

                request.Complete();
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                request.Cancel();
                this.ResetDecoderInterrupt(context.Decoder, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                request.Cancel();
                throw;
            }
            catch (Exception exception)
            {
                request.Fail(exception);
            }
        }

        if (processed)
        {
            seekAvailableTask = context.SeekRequests.Reader.WaitToReadAsync().AsTask();
        }

        return processed;
    }

    private void RecoverDecoder(AudioPlaybackContext context, AudioDecoderResult result, CancellationToken cancellationToken)
    {
        this.Logger.LogDebug("Failed to decode audio frame, retrying: {Error}", result.ErrorMessage);
        TimeSpan recoveryPosition = this.Position;
        IAudioDecoder previousDecoder = context.Decoder;

        IAudioDecoder? recoveredDecoder = this.CreateRecoveryDecoder(result, cancellationToken);
        if (recoveredDecoder is null)
        {
            throw new FFmpegException(result.ErrorMessage ?? "Unable to recover audio decoder.");
        }

        context.Decoder = recoveredDecoder;
        previousDecoder.Dispose();
        if (cancellationToken.IsCancellationRequested)
        {
            this.RequestDecoderInterrupt(recoveredDecoder);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!recoveredDecoder.TrySeek(recoveryPosition, out string? seekError))
        {
            this.Logger.LogDebug("Unable to seek recreated decoder to {Position}: {Error}", recoveryPosition, seekError);
        }

        context.IncrementGeneration();
        this.ClearFrames(context);

        lock (this._syncRoot)
        {
            if (ReferenceEquals(this._playbackContext, context))
            {
                this.CurrentDecoder = recoveredDecoder;
            }
        }
    }

    private void WriteFrame(
        AudioPlaybackContext context,
        PlaybackAudioFrame playbackFrame,
        CancellationToken cancellationToken,
        ref Task<bool> seekAvailableTask)
    {
        while (playbackFrame.Generation == context.Generation)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (context.Frames.Writer.TryWrite(playbackFrame))
            {
                return;
            }

            Task<bool> frameSpaceTask = context.Frames.Writer.WaitToWriteAsync(cancellationToken).AsTask();
            Task.WhenAny(frameSpaceTask, seekAvailableTask).GetAwaiter().GetResult();

            if (seekAvailableTask.IsCompleted)
            {
                if (!seekAvailableTask.GetAwaiter().GetResult())
                {
                    return;
                }

                this.ProcessPendingSeekRequests(context, cancellationToken, ref seekAvailableTask);
                return;
            }

            if (!frameSpaceTask.GetAwaiter().GetResult())
            {
                return;
            }
        }
    }

    private void ClearFrames(AudioPlaybackContext context)
    {
        while (context.Frames.Reader.TryRead(out _))
        {
        }
    }

    private bool ShouldFinishAtEndOfFile(AudioPlaybackContext context)
    {
        lock (this._syncRoot)
        {
            if (context.SeekRequests.Reader.TryPeek(out _))
            {
                return false;
            }

            context.AcceptsSeekRequests = false;
            return true;
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

    private void RequestDecoderInterrupt(IAudioDecoder decoder)
    {
        lock (this._syncRoot)
        {
            decoder.RequestInterrupt();
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

    private void ResetDecoderInterrupt(IAudioDecoder decoder, CancellationToken cancellationToken)
    {
        lock (this._syncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            decoder.ResetInterrupt();

            if (cancellationToken.IsCancellationRequested)
            {
                decoder.RequestInterrupt();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
    }
}
