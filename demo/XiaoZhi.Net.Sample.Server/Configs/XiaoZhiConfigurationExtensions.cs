using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using XiaoZhi.Net.Server;

namespace XiaoZhi.Net.Sample.Server.Configs;

internal static class XiaoZhiConfigurationExtensions
{
    private static readonly JsonSerializerOptions s_stringOptions = new()
    {
        Converters = { new LenientStringConverter() }
    };

    public static XiaoZhiConfig GetXiaoZhiConfig(this IConfiguration configuration)
    {
        XiaoZhiConfig config = configuration.Get<XiaoZhiConfig>()
            ?? throw new InvalidOperationException("Cannot read the config settings.");

        // Binder handles typed properties. Only the legacy string dictionaries need adaptation.
        config.ConfiguredSettings = configuration.GetSection(nameof(XiaoZhiConfig.ConfiguredSettings))
            .GetChildren().ToDictionary(category => category.Key, category => category.GetChildren()
                .ToDictionary(model => model.Key, ReadParameters));

        if (config.McpSettings is not null)
        {
            foreach (var (name, model) in config.McpSettings)
            {
                model.Config = ReadParameters(configuration.GetSection($"McpSettings:{name}:Config"));
            }
        }

        return config;
    }

    private static Dictionary<string, string> ReadParameters(IConfigurationSection section)
    {
        return section.GetChildren().ToDictionary(parameter => parameter.Key, parameter =>
        {
            if (parameter.Value is { } value)
            {
                // JSON providers expose booleans as True/False; ConfigHelper expects valid JSON.
                return bool.TryParse(value, out bool boolean) ? (boolean ? "true" : "false") : value;
            }

            string json = ReadStructuredValue(parameter)?.ToJsonString() ?? "null";
            return JsonSerializer.Deserialize<string>(json, s_stringOptions)!;
        });
    }

    // Converts an already merged parameter into its legacy JSON-string representation.
    // IConfiguration has no source JSON types: structured leaves use JSON literal conventions.
    // Supply the entire parameter as a JSON string when exact string/number types must be kept.
    private static JsonNode? ReadStructuredValue(IConfigurationSection section)
    {
        if (section.Value is { } value)
        {
            if (bool.TryParse(value, out bool boolean))
            {
                return JsonValue.Create(boolean);
            }

            if (value.Length > 0 && (char.IsAsciiDigit(value[0]) || value[0] == '-'))
            {
                try
                {
                    return JsonNode.Parse(value);
                }
                catch (JsonException)
                {
                    // Paths and text beginning with a digit remain strings.
                }
            }

            return JsonValue.Create(value);
        }

        IConfigurationSection[] children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return null;
        }

        if (children.All(child => int.TryParse(child.Key, NumberStyles.None,
            CultureInfo.InvariantCulture, out _)))
        {
            return new JsonArray(children.Select(ReadStructuredValue).ToArray());
        }

        return new JsonObject(children.Select(child => new KeyValuePair<string, JsonNode?>(child.Key, ReadStructuredValue(child))));
    }
}
