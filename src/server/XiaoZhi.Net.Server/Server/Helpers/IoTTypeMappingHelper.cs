using System;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class IoTTypeMappingHelper
    {
        public static Type GetIoTType(string typeDescription)
        {
            return typeDescription.ToLower() switch
            {
                "number" => typeof(decimal),
                "boolean" => typeof(bool),
                _ => typeof(string),
            };
        }
        public static object GetDefaultValue(string typeDescription)
        {
            return typeDescription.ToLower() switch
            {
                "number" => 0m,
                "boolean" => false,
                _ => "",
            };
        }
        public static object? ConvertValue(object? value, Type? type)
        {
            if (type is null)
            {
                return value;
            }
            return type switch
            {
                Type t when t == typeof(decimal) => Convert.ToDecimal(value),
                Type t when t == typeof(bool) => Convert.ToBoolean(value),
                _ => value,
            };
        }
    }
}
