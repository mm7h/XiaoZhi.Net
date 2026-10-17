namespace XiaoZhi.Net.Server.Common.Contexts.Aliyun
{
    /// <summary>
    /// 阿里云实时语音合成的运行参数。
    /// </summary>
    internal sealed record AliyunRealtimeTtsOptions(
        string Endpoint,
        string ApiKey,
        string ModelName,
        string Voice,
        int SampleRate,
        int Volume,
        float Rate,
        float Pitch,
        string[] LanguageHints,
        string? Instruction,
        int ConnectionTimeoutSeconds,
        int ResponseTimeoutSeconds);
}
