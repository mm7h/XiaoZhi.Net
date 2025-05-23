using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace XiaoZhi.Net.Server.Plugins
{
    /// <summary>
    /// Provides an <see cref="KernelPlugin"/> implementation around a collection of functions.
    /// </summary>
    internal class MutableKernelPlugin : KernelPlugin
    {
        private readonly Dictionary<string, KernelFunction> _functions;

        public MutableKernelPlugin(string name, string? description = null, IEnumerable<KernelFunction>? functions = null) : base(name, description)
        {
            this._functions = new Dictionary<string, KernelFunction>(StringComparer.OrdinalIgnoreCase);
            if (functions != null)
            {
                foreach (KernelFunction f in functions)
                {
                    if (f == null)
                    { 
                        throw new ArgumentNullException(nameof(f));
                    }

                    var cloned = f.Clone(name);
                    this._functions.Add(cloned.Name, cloned);
                }
            }
        }

        public override int FunctionCount => this._functions.Count;

        public override bool TryGetFunction(string name, [NotNullWhen(true)] out KernelFunction? function) =>
            this._functions.TryGetValue(name, out function);

        public void AddFunction(KernelFunction function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            var cloned = function.Clone(this.Name);
            this._functions.Add(cloned.Name, cloned);
        }

        public override IEnumerator<KernelFunction> GetEnumerator() => this._functions.Values.GetEnumerator();
    }
}
