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

    public static ConfigurationRoot LoadConfiguration(string[] args, string environmentName)
    {
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--config" || string.IsNullOrWhiteSpace(args[1])))
        {
            throw new ArgumentException("no configuration file path here.");
        }

#if DEBUG
        string configPath = Path.Combine(Environment.CurrentDirectory, "Configs", "config.json");
#else
        string configPath = Path.GetFullPath(args[1]);
#endif


        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("未找到主服务配置文件。", configPath);
        }

        string configDirectory = Path.GetDirectoryName(configPath)!;
        var builder = new ConfigurationBuilder()
            .SetBasePath(configDirectory)
            .AddJsonFile(Path.GetFileName(configPath), optional: false, reloadOnChange: false);
        StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // 配置分片与环境覆盖始终从指定配置文件所在目录读取。
        foreach (string path in Directory.EnumerateFiles(configDirectory, "config_*.json")
            .Where(path => !string.Equals(path, configPath, pathComparison))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AddJsonFile(Path.GetFileName(path), optional: false, reloadOnChange: false);
        }
        return (ConfigurationRoot)builder.AddJsonFile($"config.{environmentName}.json", optional: true, reloadOnChange: false).Build();
    }

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
