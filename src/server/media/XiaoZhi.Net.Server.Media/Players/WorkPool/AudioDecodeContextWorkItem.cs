using XiaoZhi.Net.Server.Media.Players.Contexts;

namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

internal sealed class AudioDecodeContextWorkItem(AudioDecodeScheduleRequest request) : IAudioDecodeScheduledWorkItem
{
    public bool IsLoad => false;

    public void Cancel(CancellationToken cancellationToken)
    {
        this.Request.Context.CancelScheduledWork(this.Request.ScheduleVersion, cancellationToken);
    }

    public void Execute(AudioDecodeScheduler scheduler, CancellationToken shutdownCancellationToken)
    {
        this.Request.Context.ExecuteScheduledWork(this.Request, scheduler, shutdownCancellationToken);
    }

    private AudioDecodeScheduleRequest Request { get; } = request;
}
