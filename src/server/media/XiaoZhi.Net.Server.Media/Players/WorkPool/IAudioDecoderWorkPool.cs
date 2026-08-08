namespace XiaoZhi.Net.Server.Media.Players.WorkPool;

/// <summary>
/// 为阻塞式音频解码提供固定专用线程的工作池。
/// </summary>
internal interface IAudioDecoderWorkPool : IDisposable
{
    /// <summary>
    /// 在解码工作池中执行操作。
    /// </summary>
    /// <param name="action">要执行的阻塞操作。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示操作完成的任务。</returns>
    Task RunAsync(Action<CancellationToken> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在解码工作池中执行操作并返回结果。
    /// </summary>
    /// <typeparam name="TResult">操作结果类型。</typeparam>
    /// <param name="function">要执行的阻塞操作。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示操作完成及其结果的任务。</returns>
    Task<TResult> RunAsync<TResult>(Func<CancellationToken, TResult> function, CancellationToken cancellationToken = default);
}
