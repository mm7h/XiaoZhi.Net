using System.Threading.Channels;
using XiaoZhi.Net.Server.Media.Decoders;

namespace XiaoZhi.Net.Server.Media.Players.Contexts;

internal sealed class AudioPlaybackContext : IDisposable
{
    private readonly object _pauseSyncRoot = new();
    private readonly CancellationTokenSource _cancellationSource;
    private readonly CancellationTokenRegistration _decoderInterruptRegistration;
    private IAudioDecoder _decoder;
    private TaskCompletionSource _resumeSource = CreateCompletedResumeSource();
    private int _generation;
    private bool _disposed;
    private bool _paused;

    public AudioPlaybackContext(IAudioDecoder decoder, int frameBufferCapacity, CancellationToken cancellationToken)
    {
        this._decoder = decoder;
        this._cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this._decoderInterruptRegistration = this._cancellationSource.Token.Register(
            static state =>
            {
                AudioPlaybackContext context = (AudioPlaybackContext)state!;
                context.RequestDecoderInterrupt();
            },
            this);
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

    public Task CleanupTask => this.CleanupCompletionSource.Task;

    public bool AcceptsSeekRequests { get; set; } = true;

    public IAudioDecoder Decoder
    {
        get => Volatile.Read(ref this._decoder);
        set => Volatile.Write(ref this._decoder, value);
    }

    public Task DecoderTask { get; set; } = Task.CompletedTask;

    public Task EngineTask { get; set; } = Task.CompletedTask;

    public Channel<PlaybackAudioFrame> Frames { get; }

    public int Generation => Volatile.Read(ref this._generation);

    public bool IsCancellationRequested => this._cancellationSource.IsCancellationRequested;

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

    public CancellationToken Token => this._cancellationSource.Token;

    public void Cancel()
    {
        this._cancellationSource.Cancel();
        this.Resume();
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
        this._decoderInterruptRegistration.Dispose();
        this._cancellationSource.Dispose();
    }

    public int IncrementGeneration()
    {
        return Interlocked.Increment(ref this._generation);
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

    public void RequestDecoderInterrupt()
    {
        this.Decoder.RequestInterrupt();
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

    public Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        lock (this._pauseSyncRoot)
        {
            return this._paused ? this._resumeSource.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
        }
    }

    private static TaskCompletionSource CreateCompletedResumeSource()
    {
        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        completionSource.SetResult();
        return completionSource;
    }
}
