using System.Text.Json.Nodes;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IFont : IResource<GlyphPushSetting>
    {
        JsonObject? CreatePayload(TextFontCapability? capability, string text);
    }
}
