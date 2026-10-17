using System.Text.Json;
using System.Text.Json.Serialization;

namespace XiaoZhi.Net.Sample.Server.Configs
{
    internal class LenientStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Number)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return doc.RootElement.ToString();
            }
            if (reader.TokenType is JsonTokenType.True)
            {
                return "true";
            }
            if (reader.TokenType is JsonTokenType.False)
            {
                return "false";
            }
            if (reader.TokenType is JsonTokenType.StartObject || reader.TokenType is JsonTokenType.StartArray)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return doc.RootElement.GetRawText();
            }
            return reader.GetString()!;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
