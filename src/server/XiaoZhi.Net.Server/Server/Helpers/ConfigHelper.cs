using System.Collections.Generic;
using System.Text.Json;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class ConfigHelper
    {
        public static string? GetConfigValueOrDefault(this IDictionary<string, string> config, string key)
        {
            if (config == null || string.IsNullOrEmpty(key) || !config.TryGetValue(key, out string? value))
            {
                return default;
            }
            return value;
        }

        public static TValue? GetConfigValueOrDefault<TValue>(this IDictionary<string, string> config, string key)
        {
            if (config == null || string.IsNullOrEmpty(key) || !config.TryGetValue(key, out string? value))
            {
                return default;
            }

            if (string.IsNullOrEmpty(value))
            {
                return default;
            }
            return JsonSerializer.Deserialize<TValue>(value, JsonHelper.OPTIONS);
        }

        public static TValue GetConfigValueOrDefault<TValue>(this IDictionary<string, string> config, string key, TValue defaultValue)
        {
            if (config == null || string.IsNullOrEmpty(key) || !config.TryGetValue(key, out string? value))
            {
                return defaultValue;
            }

            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            return JsonSerializer.Deserialize<TValue>(value, JsonHelper.OPTIONS) ?? defaultValue;
        }
    }
}
