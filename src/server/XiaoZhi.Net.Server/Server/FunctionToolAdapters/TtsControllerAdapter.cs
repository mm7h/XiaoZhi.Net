using System;
using XiaoZhi.Net.Server.Abstractions.Common.Dtos.Tts;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    /// <summary>
    /// 将公开的函数工具 TTS 契约适配到当前会话的内部 Provider。
    /// </summary>
    internal sealed class TtsControllerAdapter : ITtsController
    {
        private readonly Session _session;

        public TtsControllerAdapter(Session session)
        {
            this._session = session;
        }

        public TtsRuntimeState GetState()
        {
            ITts? tts = this._session.PrivateProvider.Tts;
            return tts?.GetRuntimeState() ?? TtsRuntimeState.Unsupported;
        }

        public TtsUpdateResult Rebuild(TtsRuntimeSettings settings)
        {
            ITts? tts = this._session.PrivateProvider.Tts;
            if (tts is null)
            {
                return new TtsUpdateResult(false, "当前会话没有可用的 TTS Provider。");
            }

            TtsRuntimeState state = tts.GetRuntimeState();
            if (!state.CanRebuild)
            {
                return new TtsUpdateResult(false, "当前 TTS Provider 不支持运行时更新。");
            }

            if (string.IsNullOrWhiteSpace(settings.Voice)
                && !settings.SpeechRate.HasValue
                && !settings.Pitch.HasValue)
            {
                return new TtsUpdateResult(false, "至少需要提供一个 TTS 参数。");
            }

            try
            {
                var modelSetting = new ModelSetting { ModelName = tts.ModelName };
                if (!string.IsNullOrWhiteSpace(settings.Voice))
                {
                    modelSetting.Config.SetConfigValue("Voice", settings.Voice);
                }
                if (settings.SpeechRate.HasValue)
                {
                    modelSetting.Config.SetConfigValue("SpeechRate", settings.SpeechRate.Value);
                }
                if (settings.Pitch.HasValue)
                {
                    modelSetting.Config.SetConfigValue("Pitch", settings.Pitch.Value);
                }

                return tts.Rebuild(modelSetting)
                    ? new TtsUpdateResult(true)
                    : new TtsUpdateResult(false, "TTS Provider 拒绝了这组运行时参数。");
            }
            catch (Exception)
            {
                return new TtsUpdateResult(false, "TTS 参数更新失败，当前设置未改变。");
            }
        }
    }
}
