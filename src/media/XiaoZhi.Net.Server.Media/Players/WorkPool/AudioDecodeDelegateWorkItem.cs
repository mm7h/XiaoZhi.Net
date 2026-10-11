namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

internal sealed class AudioDecodeDelegateWorkItem : IAudioDecodeScheduledWorkItem
{
    private readonly Action<CancellationToken> _action;
    private readonly Action<CancellationToken> _cancel;

    public AudioDecodeDelegateWorkItem(bool isLoad, Action<CancellationToken> action, Action<CancellationToken> cancel)
    {
        this.IsLoad = isLoad;
        this._action = action;
        this._cancel = cancel;
    }

    public bool IsLoad { get; }

    public void Cancel(CancellationToken cancellationToken)
    {
        this._cancel(cancellationToken);
    }

    public void Execute(AudioDecodeScheduler scheduler, CancellationToken shutdownCancellationToken)
    {
        this._action(shutdownCancellationToken);
    }
}
