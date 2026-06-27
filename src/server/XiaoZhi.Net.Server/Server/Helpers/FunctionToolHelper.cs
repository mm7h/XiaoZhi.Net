using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Providers.LLM.Contexts;

namespace XiaoZhi.Net.Server.Helpers
{
    /// <summary>
    /// 函数工具帮助方法。
    /// </summary>
    internal static class FunctionToolHelper
    {
        public static FunctionMetadata ToFunctionMetadata(this AIFunction function)
        {
            string? inputJsonSchema = function is AIFunctionDeclaration declaration && declaration.JsonSchema.ValueKind != JsonValueKind.Undefined
                ? declaration.JsonSchema.GetRawText()
                : null;
            return function.ToFunctionMetadata(inputJsonSchema, function.Description);
        }

        public static FunctionMetadata ToFunctionMetadata(this AIFunction function, string? inputJsonSchema, string? description)
        {
            FunctionMetadata metadata = new FunctionMetadata
            {
                Name = function.Name,
                Description = description,
                Parameters = new List<FunctionParameter>(),
                InputJsonSchema = inputJsonSchema
            };

            if (string.IsNullOrWhiteSpace(inputJsonSchema))
            {
                return metadata;
            }

            using JsonDocument schemaDocument = JsonDocument.Parse(inputJsonSchema);
            JsonElement schema = schemaDocument.RootElement;
            List<string> requiredParameters = new List<string>();
            if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
            {
                requiredParameters.AddRange(required.EnumerateArray().Select(item => item.GetString() ?? string.Empty));
            }

            if (schema.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    string parameterDescription = property.Value.TryGetProperty("description", out JsonElement propDescription)
                        ? ReadSchemaText(propDescription)
                        : string.Empty;
                    string type = property.Value.TryGetProperty("type", out JsonElement propType)
                        ? ReadSchemaType(propType)
                        : string.Empty;

                    metadata.Parameters.Add(new FunctionParameter
                    {
                        Name = property.Name,
                        Type = type,
                        Description = parameterDescription,
                        Required = requiredParameters.Contains(property.Name)
                    });
                }
            }

            return metadata;
        }

        public static void FireHooksSafely(IEnumerable<Task> tasks, string hookName)
        {
            Task combined = Task.WhenAll(tasks);
            _ = combined.ContinueWith(static (t, state) =>
            {
                if (t.Exception is not null)
                {
                    foreach (Exception ex in t.Exception.InnerExceptions)
                    {
                        Serilog.Log.Warning(ex, "工具钩子 {HookName} 执行时发生异常", (string?)state);
                    }
                }
            }, hookName, TaskContinuationOptions.OnlyOnFaulted);
        }

        private static string ReadSchemaText(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(" / ", element.EnumerateArray().Select(ReadSchemaText).Where(static item => !string.IsNullOrWhiteSpace(item))),
                JsonValueKind.Object => element.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Number => element.GetRawText(),
                _ => string.Empty,
            };
        }

        private static string ReadSchemaType(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                List<string> typeNames = element.EnumerateArray()
                    .Select(ReadSchemaText)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return string.Join(" | ", typeNames);
            }

            return ReadSchemaText(element);
        }
    }
}