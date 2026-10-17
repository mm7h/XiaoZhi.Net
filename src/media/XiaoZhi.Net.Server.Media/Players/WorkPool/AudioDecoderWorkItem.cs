namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

internal abstract class AudioDecoderWorkItem
{
    protected AudioDecoderWorkItem(CancellationToken cancellationToken)
    {
        this.CancellationToken = cancellationToken;
    }

    protected CancellationToken CancellationToken { get; }

    public abstract void Execute(CancellationToken shutdownCancellationToken);

    public abstract void Cancel(CancellationToken cancellationToken);
}
