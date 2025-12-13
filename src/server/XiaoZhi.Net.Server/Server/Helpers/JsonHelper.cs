using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace XiaoZhi.Net.Server.Helpers
{
    internal static class JsonHelper
    {
        public static readonly JsonSerializerOptions OPTIONS = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = new JsonSnakeCaseNamingPolicy(),
            DictionaryKeyPolicy = new JsonSnakeCaseNamingPolicy(),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string ToJson(this object obj) => JsonSerializer.Serialize(obj, JsonHelper.OPTIONS);
        public static JsonNode? ToNode(this object obj) => obj is null ? null : JsonSerializer.SerializeToNode(obj, JsonHelper.OPTIONS);
        public static string Serialize(object obj) => JsonSerializer.Serialize(obj, JsonHelper.OPTIONS);
        public static string Serialize(JsonObject obj) => obj.ToJsonString(JsonHelper.OPTIONS);
        public static byte[] SerializeToUtf8Bytes(object obj) => JsonSerializer.SerializeToUtf8Bytes(obj, JsonHelper.OPTIONS);
        public static T? Deserialize<T>(string json) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonHelper.OPTIONS);
            }
            catch
            {
                return null;
            }
        }
    }

    file class JsonSnakeCaseNamingPolicy : JsonNamingPolicy
    {
        private readonly string _separator = "_";

        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name)) return string.Empty;


            // 尝试获取属性的 JsonPropertyName 特性
            var propertyInfo = GetPropertyInfo(name);
            if (propertyInfo != null)
            {
                var jsonPropertyAttribute = propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
                if (jsonPropertyAttribute != null)
                {
                    return jsonPropertyAttribute.Name;
                }
            }

            ReadOnlySpan<char> spanName = name.Trim();
            var stringBuilder = new StringBuilder();
            var addCharacter = true;

            var isPreviousSpace = false;
            var isPreviousSeparator = false;
            var isCurrentSpace = false;
            var isNextLower = false;
            var isNextUpper = false;
            var isNextSpace = false;

            for (int position = 0; position < spanName.Length; position++)
            {
                if (position != 0)
                {
                    isCurrentSpace = spanName[position] == 32;
                    isPreviousSpace = spanName[position - 1] == 32;
                    isPreviousSeparator = spanName[position - 1] == 95;

                    if (position + 1 != spanName.Length)
                    {
                        isNextLower = spanName[position + 1] > 96 && spanName[position + 1] < 123;
                        isNextUpper = spanName[position + 1] > 64 && spanName[position + 1] < 91;
                        isNextSpace = spanName[position + 1] == 32;
                    }

                    if ((isCurrentSpace) &&
                        ((isPreviousSpace) ||
                        (isPreviousSeparator) ||
                        (isNextUpper) ||
                        (isNextSpace)))
                        addCharacter = false;
                    else
                    {
                        var isCurrentUpper = spanName[position] > 64 && spanName[position] < 91;
                        var isPreviousLower = spanName[position - 1] > 96 && spanName[position - 1] < 123;
                        var isPreviousNumber = spanName[position - 1] > 47 && spanName[position - 1] < 58;

                        if ((isCurrentUpper) &&
                        ((isPreviousLower) ||
                        (isPreviousNumber) ||
                        (isNextLower) ||
                        (isNextSpace) ||
                        (isNextLower && !isPreviousSpace)))
                            stringBuilder.Append(_separator);
                        else
                        {
                            if ((isCurrentSpace &&
                                !isPreviousSpace &&
                                !isNextSpace))
                            {
                                stringBuilder.Append(_separator);
                                addCharacter = false;
                            }
                        }
                    }
                }

                if (addCharacter)
                    stringBuilder.Append(spanName[position]);
                else
                    addCharacter = true;
            }

            var result = stringBuilder.ToString().ToLower();
            return result;
        }

        private PropertyInfo? GetPropertyInfo(string propertyName)
        {
            // 遍历当前加载的所有程序集
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        var property = type.GetProperty(propertyName, 
                            BindingFlags.Public | 
                            BindingFlags.NonPublic | 
                            BindingFlags.Instance);
                        
                        if (property != null)
                        {
                            return property;
                        }
                    }
                }
                catch
                {
                    // 忽略程序集加载错误
                    continue;
                }
            }
            return null;
        }
    }

    file class DynamicJsonConverter : JsonConverter<dynamic>
    {
        public override dynamic Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {

            if (reader.TokenType == JsonTokenType.True)
            {
                return true;
            }

            if (reader.TokenType == JsonTokenType.False)
            {
                return false;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt64(out long l))
                {
                    return l;
                }

                return reader.GetDouble();
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                if (reader.TryGetDateTime(out DateTime datetime))
                {
                    return datetime;
                }

                return reader.GetString();
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using JsonDocument documentV = JsonDocument.ParseValue(ref reader);
                return ReadObject(documentV.RootElement);
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                using JsonDocument documentV = JsonDocument.ParseValue(ref reader);
                return ReadList(documentV.RootElement);
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }

        private object ReadObject(JsonElement jsonElement)
        {
            IDictionary<string, object> expandoObject = new ExpandoObject();
            foreach (var obj in jsonElement.EnumerateObject())
            {
                var k = obj.Name;
                var value = ReadValue(obj.Value);
                expandoObject[k] = value;
            }
            return expandoObject;
        }

        private object ReadValue(JsonElement jsonElement)
        {
            object result = null;
            switch (jsonElement.ValueKind)
            {
                case JsonValueKind.Object:
                    result = ReadObject(jsonElement);
                    break;
                case JsonValueKind.Array:
                    result = ReadList(jsonElement);
                    break;
                case JsonValueKind.String:
                    result = jsonElement.GetString();
                    break;
                case JsonValueKind.Number:
                    if (jsonElement.TryGetDecimal(out decimal d))
                    {
                        result = d;
                    }
                    else if (jsonElement.TryGetInt64(out long l))
                    {
                        result = l;
                    }
                    else
                    {
                        result = 0;
                    }
                    break;
                case JsonValueKind.True:
                    result = true;
                    break;
                case JsonValueKind.False:
                    result = false;
                    break;
                case JsonValueKind.Undefined:
                case JsonValueKind.Null:
                    result = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return result;
        }

        private object ReadList(JsonElement jsonElement)
        {
            IList<object> list = new List<object>();
            foreach (var item in jsonElement.EnumerateArray())
            {
                list.Add(ReadValue(item));
            }
            return list.Count == 0 ? null : list;
        }

        public override void Write(Utf8JsonWriter writer, dynamic value, JsonSerializerOptions options)
        {
            // https://docs.microsoft.com/en-us/dotnet/api/system.typecode
            switch (Type.GetTypeCode((Type)value.GetType()))
            {
                case TypeCode.Boolean:
                    writer.WriteBooleanValue(Convert.ToBoolean(value));
                    break;
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    writer.WriteNumberValue(Convert.ToInt64(value));
                    break;
                case TypeCode.Decimal:
                    writer.WriteNumberValue(Convert.ToDecimal(value));
                    break;
                case TypeCode.Char:
                case TypeCode.Empty:
                case TypeCode.String:
                    writer.WriteStringValue(Convert.ToString(value));
                    break;
                case TypeCode.DateTime:
                    writer.WriteStringValue(Convert.ToDateTime(value).ToString("yyyy-MM-dd HH:mm:ss"));
                    break;
                case TypeCode.DBNull:
                    writer.WriteNullValue();
                    break;
                default:
                    writer.WriteRawValue(JsonSerializer.Serialize(value, new JsonSerializerOptions() { WriteIndented = true }));
                    break;
            }
        }
    }
}
