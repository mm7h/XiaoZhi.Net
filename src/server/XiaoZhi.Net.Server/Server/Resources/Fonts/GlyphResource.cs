using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Resources.Fonts;

internal sealed class GlyphResource : IFont
{
    private readonly ILogger<GlyphResource> _logger;
    private readonly ConcurrentDictionary<string, Lazy<CbinFont>> _fonts = new();
    private readonly ConcurrentDictionary<TextFontCapability, byte> _warnedCapabilities = new();
    private readonly Dictionary<string, HashSet<int>> _charsets = new();
    private JsonElement _manifest;
    private string _root = string.Empty;
    public GlyphResource(ILogger<GlyphResource> logger) => this._logger = logger;
    public string ResourceName => nameof(GlyphResource);

    public bool Load(GlyphPushSetting settings)
    {
        this.Dispose();
        if (string.IsNullOrWhiteSpace(settings.ManifestPath))
        {
            return true;
        }
        try
        {
            string path = Path.GetFullPath(settings.ManifestPath);
            this._root = Path.GetDirectoryName(path)!;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement manifest = document.RootElement;
            foreach (JsonProperty charset in manifest.GetProperty("charsets").EnumerateObject())
            {
                if (charset.Value.ValueKind != JsonValueKind.Null)
                {
                    this._charsets.Add(charset.Name, charset.Value.GetProperty("codepoints").EnumerateArray().Select(value => value.GetInt32()).ToHashSet());
                }
            }
            manifest.GetProperty("bundle_id").GetString();
            manifest.GetProperty("profiles");
            manifest.GetProperty("shards");
            this._manifest = manifest.Clone();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        {
            this.Dispose();
            this._logger.LogWarning(ex, Lang.GlyphResource_LoadFailed, settings.ManifestPath);
            return false;
        }
    }

    public JsonObject? CreatePayload(TextFontCapability? capability, string text)
    {
        if (capability is null)
        {
            return null;
        }
        try
        {
            if (this._manifest.ValueKind != JsonValueKind.Object
                || this._manifest.GetProperty("bundle_id").GetString() != capability.Bundle
                || !this._charsets.TryGetValue(capability.Charset, out HashSet<int>? installed)
                || !this._manifest.GetProperty("profiles").EnumerateArray().Any(profile => profile.GetProperty("size").GetInt32() == capability.Size && profile.GetProperty("bpp").GetInt32() == capability.Bpp))
            {
                this.WarnUnavailable(capability);
                return null;
            }
            JsonArray glyphs = new();
            HashSet<int> seen = [];
            int bytes = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                int codepoint = rune.Value;
                if (Rune.IsControl(rune) || !seen.Add(codepoint) || installed.Contains(codepoint))
                {
                    continue;
                }
                CbinFont? font = this.FindFont(capability, codepoint);
                if (font is null)
                {
                    continue;
                }
                if (font.Bpp != capability.Bpp)
                {
                    throw new InvalidDataException(Lang.GlyphResource_CreatePayload_BppMismatch);
                }
                JsonObject? glyph = font.GetGlyph(codepoint);
                if (glyph is null)
                {
                    continue;
                }
                int bitmapBytes = (glyph["box_w"]!.GetValue<int>() * glyph["box_h"]!.GetValue<int>() * capability.Bpp + 7) / 8;
                if (glyphs.Count == 64 || bytes + bitmapBytes > 64 * 1024)
                {
                    break;
                }
                glyphs.Add(glyph);
                bytes += bitmapBytes;
            }
            return glyphs.Count == 0 ? null : new JsonObject
            {
                ["v"] = 1,
                ["bundle"] = capability.Bundle,
                ["size"] = capability.Size,
                ["bpp"] = capability.Bpp,
                ["glyphs"] = glyphs
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or OverflowException or ArgumentException or FormatException)
        {
            this.WarnUnavailable(capability);
            return null;
        }
    }

    private CbinFont? FindFont(TextFontCapability capability, int codepoint)
    {
        string profile = $"{capability.Size}_{capability.Bpp}";
        foreach (JsonElement shard in this._manifest.GetProperty("shards").EnumerateArray())
        {
            if (!shard.GetProperty("ranges").EnumerateArray().Any(range => codepoint >= range[0].GetInt32() && codepoint <= range[1].GetInt32()))
            {
                continue;
            }
            string path = Path.GetFullPath(Path.Combine(this._root, shard.GetProperty("profiles").GetProperty(profile).GetString()!));
            if (!path.StartsWith(this._root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidDataException(Lang.GlyphResource_FindFont_ShardPathOutOfRoot);
            }
            return this._fonts.GetOrAdd(path, file => new Lazy<CbinFont>(() => new CbinFont(File.ReadAllBytes(file)), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        return null;
    }

    private void WarnUnavailable(TextFontCapability capability)
    {
        if (this._warnedCapabilities.TryAdd(capability, 0))
        {
            this._logger.LogWarning(
                Lang.GlyphResource_ProfileUnavailable,
                capability.Bundle,
                capability.Charset,
                capability.Size,
                capability.Bpp);
        }
    }

    public void Dispose()
    {
        this._manifest = default;
        this._fonts.Clear();
        this._charsets.Clear();
        this._warnedCapabilities.Clear();
    }
}
