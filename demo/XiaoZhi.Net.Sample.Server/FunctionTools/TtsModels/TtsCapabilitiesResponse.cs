using System.Collections.Generic;
using XiaoZhi.Net.Server.Abstractions.Common.Dtos.Tts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums.Tts;

namespace XiaoZhi.Net.Sample.Server.FunctionTools.TtsModels
{
    internal sealed record TtsCapabilitiesResponse(
        bool Supported,
        string? Message,
        TtsProviderType ProviderType,
        TtsCurrentSettings? Current,
        IReadOnlyList<TtsPresetOption> Voices,
        IReadOnlyList<TtsPresetOption> SpeechRates,
        IReadOnlyList<TtsPresetOption> Pitches)
    {
        public static TtsCapabilitiesResponse Unsupported(TtsRuntimeState state, string message) => new(
            false,
            message,
            state.ProviderType,
            null,
            [],
            [],
            []);
    }
}
