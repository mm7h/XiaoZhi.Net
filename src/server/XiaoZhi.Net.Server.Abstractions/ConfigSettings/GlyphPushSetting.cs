namespace XiaoZhi.Net.Server.Abstractions.ConfigSettings;

/// <summary>
/// 可选的服务端补字字体包配置。
/// </summary>
public sealed class GlyphPushSetting
{
    /// <summary>解压后的 Noto full 字体包 manifest.json 路径；为空时不启用补字。</summary>
    public string? ManifestPath { get; set; }
}
