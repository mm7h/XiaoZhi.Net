using XiaoZhi.Net.Server.Media.Abstractions.Exceptions;

namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

internal sealed class AudioDecoderActionWorkItem : AudioDecoderWorkItem
{
    private readonly Action<CancellationToken> _action;
    private readonly TaskCompletionSource _completionSource;

    public AudioDecoderActionWorkItem(Action<CancellationToken> action, CancellationToken cancellationToken)
        : base(cancellationToken)
    {
        this._action = action;
        this._completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task Task => this._completionSource.Task;

    public override void Execute(CancellationToken shutdownCancellationToken)
    {
        using CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(this.CancellationToken, shutdownCancellationToken);
        CancellationToken cancellationToken = cancellationSource.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            this._action(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            this._completionSource.TrySetResult();
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            this.Cancel(cancellationToken);
        }
        catch (Exception exception)
        {
            this._completionSource.TrySetException(exception);
        }
    }

    public override void Cancel(CancellationToken cancellationToken)
    {
        this._completionSource.TrySetException(new AudioPlaybackCanceledException(cancellationToken));
    }
}
