using XiaoZhi.Net.Server.Media.Abstractions.Processors;

namespace XiaoZhi.Net.Server.Media.Processors;

/// <summary>
/// A sample processor that simply multiplies the given audio sample to a desired volume.
/// This class cannot be inherited.
/// <para>Implements: <see cref="SampleProcessorBase"/>.</para>
/// </summary>
/// <remarks>
/// Initializes <see cref="VolumeProcessor"/>. The volume range should be between 0f and 1f.
/// </remarks>
/// <param name="initialVolume">Initial desired audio volume.</param>
internal class VolumeProcessor(float initialVolume = 1.0f) : SampleProcessorBase
{

    /// <summary>
    /// Gets or sets desired volume.
    /// </summary>
    public float Volume { get; set; } = initialVolume;

    /// <inheritdoc />
    public override float Process(float sample)
    {
        return sample * this.Volume;
    }
}
