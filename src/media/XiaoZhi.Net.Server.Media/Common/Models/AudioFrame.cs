namespace XiaoZhi.Net.Server.Media.Common.Models;

/// <summary>
/// 表示音频帧对象。每个音频帧都包含其呈现时间以及可写入输出设备的原始音频数据。
/// 此类不能被继承。
/// </summary>
internal sealed record AudioFrame
{
    /// <summary>
    /// 初始化 <see cref="AudioFrame"/> 对象。
    /// </summary>
    /// <param name="presentationTime">以毫秒为单位的音频帧呈现时间。</param>
    /// <param name="data">可写入输出设备的 <c>Float32</c> 格式音频采样数据。</param>
    public AudioFrame(double presentationTime, byte[] data)
    {
        this.PresentationTime = presentationTime;
        this.Data = data;
    }

    /// <summary>
    /// 获取以毫秒为单位的帧呈现时间。
    /// </summary>
    public double PresentationTime { get; }

    /// <summary>
    /// 获取可写入输出设备的 <c>Float32</c> 格式音频采样数据。
    /// </summary>
    public byte[] Data { get; }
}
