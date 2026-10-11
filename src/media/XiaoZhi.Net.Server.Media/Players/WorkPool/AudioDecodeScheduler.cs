using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Exceptions;
using XiaoZhi.Net.Server.Media.Players.Contexts;

namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

/// <summary>
/// 使用固定数量后台专用线程执行短批次 FFmpeg 操作。
/// </summary>
internal sealed class AudioDecodeScheduler : IAudioDecodeScheduler
{
    private readonly CancellationTokenSource _shutdownCancellationSource = new();
    private readonly SemaphoreSlim _playbackContextSlots;
    private readonly PriorityQueue<IAudioDecodeScheduledWorkItem, (int Priority, long Deadline, long Sequence)> _workItems = new();
    private readonly HashSet<AudioPlaybackContext> _scheduledContexts = new();
    private readonly HashSet<AudioPlaybackContext> _bufferWaiterSet = new();
    private readonly Queue<AudioPlaybackContext> _bufferWaiters = new();
    private readonly Thread[] _workers;
    private readonly object _bufferSyncRoot = new();
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _workAvailable = new(0);
    private readonly int _maxConcurrentLoads;
    private readonly int _maxQueuedWorkItems;
    private long _bufferedBytes;
    private int _activeLoads;
    private long _nextSequence;
    private bool _disposed;

    public AudioDecodeScheduler(AudioPlayerOptions options)
    {
        AudioPlayerOptionsValidator.Validate(options);

        this.Options = options;
        this._maxConcurrentLoads = options.MaxConcurrentLoads;
        this._maxQueuedWorkItems = Math.Max(64, checked((options.MaxPlaybackContexts * 4) + options.MaxConcurrentLoads));
        this._playbackContextSlots = new SemaphoreSlim(options.MaxPlaybackContexts, options.MaxPlaybackContexts);
        this._workers = new Thread[options.DecoderWorkerCount];

        for (int index = 0; index < this._workers.Length; index++)
        {
            Thread worker = new(this.RunWorker)
            {
                IsBackground = true,
                Name = $"AudioDecodeScheduler_{index}"
            };

            this._workers[index] = worker;
            worker.Start();
        }
    }

    public AudioPlayerOptions Options { get; }

    public Task RunAsync(Action<CancellationToken> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AudioDecodeDelegateWorkItem workItem = new(
            true,
            shutdownCancellationToken =>
            {
                using CancellationTokenSource linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownCancellationToken);

                try
                {
                    linkedCancellationSource.Token.ThrowIfCancellationRequested();
                    action(linkedCancellationSource.Token);
                    completionSource.TrySetResult();
                }
                catch (OperationCanceledException) when (linkedCancellationSource.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(linkedCancellationSource.Token);
                }
                catch (Exception exception)
                {
                    completionSource.TrySetException(exception);
                }
            },
            token => completionSource.TrySetCanceled(token));

        if (cancellationToken.IsCancellationRequested)
        {
            completionSource.TrySetCanceled(cancellationToken);
            return completionSource.Task;
        }

        this.Enqueue(workItem, AudioDecodeWorkPriority.Load, TimeProvider.System.GetTimestamp());
        return completionSource.Task;
    }

    public Task<TResult> RunAsync<TResult>(Func<CancellationToken, TResult> function, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);

        TaskCompletionSource<TResult> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AudioDecodeDelegateWorkItem workItem = new(
            true,
            shutdownCancellationToken =>
            {
                using CancellationTokenSource linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownCancellationToken);

                try
                {
                    linkedCancellationSource.Token.ThrowIfCancellationRequested();
                    completionSource.TrySetResult(function(linkedCancellationSource.Token));
                }
                catch (OperationCanceledException) when (linkedCancellationSource.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(linkedCancellationSource.Token);
                }
                catch (Exception exception)
                {
                    completionSource.TrySetException(exception);
                }
            },
            token => completionSource.TrySetCanceled(token));

        if (cancellationToken.IsCancellationRequested)
        {
            completionSource.TrySetCanceled(cancellationToken);
            return completionSource.Task;
        }

        this.Enqueue(workItem, AudioDecodeWorkPriority.Load, TimeProvider.System.GetTimestamp());
        return completionSource.Task;
    }

    public Task RunCleanupAsync(Action<CancellationToken> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AudioDecodeDelegateWorkItem workItem = new(
            false,
            shutdownCancellationToken =>
            {
                try
                {
                    action(shutdownCancellationToken);
                    completionSource.TrySetResult();
                }
                catch (OperationCanceledException) when (shutdownCancellationToken.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(shutdownCancellationToken);
                }
                catch (Exception exception)
                {
                    completionSource.TrySetException(exception);
                }
            },
            token => completionSource.TrySetCanceled(token));

        this.Enqueue(workItem, AudioDecodeWorkPriority.StopOrDispose, TimeProvider.System.GetTimestamp());
        return completionSource.Task;
    }

    public bool TryAcquirePlaybackContext()
    {
        this.ThrowIfDisposed();
        return this._playbackContextSlots.Wait(0);
    }

    public void ReleasePlaybackContext()
    {
        this._playbackContextSlots.Release();
    }

    public bool TryReserveBuffer(long bytes, AudioPlaybackContext context)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bytes, 0);
        ArgumentNullException.ThrowIfNull(context);

        lock (this._bufferSyncRoot)
        {
            if (bytes <= this.Options.MaxGlobalBufferedBytes - this._bufferedBytes)
            {
                this._bufferedBytes += bytes;
                context.ClearGlobalBufferWait();
                return true;
            }

            if (context.MarkWaitingForGlobalBuffer() && this._bufferWaiterSet.Add(context))
            {
                this._bufferWaiters.Enqueue(context);
            }

            return false;
        }
    }

    public void ReleaseBuffer(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        AudioPlaybackContext? contextToWake = null;
        lock (this._bufferSyncRoot)
        {
            this._bufferedBytes -= bytes;

            while (this._bufferWaiters.TryDequeue(out AudioPlaybackContext? candidate))
            {
                this._bufferWaiterSet.Remove(candidate);
                if (candidate.TryWakeGlobalBufferWait())
                {
                    contextToWake = candidate;
                    break;
                }
            }
        }

        if (contextToWake is not null)
        {
            try
            {
                this.Schedule(contextToWake, AudioDecodeWorkPriority.Refill);
            }
            catch (ObjectDisposedException)
            {
                // 调度器关闭时，播放上下文会由关闭流程统一取消。
            }
        }
    }

    public void Schedule(AudioPlaybackContext context, AudioDecodeWorkPriority priority)
    {
        ArgumentNullException.ThrowIfNull(context);

        AudioDecodeScheduleRequest? request = context.RequestScheduling(priority, TimeProvider.System.GetTimestamp());
        if (request is not null)
        {
            this.EnqueueContextRequest(request.Value);
        }
    }

    public void Dispose()
    {
        IAudioDecodeScheduledWorkItem[] pendingWorkItems;
        AudioPlaybackContext[] scheduledContexts;

        lock (this._syncRoot)
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            pendingWorkItems = this._workItems.UnorderedItems.Select(static item => item.Element).ToArray();
            this._workItems.Clear();
            scheduledContexts = this._scheduledContexts.ToArray();
            this._scheduledContexts.Clear();
        }

        this._shutdownCancellationSource.Cancel();

        foreach (AudioPlaybackContext context in scheduledContexts)
        {
            context.Cancel();
        }

        foreach (IAudioDecodeScheduledWorkItem workItem in pendingWorkItems)
        {
            workItem.Cancel(this._shutdownCancellationSource.Token);
        }

        this._workAvailable.Release(this._workers.Length);

        foreach (Thread worker in this._workers)
        {
            if (worker != Thread.CurrentThread)
            {
                worker.Join();
            }
        }

        this._workAvailable.Dispose();
        this._playbackContextSlots.Dispose();
        this._shutdownCancellationSource.Dispose();
    }

    internal void CompleteContextWork(AudioPlaybackContext context, AudioDecodeBatchResult result)
    {
        AudioDecodeScheduleRequest? nextRequest = context.CompleteScheduledWork(result, TimeProvider.System.GetTimestamp());
        if (nextRequest is not null)
        {
            try
            {
                this.EnqueueContextRequest(nextRequest.Value);
            }
            catch (Exception exception)
            {
                context.FailScheduledWork(nextRequest.Value.ScheduleVersion, exception);
            }
        }
    }

    public void Unregister(AudioPlaybackContext context)
    {
        lock (this._syncRoot)
        {
            this._scheduledContexts.Remove(context);
        }
    }

    private void EnqueueContextRequest(AudioDecodeScheduleRequest request)
    {
        try
        {
            lock (this._syncRoot)
            {
                this._scheduledContexts.Add(request.Context);
            }

            this.Enqueue(new AudioDecodeContextWorkItem(request), request.Priority, request.DeadlineTimestamp);
        }
        catch (Exception exception)
        {
            this.Unregister(request.Context);
            request.Context.FailScheduledWork(request.ScheduleVersion, exception);
            throw;
        }
    }

    private void Enqueue(IAudioDecodeScheduledWorkItem workItem, AudioDecodeWorkPriority priority, long deadlineTimestamp)
    {
        lock (this._syncRoot)
        {
            this.ThrowIfDisposed();

            if (this._workItems.Count >= this._maxQueuedWorkItems)
            {
                throw new AudioPlaybackCapacityExceededException(AudioPlaybackCapacityExceededReason.SchedulerQueueFull);
            }

            long sequence = this._nextSequence++;
            this._workItems.Enqueue(workItem, ((int)priority, deadlineTimestamp, sequence));
        }

        this._workAvailable.Release();
    }

    private void RunWorker()
    {
        while (true)
        {
            this._workAvailable.Wait();

            if (!this.TryDequeue(out IAudioDecodeScheduledWorkItem? workItem) || workItem is null)
            {
                lock (this._syncRoot)
                {
                    if (this._disposed && this._workItems.Count == 0)
                    {
                        return;
                    }
                }

                continue;
            }

            try
            {
                workItem.Execute(this, this._shutdownCancellationSource.Token);
            }
            finally
            {
                if (workItem.IsLoad)
                {
                    Interlocked.Decrement(ref this._activeLoads);
                    this._workAvailable.Release();
                }
            }
        }
    }

    private bool TryDequeue(out IAudioDecodeScheduledWorkItem? workItem)
    {
        List<(IAudioDecodeScheduledWorkItem Item, (int Priority, long Deadline, long Sequence) Priority)>? deferredItems = null;

        lock (this._syncRoot)
        {
            while (this._workItems.TryDequeue(out IAudioDecodeScheduledWorkItem? candidate, out (int Priority, long Deadline, long Sequence) priority))
            {
                if (candidate.IsLoad && Volatile.Read(ref this._activeLoads) >= this._maxConcurrentLoads)
                {
                    deferredItems ??= [];
                    deferredItems.Add((candidate, priority));
                    continue;
                }

                if (candidate.IsLoad)
                {
                    Interlocked.Increment(ref this._activeLoads);
                }

                if (deferredItems is not null)
                {
                    foreach ((IAudioDecodeScheduledWorkItem deferredItem, (int Priority, long Deadline, long Sequence) deferredPriority) in deferredItems)
                    {
                        this._workItems.Enqueue(deferredItem, deferredPriority);
                    }
                }

                workItem = candidate;
                return true;
            }

            if (deferredItems is not null)
            {
                foreach ((IAudioDecodeScheduledWorkItem deferredItem, (int Priority, long Deadline, long Sequence) deferredPriority) in deferredItems)
                {
                    this._workItems.Enqueue(deferredItem, deferredPriority);
                }
            }
        }

        workItem = null;
        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
    }
}
