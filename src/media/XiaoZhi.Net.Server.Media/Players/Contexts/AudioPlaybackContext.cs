using System.Threading.Channels;
using XiaoZhi.Net.Server.Media.Decoders;
using XiaoZhi.Net.Server.Media.Players.WorkPool;

namespace XiaoZhi.Net.Server.Media.Players.Contexts;

internal sealed class AudioPlaybackContext : IDisposable
{
    private const int DecodeWorkIdle = 0;
    private const int DecodeWorkQueued = 1;
    private const int DecodeWorkRunning = 2;
    private readonly Func<AudioPlaybackContext, AudioDecodeBatchResult> _decodeBatch;
    private readonly object _decodeWorkSyncRoot = new();
    private readonly object _pauseSyncRoot = new();
    private readonly CancellationTokenSource _cancellationSource;
    private readonly IAudioDecodeScheduler _scheduler;
    private readonly TimeSpan _frameDuration;
    private readonly int _expectedFrameBytes;
    private readonly int _frameBufferCapacity;
    private IAudioDecoder _decoder;
    private TaskCompletionSource _decodeIdleSource = CreateCompletedSource();
    private TaskCompletionSource _resumeSource = CreateCompletedSource();
    private int _decodeWorkState;
    private int _generation;
    private int _queuedFrameCount;
    private int _scheduleVersion;
    private int _waitingForGlobalBuffer;
    private long _bufferedBytes;
    private bool _decoderWasInterrupted;
    private bool _disposed;
    private bool _endOfFile;
    private bool _fillAfterSeek;
    private bool _faulted;
    private bool _paused;
    private bool _rescheduleRequested;
    private bool _terminalFault;
    private AudioDecodeWorkPriority _reschedulePriority;
    private AudioDecodeWorkPriority _scheduledPriority;

    public AudioPlaybackContext(
        IAudioDecoder decoder,
        IAudioDecodeScheduler scheduler,
        int frameBufferCapacity,
        TimeSpan frameDuration,
        int expectedFrameBytes,
        TimeSpan startPosition,
        CancellationToken cancellationToken,
        Func<AudioPlaybackContext, AudioDecodeBatchResult> decodeBatch)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(decodeBatch);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameBufferCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expectedFrameBytes, 0);

        this._decoder = decoder;
        this._scheduler = scheduler;
        this._frameDuration = frameDuration;
        this._expectedFrameBytes = expectedFrameBytes;
        this._frameBufferCapacity = frameBufferCapacity;
        this._decodeBatch = decodeBatch;
        this.StartPosition = startPosition;
        this._cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this.Frames = Channel.CreateBounded<PlaybackAudioFrame>(new BoundedChannelOptions(frameBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });
        this.SeekRequests = Channel.CreateUnbounded<AudioSeekRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        this.CleanupCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public TaskCompletionSource CleanupCompletionSource { get; }

    public bool InitialSeekPending { get; private set; } = true;

    public Task CleanupTask => this.CleanupCompletionSource.Task;

    public TimeSpan BufferedDuration => TimeSpan.FromTicks(this._frameDuration.Ticks * Volatile.Read(ref this._queuedFrameCount));

    public long BufferedBytes => Volatile.Read(ref this._bufferedBytes);

    public IAudioDecoder Decoder
    {
        get => Volatile.Read(ref this._decoder);
        set => Volatile.Write(ref this._decoder, value);
    }

    public Task DecoderIdleTask
    {
        get
        {
            lock (this._decodeWorkSyncRoot)
            {
                return this._decodeIdleSource.Task;
            }
        }
    }

    public Task EngineTask { get; set; } = Task.CompletedTask;

    public bool EndOfFile => Volatile.Read(ref this._endOfFile);

    public Channel<PlaybackAudioFrame> Frames { get; }

    public int Generation => Volatile.Read(ref this._generation);

    public bool HasPendingSeekRequests => this.SeekRequests.Reader.TryPeek(out _);

    public bool IsCancellationRequested => this._cancellationSource.IsCancellationRequested;

    public bool IsFaulted => Volatile.Read(ref this._faulted);

    /// <summary>
    /// 获取是否发生了不应自动重建解码器的终止性故障。
    /// </summary>
    public bool IsTerminalFault => Volatile.Read(ref this._terminalFault);

    public bool IsPaused
    {
        get
        {
            lock (this._pauseSyncRoot)
            {
                return this._paused;
            }
        }
    }

    public Task PlaybackTask { get; set; } = Task.CompletedTask;

    public Channel<AudioSeekRequest> SeekRequests { get; }

    public TimeSpan StartPosition { get; }

    public CancellationToken Token => this._cancellationSource.Token;

    public bool WasInterrupted => Volatile.Read(ref this._decoderWasInterrupted);

    public bool CanDecodeMore()
    {
        if (this.IsCancellationRequested || this._faulted || this.EndOfFile)
        {
            return false;
        }

        if (this.IsPaused && !this._fillAfterSeek)
        {
            return false;
        }

        return this.BufferedDuration < this._scheduler.Options.TargetBufferDuration
            && Volatile.Read(ref this._queuedFrameCount) < this._frameBufferCapacity;
    }

    public bool ShouldRequestRefill()
    {
        if (this.IsCancellationRequested || this._faulted || this.EndOfFile || this.IsPaused)
        {
            return false;
        }

        return this.BufferedDuration < this._scheduler.Options.LowBufferDuration;
    }

    public void Cancel()
    {
        if (!this._cancellationSource.IsCancellationRequested)
        {
            this._cancellationSource.Cancel();
        }

        this.RequestDecoderInterrupt();
        this.Resume();
    }

    public void ClearFrames()
    {
        while (this.Frames.Reader.TryRead(out PlaybackAudioFrame? frame))
        {
            this.ReleaseQueuedFrame(frame);
        }
    }

    public void ClearGlobalBufferWait()
    {
        Interlocked.Exchange(ref this._waitingForGlobalBuffer, 0);
    }

    public void CompleteFrames(Exception? exception = null)
    {
        this.Frames.Writer.TryComplete(exception);
    }

    public AudioDecodeScheduleRequest? CompleteScheduledWork(AudioDecodeBatchResult result, long timestamp)
    {
        lock (this._decodeWorkSyncRoot)
        {
            this._decodeWorkState = DecodeWorkIdle;

            bool shouldReschedule = !this.IsCancellationRequested
                && !this._faulted
                && !this.EndOfFile
                && (result.ShouldReschedule || this._rescheduleRequested);

            AudioDecodeWorkPriority priority = this._rescheduleRequested
                ? MinPriority(result.Priority, this._reschedulePriority)
                : result.Priority;
            this._rescheduleRequested = false;

            if (!shouldReschedule)
            {
                this._decodeIdleSource.TrySetResult();
                return null;
            }

            this._decodeWorkState = DecodeWorkQueued;
            this._scheduledPriority = priority;
            int version = ++this._scheduleVersion;
            return this.CreateScheduleRequest(version, priority, timestamp);
        }
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this.SeekRequests.Writer.TryComplete();
        this.Frames.Writer.TryComplete();
        this.ClearFrames();
        this._cancellationSource.Dispose();
    }

    public void ExecuteScheduledWork(
        AudioDecodeScheduleRequest request,
        AudioDecodeScheduler scheduler,
        CancellationToken shutdownCancellationToken)
    {
        if (shutdownCancellationToken.IsCancellationRequested)
        {
            this.Cancel();
        }

        if (!this.TryStartScheduledWork(request.ScheduleVersion))
        {
            return;
        }

        AudioDecodeBatchResult result;
        try
        {
            result = this.IsCancellationRequested ? AudioDecodeBatchResult.Completed : this._decodeBatch(this);
        }
        catch (Exception exception)
        {
            this._faulted = true;
            this.CompleteFrames(exception);
            result = AudioDecodeBatchResult.Completed;
        }

        scheduler.CompleteContextWork(this, result);
    }

    public void FailScheduledWork(int scheduleVersion, Exception exception)
    {
        lock (this._decodeWorkSyncRoot)
        {
            if (this._decodeWorkState != DecodeWorkQueued || scheduleVersion != this._scheduleVersion)
            {
                return;
            }

            this._decodeWorkState = DecodeWorkIdle;
            this._faulted = true;
            this._decodeIdleSource.TrySetResult();
        }

        this.CompleteFrames(exception);
    }

    public int IncrementGeneration()
    {
        return Interlocked.Increment(ref this._generation);
    }

    public void MarkDecoderInterrupted()
    {
        Volatile.Write(ref this._decoderWasInterrupted, true);
    }

    /// <summary>
    /// 标记当前播放必须完全卸载资源后才能继续。
    /// </summary>
    public void MarkTerminalFault()
    {
        Volatile.Write(ref this._terminalFault, true);
    }

    public void ResetDecoderInterrupted()
    {
        Volatile.Write(ref this._decoderWasInterrupted, false);
    }

    public void MarkEndOfFile()
    {
        Volatile.Write(ref this._endOfFile, true);
        this.CompleteFrames();
    }

    public void CompleteInitialSeek()
    {
        this.InitialSeekPending = false;
    }

    public void Pause()
    {
        lock (this._pauseSyncRoot)
        {
            if (this._paused)
            {
                return;
            }

            this._paused = true;
            this._resumeSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public AudioDecodeScheduleRequest? RequestScheduling(AudioDecodeWorkPriority priority, long timestamp)
    {
        lock (this._decodeWorkSyncRoot)
        {
            if (this._faulted || this.EndOfFile)
            {
                return null;
            }

            if (this.IsCancellationRequested && priority != AudioDecodeWorkPriority.StopOrDispose)
            {
                return null;
            }

            switch (this._decodeWorkState)
            {
                case DecodeWorkIdle:
                    this._decodeIdleSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    this._decodeWorkState = DecodeWorkQueued;
                    this._scheduledPriority = priority;
                    return this.CreateScheduleRequest(++this._scheduleVersion, priority, timestamp);
                case DecodeWorkQueued when priority < this._scheduledPriority:
                    this._scheduledPriority = priority;
                    return this.CreateScheduleRequest(++this._scheduleVersion, priority, timestamp);
                case DecodeWorkRunning:
                    if (!this._rescheduleRequested)
                    {
                        this._reschedulePriority = priority;
                    }
                    else
                    {
                        this._reschedulePriority = MinPriority(this._reschedulePriority, priority);
                    }

                    this._rescheduleRequested = true;
                    return null;
                default:
                    return null;
            }
        }
    }

    public void RequestDecoderInterrupt()
    {
        this.Decoder.RequestInterrupt();
    }

    public void ResetDecoderInterrupt()
    {
        this.Decoder.ResetInterrupt();
    }

    public void Resume()
    {
        TaskCompletionSource resumeSource;

        lock (this._pauseSyncRoot)
        {
            this._paused = false;
            resumeSource = this._resumeSource;
        }

        resumeSource.TrySetResult();
    }

    public void StartFillingAfterSeek()
    {
        this._fillAfterSeek = true;
    }

    public bool TryReadFrame(out PlaybackAudioFrame? frame)
    {
        if (!this.Frames.Reader.TryRead(out frame))
        {
            return false;
        }

        this.ReleaseQueuedFrame(frame);
        return true;
    }

    public bool TryReserveFrame(out int reservedBytes)
    {
        reservedBytes = 0;

        if (!this.CanDecodeMore() || !this._scheduler.TryReserveBuffer(this._expectedFrameBytes, this))
        {
            return false;
        }

        reservedBytes = this._expectedFrameBytes;
        return true;
    }

    public bool TryWriteReservedFrame(int reservedBytes, PlaybackAudioFrame frame)
    {
        int actualBytes = frame.Frame.Data.Length;
        if (actualBytes > reservedBytes && !this._scheduler.TryReserveBuffer(actualBytes - reservedBytes, this))
        {
            this._scheduler.ReleaseBuffer(reservedBytes);
            return false;
        }

        if (actualBytes < reservedBytes)
        {
            this._scheduler.ReleaseBuffer(reservedBytes - actualBytes);
        }

        if (!this.Frames.Writer.TryWrite(frame))
        {
            this._scheduler.ReleaseBuffer(actualBytes);
            return false;
        }

        Interlocked.Increment(ref this._queuedFrameCount);
        Interlocked.Add(ref this._bufferedBytes, actualBytes);

        if (this._fillAfterSeek && this.BufferedDuration >= this._scheduler.Options.TargetBufferDuration)
        {
            this._fillAfterSeek = false;
        }

        return true;
    }

    public void ReleaseReservedFrame(int reservedBytes)
    {
        this._scheduler.ReleaseBuffer(reservedBytes);
    }

    public void CancelScheduledWork(int scheduleVersion, CancellationToken cancellationToken)
    {
        lock (this._decodeWorkSyncRoot)
        {
            if (this._decodeWorkState == DecodeWorkQueued && scheduleVersion == this._scheduleVersion)
            {
                this._decodeWorkState = DecodeWorkIdle;
                this._decodeIdleSource.TrySetCanceled(cancellationToken);
            }
        }
    }

    public bool MarkWaitingForGlobalBuffer()
    {
        return Interlocked.Exchange(ref this._waitingForGlobalBuffer, 1) == 0;
    }

    public bool TryWakeGlobalBufferWait()
    {
        if (Interlocked.Exchange(ref this._waitingForGlobalBuffer, 0) == 0)
        {
            return false;
        }

        return this.CanDecodeMore();
    }

    public Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        lock (this._pauseSyncRoot)
        {
            return this._paused ? this._resumeSource.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
        }
    }

    private AudioDecodeScheduleRequest CreateScheduleRequest(int version, AudioDecodeWorkPriority priority, long timestamp)
    {
        long deadlineTimestamp = timestamp + (long)(this.BufferedDuration.TotalSeconds * TimeProvider.System.TimestampFrequency);
        return new AudioDecodeScheduleRequest(this, version, this.Generation, priority, deadlineTimestamp);
    }

    private static AudioDecodeWorkPriority MinPriority(AudioDecodeWorkPriority first, AudioDecodeWorkPriority second)
    {
        return first <= second ? first : second;
    }

    private void ReleaseQueuedFrame(PlaybackAudioFrame frame)
    {
        int bytes = frame.Frame.Data.Length;
        Interlocked.Decrement(ref this._queuedFrameCount);
        Interlocked.Add(ref this._bufferedBytes, -bytes);
        this._scheduler.ReleaseBuffer(bytes);
    }

    private bool TryStartScheduledWork(int scheduleVersion)
    {
        lock (this._decodeWorkSyncRoot)
        {
            if (this._decodeWorkState != DecodeWorkQueued || scheduleVersion != this._scheduleVersion)
            {
                return false;
            }

            this._decodeWorkState = DecodeWorkRunning;
            return true;
        }
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
