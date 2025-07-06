using Serilog;
using System;
using System.Diagnostics;

namespace XiaoZhi.Net.Server.Helpers
{
    internal sealed class CodeTimer : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _template;
        private readonly ILogger _logger;

        public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

        private CodeTimer(string template, ILogger logger)
        {
            this._stopwatch = Stopwatch.StartNew();
            this._template = template;
            this._logger = logger;
        }

        public string? Message { get; }

        public static CodeTimer Create(string template, ILogger logger)
        {
            return new CodeTimer(template, logger);
        }

        public void Dispose()
        {
            if (!string.IsNullOrEmpty(this.Message))
                this._logger.Information(this.Message);
            else
                this._logger.Information("The job finished and took {elapsed:F2} ms.", this.ElapsedMilliseconds);
            this._stopwatch.Stop();
        }
    }
}
