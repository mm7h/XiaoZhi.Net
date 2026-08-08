using XiaoZhi.Net.Server.Media.Abstractions.Exceptions;

namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

/// <summary>
/// 使用固定数量后台专用线程执行阻塞式音频解码工作。
/// </summary>
internal sealed class AudioDecoderWorkPool : IAudioDecoderWorkPool
{
    private readonly CancellationTokenSource _shutdownCancellationSource = new();
    private readonly SemaphoreSlim _availableSlots;
    private readonly Queue<AudioDecoderWorkItem> _workItems = new();
    private readonly Thread[] _workers;
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _workAvailable = new(0);
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="AudioDecoderWorkPool"/> 的新实例。
    /// </summary>
    /// <param name="workerCount">专用解码线程数量。</param>
    public AudioDecoderWorkPool(int workerCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);

        this._availableSlots = new SemaphoreSlim(workerCount, workerCount);
        this._workers = new Thread[workerCount];

        for (int index = 0; index < this._workers.Length; index++)
        {
            Thread worker = new(this.RunWorker)
            {
                IsBackground = true,
                Name = $"AudioDecoderWorkPool_{index}"
            };

            this._workers[index] = worker;
            worker.Start();
        }
    }

    /// <inheritdoc />
    public Task RunAsync(Action<CancellationToken> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        AudioDecoderActionWorkItem workItem = new(action, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            workItem.Cancel(cancellationToken);
            return workItem.Task;
        }

        this.Enqueue(workItem);
        return workItem.Task;
    }

    /// <inheritdoc />
    public Task<TResult> RunAsync<TResult>(Func<CancellationToken, TResult> function, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);

        AudioDecoderResultWorkItem<TResult> workItem = new(function, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            workItem.Cancel(cancellationToken);
            return workItem.Task;
        }

        this.Enqueue(workItem);
        return workItem.Task;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        AudioDecoderWorkItem[] pendingWorkItems;

        lock (this._syncRoot)
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            pendingWorkItems = this._workItems.ToArray();
            this._workItems.Clear();
        }

        this._shutdownCancellationSource.Cancel();

        foreach (AudioDecoderWorkItem workItem in pendingWorkItems)
        {
            workItem.Cancel(this._shutdownCancellationSource.Token);
            this._availableSlots.Release();
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
        this._availableSlots.Dispose();
        this._shutdownCancellationSource.Dispose();
    }

    private void Enqueue(AudioDecoderWorkItem workItem)
    {
        lock (this._syncRoot)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);

            if (!this._availableSlots.Wait(0))
            {
                throw new AudioPlaybackCapacityExceededException();
            }

            try
            {
                this._workItems.Enqueue(workItem);
                this._workAvailable.Release();
            }
            catch
            {
                this._availableSlots.Release();
                throw;
            }
        }
    }

    private void RunWorker()
    {
        while (true)
        {
            this._workAvailable.Wait();

            AudioDecoderWorkItem? workItem;
            lock (this._syncRoot)
            {
                if (this._workItems.Count == 0)
                {
                    if (this._disposed)
                    {
                        return;
                    }

                    continue;
                }

                workItem = this._workItems.Dequeue();
            }

            try
            {
                workItem.Execute(this._shutdownCancellationSource.Token);
            }
            finally
            {
                this._availableSlots.Release();
            }
        }
    }
}
