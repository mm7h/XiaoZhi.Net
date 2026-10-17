namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

internal interface IAudioDecodeScheduledWorkItem
{
    bool IsLoad { get; }

    void Execute(AudioDecodeScheduler scheduler, CancellationToken shutdownCancellationToken);

    void Cancel(CancellationToken cancellationToken);
}
