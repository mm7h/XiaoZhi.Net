using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Server.Common.Contexts;

internal sealed record TextFontCapability(
    string Bundle,
    string Charset,
    int Size,
    int Bpp)
{
    public static TextFontCapability? Parse(JsonObject hello)
    {
        if (hello["features"] is not JsonObject features
            || features["glyph_push"] is not JsonValue flag || !flag.TryGetValue(out bool supported) || !supported
            || hello["text_font"] is not JsonObject font
            || font["bundle"] is not JsonValue bundleNode || !bundleNode.TryGetValue(out string? bundle)
            || string.IsNullOrWhiteSpace(bundle) || bundle.Length > 64
            || font["charset"] is not JsonValue charsetNode || !charsetNode.TryGetValue(out string? charset)
            || charset is not ("basic" or "common")
            || font["size"] is not JsonValue sizeNode || !sizeNode.TryGetValue(out int size) || size <= 0 || size > 64
            || font["bpp"] is not JsonValue bppNode || !bppNode.TryGetValue(out int bpp) || bpp is not (1 or 4))
        {
            return null;
        }
        return new TextFontCapability(
            bundle,
            charset,
            size,
            bpp);
    }
}
