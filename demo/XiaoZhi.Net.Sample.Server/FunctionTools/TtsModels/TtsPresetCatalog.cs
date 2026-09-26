using System.Collections.Generic;
using XiaoZhi.Net.Server.Abstractions.Common.Enums.Tts;

namespace XiaoZhi.Net.Sample.Server.FunctionTools.TtsModels
{
    internal sealed record TtsPresetCatalog(
        TtsProviderType ProviderType,
        IReadOnlyList<TtsStringPreset> Voices,
        IReadOnlyList<TtsNumericPreset> SpeechRates,
        IReadOnlyList<TtsNumericPreset> Pitches);
}
