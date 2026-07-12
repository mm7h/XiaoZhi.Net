using Microsoft.Extensions.AI;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal sealed class ProxyAIFunction : AIFunction
    {
        private readonly Func<AIFunctionArguments, CancellationToken, Task<object?>> _invoker;

        public ProxyAIFunction(
            string name,
            string description,
            JsonElement jsonSchema,
            Func<AIFunctionArguments, CancellationToken, Task<object?>> invoker)
        {
            this.Name = name;
            this.Description = description;
            this.JsonSchema = jsonSchema;
            this._invoker = invoker;
        }

        public override string Name { get; }

        public override string Description { get; }

        public override JsonElement JsonSchema { get; }

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            return await this._invoker(arguments ?? new AIFunctionArguments(), cancellationToken);
        }
    }
}
