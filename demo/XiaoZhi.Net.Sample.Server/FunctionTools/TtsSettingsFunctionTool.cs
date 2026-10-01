using System.ComponentModel;
using XiaoZhi.Net.Sample.Server.FunctionTools.TtsModels;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Common.Contexts;
using XiaoZhi.Net.Server.Abstractions.Common.Dtos.Tts;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Abstractions.Common.Enums.Tts;

namespace XiaoZhi.Net.Sample.Server.FunctionTools
{
    /// <summary>
    /// 修改TTS音色、语速和音调的扩展。
    /// 不支持 Sherpa 的全局单例模型
    /// </summary>
    internal sealed class TtsSettingsFunctionTool : PrivateFunctionTool
    {
        private static readonly IReadOnlyList<TtsPresetCatalog> s_catalogs =
        [
            CreateHuoshanCatalog(TtsProviderType.HuoshanBidirection, includePitch: false, useIntegerRate: true),
            CreateHuoshanCatalog(TtsProviderType.HuoshanHttpV3, includePitch: false, useIntegerRate: true),
            CreateHuoshanCatalog(TtsProviderType.HuoshanHttp, includePitch: true, useIntegerRate: false),
            new TtsPresetCatalog(
                TtsProviderType.AliyunRealtime,
                [
                    new TtsStringPreset("longanyang", "龙安阳", "longanyang"),
                    new TtsStringPreset("longanhuan", "龙安欢", "longanhuan_v3"),
                    new TtsStringPreset("longhuhu", "龙呼呼", "longhuhu_v3")
                ],
                CreateRatioPresets(),
                CreateRatioPresets("low", "低", "normal", "正常", "high", "高")),
            new TtsPresetCatalog(
                TtsProviderType.AliyunHttp,
                [
                    new TtsStringPreset("longanfengyue", "龙安风月", "longanfengyue"),
                    new TtsStringPreset("longanyuanfei", "龙安远飞", "longanyuanfei"),
                    new TtsStringPreset("longanlingxi", "龙安灵犀", "longanlingxi")
                ],
                CreateRatioPresets(),
                CreateRatioPresets("low", "低", "normal", "正常", "high", "高"))
        ];

        [Description("获取当前会话 TTS 的可选音色、语速和音调预设。返回的能力是此 Sample 工具允许使用的预设，并非云厂商账号的完整音色目录。")]
        public TtsCapabilitiesResponse GetTtsCapabilities()
        {
            TtsRuntimeState state = this.TtsController.GetState();
            TtsPresetCatalog? catalog = FindCatalog(state);
            if (catalog is null)
            {
                return TtsCapabilitiesResponse.Unsupported(
                    state,
                    "当前 TTS 实现或云端模型没有配置 Sample 预设，无法通过此工具修改。");
            }

            return new TtsCapabilitiesResponse(
                true,
                null,
                state.ProviderType,
                new TtsCurrentSettings(
                    DescribeCurrent(catalog.Voices, state.Settings.Voice),
                    DescribeCurrent(catalog.SpeechRates, state.Settings.SpeechRate),
                    DescribeCurrent(catalog.Pitches, state.Settings.Pitch)),
                catalog.Voices.Select(ToOption).ToArray(),
                catalog.SpeechRates.Select(ToOption).ToArray(),
                catalog.Pitches.Select(ToOption).ToArray());
        }

        [Description("修改当前会话的 TTS 音色、语速或音调。参数使用 GetTtsCapabilities 返回的 id 或名称；可只传需要修改的项。灿灿、爽快思思、温暖阿虎、龙安阳、龙安欢、龙呼呼、龙安风月、龙安远飞、龙安灵犀是可识别的示例音色名称。")]
        public FunctionReturn<string> SetTtsSettings([Description("音色预设的 id 或名称")] string? voice = null, [Description("语速预设的 id 或名称")] string? speechRate = null, [Description("音调预设的 id 或名称")] string? pitch = null)
        {
            TtsRuntimeState state = this.TtsController.GetState();
            TtsPresetCatalog? catalog = FindCatalog(state);
            if (catalog is null)
            {
                return CreateResult("当前 TTS 实现或云端模型未配置可切换预设。请先调用 GetTtsCapabilities。", ToolAction.Continue);
            }

            if (string.IsNullOrWhiteSpace(voice)
                && string.IsNullOrWhiteSpace(speechRate)
                && string.IsNullOrWhiteSpace(pitch))
            {
                return CreateResult("至少需要提供音色、语速或音调中的一项。", ToolAction.Continue);
            }

            var errors = new List<string>();
            TtsStringPreset? voicePreset = Resolve(catalog.Voices, voice, "音色", errors);
            TtsNumericPreset? speechRatePreset = Resolve(catalog.SpeechRates, speechRate, "语速", errors);
            TtsNumericPreset? pitchPreset = Resolve(catalog.Pitches, pitch, "音调", errors);
            if (errors.Count > 0)
            {
                return CreateResult(string.Join("；", errors), ToolAction.Continue);
            }

            TtsUpdateResult result = this.TtsController.Rebuild(new TtsRuntimeSettings(
                voicePreset?.Value,
                speechRatePreset?.Value,
                pitchPreset?.Value));
            if (!result.Succeeded)
            {
                return CreateResult(result.Message ?? "TTS 设置未更新，已保留原有设置。", ToolAction.Continue);
            }

            string changes = string.Join("、", new[]
            {
                voicePreset is null ? null : $"音色：{voicePreset.Name}",
                speechRatePreset is null ? null : $"语速：{speechRatePreset.Name}",
                pitchPreset is null ? null : $"音调：{pitchPreset.Name}"
            }.Where(static item => item is not null));
            return CreateResult($"TTS 设置已更新（{changes}），接下来的回复会使用新参数。", ToolAction.Continue);
        }

        private static TtsPresetCatalog CreateHuoshanCatalog(
            TtsProviderType providerType,
            bool includePitch,
            bool useIntegerRate) => new(
                providerType,
                [
                    new TtsStringPreset("cancan", "灿灿", "zh_female_cancan_mars_bigtts"),
                    new TtsStringPreset("sisi", "爽快思思", "zh_female_shuangkuaisisi_moon_bigtts"),
                    new TtsStringPreset("ahu", "温暖阿虎", "zh_male_wennuanahu_moon_bigtts")
                ],
                useIntegerRate
                    ? [
                        new TtsNumericPreset("slow", "慢速", -25F),
                        new TtsNumericPreset("normal", "正常", 0F),
                        new TtsNumericPreset("fast", "快速", 25F)
                    ]
                    : CreateRatioPresets(),
                includePitch
                    ? CreateRatioPresets("low", "低", "normal", "正常", "high", "高")
                    : []);

        private static TtsNumericPreset[] CreateRatioPresets(
            string slowId = "slow",
            string slowName = "慢速",
            string normalId = "normal",
            string normalName = "正常",
            string fastId = "fast",
            string fastName = "快速") =>
        [
            new TtsNumericPreset(slowId, slowName, 0.8F),
            new TtsNumericPreset(normalId, normalName, 1.0F),
            new TtsNumericPreset(fastId, fastName, 1.2F)
        ];

        private static TtsPresetCatalog? FindCatalog(TtsRuntimeState state) =>
            state.CanRebuild
                ? s_catalogs.FirstOrDefault(catalog => catalog.ProviderType == state.ProviderType)
                : null;

        private static TtsStringPreset? Resolve(
            IReadOnlyList<TtsStringPreset> presets,
            string? selection,
            string parameterName,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(selection))
            {
                return null;
            }

            TtsStringPreset? preset = presets.FirstOrDefault(item => Matches(item.Id, item.Name, selection));
            if (preset is null)
            {
                errors.Add($"不支持的{parameterName}“{selection}”，可选项：{DescribeOptions(presets)}");
            }
            return preset;
        }

        private static TtsNumericPreset? Resolve(
            IReadOnlyList<TtsNumericPreset> presets,
            string? selection,
            string parameterName,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(selection))
            {
                return null;
            }

            TtsNumericPreset? preset = presets.FirstOrDefault(item => Matches(item.Id, item.Name, selection));
            if (preset is null)
            {
                errors.Add($"不支持的{parameterName}“{selection}”，可选项：{DescribeOptions(presets)}");
            }
            return preset;
        }

        private static bool Matches(string id, string name, string selection) =>
            string.Equals(id, selection, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, selection, StringComparison.OrdinalIgnoreCase);

        private static string DescribeCurrent(IReadOnlyList<TtsStringPreset> presets, string? value) =>
            presets.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.Ordinal))?.Name
            ?? value
            ?? "未知";

        private static string DescribeCurrent(IReadOnlyList<TtsNumericPreset> presets, float? value) =>
            presets.FirstOrDefault(item => value.HasValue && MathF.Abs(item.Value - value.Value) < 0.0001F)?.Name
            ?? value?.ToString("0.##")
            ?? "未知";

        private static TtsPresetOption ToOption(TtsStringPreset preset) => new(preset.Id, preset.Name);

        private static TtsPresetOption ToOption(TtsNumericPreset preset) => new(preset.Id, preset.Name);

        private static string DescribeOptions(IEnumerable<TtsStringPreset> presets) => string.Join("、", presets.Select(item => item.Name));

        private static string DescribeOptions(IEnumerable<TtsNumericPreset> presets) => string.Join("、", presets.Select(item => item.Name));

        private static FunctionReturn<string> CreateResult(string response, ToolAction action) => new()
        {
            Next = action,
            Result = response,
            Response = response
        };
    }
}
