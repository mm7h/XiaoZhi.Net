using System.Collections.Generic;
using System.Text.Json;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class ConfigHelper
    {
        public static string? GetConfigValueOrDefault(this IDictionary<string, string> config, string key)
        {
            if (config == null || string.IsNullOrWhiteSpace(key) || !config.TryGetValue(key, out string? value))
            {
                return default;
            }
            return value;
        }

        public static TValue? GetConfigValueOrDefault<TValue>(this IDictionary<string, string> config, string key)
        {
            if (config == null || string.IsNullOrWhiteSpace(key) || !config.TryGetValue(key, out string? value))
            {
                return default;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return default;
            }

            if (typeof(TValue) == typeof(string))
            {
                return (TValue)(object)value;
            }

            return JsonSerializer.Deserialize<TValue>(value, JsonHelper.OPTIONS);
        }

        public static TValue GetConfigValueOrDefault<TValue>(this IDictionary<string, string> config, string key, TValue defaultValue)
        {
            if (config == null || string.IsNullOrWhiteSpace(key) || !config.TryGetValue(key, out string? value))
            {
                return defaultValue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (typeof(TValue) == typeof(string))
            {
                return (TValue)(object)value;
            }

            return JsonSerializer.Deserialize<TValue>(value, JsonHelper.OPTIONS) ?? defaultValue;
        }

        public static void SetConfigValue<TValue>(this IDictionary<string, string> config, string key, TValue? configValue)
        { 
            if (config == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (configValue == null)
            {
                if (config.ContainsKey(key))
                {
                    config.Remove(key);
                }
                return;
            }

            string value;
            if (configValue is string s)
            {
                value = s;
            }
            else
            {
                value = JsonSerializer.Serialize(configValue, JsonHelper.OPTIONS);
            }

            config[key] = value;
        }
    }
}
