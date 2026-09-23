using System.Text.Json;
using System.Text.Json.Nodes;

namespace XiaoZhi.Net.Sample.OTA.Server.Helpers
{
    public class ConfigHelper
    {
        private static readonly JsonNodeOptions s_nodeOptions = new JsonNodeOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonDocumentOptions s_documentOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static string Merge(string configDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);

            string baseConfigPath = Path.Combine(configDirectory, "config.json");
            JsonObject mergedConfig = ParseConfig(baseConfigPath);
            IEnumerable<string> fragmentPaths = Directory
                .EnumerateFiles(configDirectory, "config_*.json")
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

            foreach (string fragmentPath in fragmentPaths)
            {
                MergeObject(mergedConfig, ParseConfig(fragmentPath));
            }

            return mergedConfig.ToJsonString();
        }

        private static JsonObject ParseConfig(string configPath)
        {
            try
            {
                string configJson = File.ReadAllText(configPath);
                JsonNode? configNode = JsonNode.Parse(configJson, s_nodeOptions, s_documentOptions);
                return configNode as JsonObject
                    ?? throw new JsonException($"配置文件的根节点必须是 JSON 对象：{configPath}");
            }
            catch (JsonException ex)
            {
                throw new JsonException($"配置文件格式错误：{configPath}", ex);
            }
        }

        private static void MergeObject(JsonObject target, JsonObject source)
        {
            foreach ((string propertyName, JsonNode? sourceValue) in source)
            {
                if (target[propertyName] is JsonObject targetObject
                    && sourceValue is JsonObject sourceObject)
                {
                    MergeObject(targetObject, sourceObject);
                    continue;
                }

                target[propertyName] = sourceValue?.DeepClone();
            }
        }
    }
}
