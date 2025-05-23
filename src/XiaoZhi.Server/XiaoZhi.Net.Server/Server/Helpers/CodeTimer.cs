using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace XiaoZhi.Net.Server.Helpers
{
    internal sealed class CodeTimer : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private bool _showMesssage = true;

        public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;
        private CodeTimer()
        {
            this._stopwatch = Stopwatch.StartNew();
        }
        private CodeTimer(bool showMessage) : this()
        {
            this._showMesssage = showMessage;
        }

        public string? Message { get; set; }

        public static CodeTimer Create()
        {
            return new CodeTimer();
        }
        public static CodeTimer Create(bool showMessage)
        {
            return new CodeTimer(showMessage);
        }

        public void Dispose()
        {
            if (!this._showMesssage)
                return;
            if (!string.IsNullOrEmpty(this.Message))
                Log.Information(this.Message);
            else
                Log.Information($"The job finished and took {this.ElapsedMilliseconds:F2} ms.");
            this._stopwatch.Stop();
        }
    }
}
