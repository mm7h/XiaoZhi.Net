using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.ASR.Contexts;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal abstract class BaseSherpaAsr<TLogger> : BaseProvider<TLogger, ModelSetting>, IAsr
    {
        private readonly ConcurrentDictionary<string, IAsrEventCallback> _asrSessions;

        protected BaseSherpaAsr(ILogger<TLogger> logger) : base(logger)
        {
            this._asrSessions = new ConcurrentDictionary<string, IAsrEventCallback>();
        }

        public override string ProviderType => "asr";
        public abstract bool IsStreaming { get; }

        public abstract Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token);

        public abstract Task ConvertSpeechTextStreamingAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, StreamingAsrOperation operation, CancellationToken token);

        public virtual void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback)
        {
            this._asrSessions.AddOrUpdate(GetSessionKey(deviceId, sessionId), callback, (_, _) => callback);
            this.Logger.LogDebug(Lang.BaseSherpaAsr_RegisterDevice_Registered, deviceId, sessionId);
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            if (this._asrSessions.TryRemove(GetSessionKey(deviceId, sessionId), out _))
            {
                this.Logger.LogDebug(Lang.BaseSherpaAsr_UnregisterDevice_Unregistered, deviceId, sessionId);
            }
        }

        public override bool CheckDeviceRegistered(string deviceId, string sessionId)
        {
            return this._asrSessions.ContainsKey(GetSessionKey(deviceId, sessionId));
        }

        protected bool TryGetCallback(string deviceId, string sessionId, out IAsrEventCallback? callback)
        {
            return this._asrSessions.TryGetValue(GetSessionKey(deviceId, sessionId), out callback);
        }

        protected void LogBuilt()
        {
            string message = this.IsStreaming ? Lang.BaseSherpaAsr_Build_StreamingBuilt : Lang.BaseSherpaAsr_Build_OfflineBuilt;
            this.Logger.LogInformation(message, this.ModelName);
        }

        protected void LogBuildError(System.Exception ex)
        {
            string message = this.IsStreaming ? Lang.BaseSherpaAsr_Build_StreamingFailed : Lang.BaseSherpaAsr_Build_OfflineFailed;
            this.Logger.LogError(ex, message, this.ModelName);
        }

        protected void ClearRegisteredDevices()
        {
            this._asrSessions.Clear();
        }
    }
}
