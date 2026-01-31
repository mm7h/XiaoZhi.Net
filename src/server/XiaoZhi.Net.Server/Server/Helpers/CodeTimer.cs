using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Helpers
{
    internal class CodeTimer : IDisposable
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

        public static CodeTimer Create(string template, ILogger logger)
        {
            return new CodeTimer(template, logger);
        }

        public void Dispose()
        {
            if (!string.IsNullOrEmpty(this._template))
                this._logger.LogDebug(this._template, this.ElapsedMilliseconds);
            else
                this._logger.LogDebug(Lang.CodeTimer_Dispose_JobFinished, this.ElapsedMilliseconds);
            this._stopwatch.Stop();
        }
    }
}
