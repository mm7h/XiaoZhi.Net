namespace XiaoZhi.Net.Server.Media.Players.Contexts;

internal sealed class AudioSeekRequest
{
    private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AudioSeekRequest(TimeSpan position, CancellationToken cancellationToken)
    {
        this.Position = position;
        this.CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }

    public TimeSpan Position { get; }

    public Task Task => this._completionSource.Task;

    public void Complete()
    {
        this._completionSource.TrySetResult();
    }

    public void Cancel()
    {
        this._completionSource.TrySetCanceled(this.CancellationToken);
    }

    public void Fail(Exception exception)
    {
        this._completionSource.TrySetException(exception);
    }
}
