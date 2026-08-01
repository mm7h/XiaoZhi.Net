namespace XiaoZhi.Net.Server.Media.Abstractions.Common.Enums
{
    /// <summary>
    /// 音量过渡曲线类型
    /// </summary>
    public enum VolumeTransitionCurve
    {
        /// <summary>
        /// 线性过渡
        /// </summary>
        Linear,
        /// <summary>
        /// 对数过渡（更自然）
        /// </summary>
        Logarithmic,
        /// <summary>
        /// 正弦过渡（平滑）
        /// </summary>
        Sine,
        /// <summary>
        /// 指数过渡（快速开始，慢速结束）
        /// </summary>
        Exponential
    }
}
