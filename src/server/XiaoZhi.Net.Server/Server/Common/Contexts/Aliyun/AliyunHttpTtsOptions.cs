namespace XiaoZhi.Net.Server.Common.Contexts.Aliyun
{
    /// <summary>
    /// 阿里云非实时 HTTP 语音合成的运行参数。
    /// </summary>
    internal sealed record AliyunHttpTtsOptions(
        string Endpoint,
        string ApiKey,
        string ModelName,
        string Voice,
        bool Streaming,
        int SampleRate,
        int Volume,
        float Rate,
        float Pitch,
        string[] LanguageHints,
        string? Instruction,
        int ResponseTimeoutSeconds);
}
