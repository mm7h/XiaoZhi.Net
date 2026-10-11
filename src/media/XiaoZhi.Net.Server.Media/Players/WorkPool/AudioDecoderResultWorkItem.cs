using XiaoZhi.Net.Server.Media.Abstractions.Exceptions;

namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

internal sealed class AudioDecoderResultWorkItem<TResult> : AudioDecoderWorkItem
{
    private readonly TaskCompletionSource<TResult> _completionSource;
    private readonly Func<CancellationToken, TResult> _function;

    public AudioDecoderResultWorkItem(Func<CancellationToken, TResult> function, CancellationToken cancellationToken)
        : base(cancellationToken)
    {
        this._function = function;
        this._completionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task<TResult> Task => this._completionSource.Task;

    public override void Execute(CancellationToken shutdownCancellationToken)
    {
        using CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(this.CancellationToken, shutdownCancellationToken);
        CancellationToken cancellationToken = cancellationSource.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TResult result = this._function(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                if (result is IDisposable disposableResult)
                {
                    disposableResult.Dispose();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            this._completionSource.TrySetResult(result);
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
