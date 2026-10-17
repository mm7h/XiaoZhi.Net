using XiaoZhi.Net.Server.Media.Abstractions.Processors;

namespace XiaoZhi.Net.Server.Media.Processors;

/// <summary>
/// 表示通过将音频采样值乘以目标音量来处理音频采样的处理器。
/// 此类不能被继承。
/// <para>实现：<see cref="SampleProcessorBase"/>。</para>
/// </summary>
/// <remarks>
/// 初始化 <see cref="VolumeProcessor"/>。音量范围应介于 0f 和 1f 之间。
/// </remarks>
/// <param name="initialVolume">初始目标音量。</param>
internal class VolumeProcessor(float initialVolume = 1.0f) : SampleProcessorBase
{

    /// <summary>
    /// 获取或设置目标音量。
    /// </summary>
    public float Volume { get; set; } = initialVolume;

    /// <inheritdoc />
    public override float Process(float sample)
    {
        return sample * this.Volume;
    }
}
