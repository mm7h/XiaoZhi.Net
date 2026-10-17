using XiaoZhi.Net.Server.Abstractions.Common.Dtos.Tts;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    /// <summary>
    /// 为私有函数工具提供当前会话 TTS 的状态读取与运行时更新能力。
    /// </summary>
    public interface ITtsController
    {
        TtsRuntimeState GetState();

        TtsUpdateResult Rebuild(TtsRuntimeSettings settings);
    }
}
