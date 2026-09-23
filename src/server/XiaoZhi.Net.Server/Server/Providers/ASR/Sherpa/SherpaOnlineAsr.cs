using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.ASR.Contexts;

namespace XiaoZhi.Net.Server.Providers.ASR.Sherpa
{
    internal abstract class SherpaOnlineAsr<TLogger> : BaseSherpaAsr<TLogger>
    {
        private readonly ConcurrentDictionary<string, OnlineAsrSessionState> _sessions;
        private readonly object _recognizerGate;
        private OnlineRecognizer? _recognizer;

        protected SherpaOnlineAsr(ILogger<TLogger> logger) : base(logger)
        {
            this._sessions = new ConcurrentDictionary<string, OnlineAsrSessionState>();
            this._recognizerGate = new object();
        }

        public override bool IsStreaming => true;

        protected void Build(OnlineRecognizerConfig config, ModelSetting modelSetting)
        {
            config.ModelConfig.Tokens = Path.Combine(this.ModelFileFoler, "tokens.txt");
            config.ModelConfig.NumThreads = modelSetting.Config.GetConfigValueOrDefault("NumThreads", 1);
            config.ModelConfig.Provider = modelSetting.Config.GetConfigValueOrDefault("Provider", "cpu");
            config.ModelConfig.Debug = modelSetting.Config.GetConfigValueOrDefault("Debug", 0);
            config.DecodingMethod = modelSetting.Config.GetConfigValueOrDefault("DecodingMethod", "greedy_search");
            config.MaxActivePaths = modelSetting.Config.GetConfigValueOrDefault("MaxActivePaths", 4);
            config.EnableEndpoint = modelSetting.Config.GetConfigValueOrDefault("EnableEndpoint", 0);
            config.Rule1MinTrailingSilence = modelSetting.Config.GetConfigValueOrDefault("Rule1MinTrailingSilence", 1.2F);
            config.Rule2MinTrailingSilence = modelSetting.Config.GetConfigValueOrDefault("Rule2MinTrailingSilence", 2.4F);
            config.Rule3MinUtteranceLength = modelSetting.Config.GetConfigValueOrDefault("Rule3MinUtteranceLength", 20.0F);

            string hotwordsPath = Path.Combine(this.ModelFileFoler, "hotwords.txt");
            if (File.Exists(hotwordsPath))
            {
                config.HotwordsFile = hotwordsPath;
                config.HotwordsScore = modelSetting.Config.GetConfigValueOrDefault("HotwordsScore", 1.5F);
            }

            lock (this._recognizerGate)
            {
                this.DisposeRecognizer();
                this._recognizer = new OnlineRecognizer(config);
            }
        }

        public override void RegisterDevice(string deviceId, string sessionId, IAsrEventCallback callback)
        {
            string sessionKey = GetSessionKey(deviceId, sessionId);
            lock (this._recognizerGate)
            {
                if (this._sessions.TryRemove(sessionKey, out OnlineAsrSessionState? previous))
                {
                    previous.Stream?.Dispose();
                }
                this._sessions[sessionKey] = new OnlineAsrSessionState();
                base.RegisterDevice(deviceId, sessionId, callback);
            }
        }

        public override void UnregisterDevice(string deviceId, string sessionId)
        {
            lock (this._recognizerGate)
            {
                if (this._sessions.TryRemove(GetSessionKey(deviceId, sessionId), out OnlineAsrSessionState? state))
                {
                    state.Stream?.Dispose();
                }
                base.UnregisterDevice(deviceId, sessionId);
            }
        }

        public override Task ConvertSpeechTextAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, CancellationToken token)
        {
            throw new NotSupportedException($"{this.ModelName} 仅支持流式 ASR。");
        }

        public override Task ConvertSpeechTextStreamingAsync(Workflow<float[]> workflow, int sampleRate, int frameSize, StreamingAsrOperation operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new SessionNotInitializedException();
            }

            IAsrEventCallback? callback = null;
            string? text = null;
            lock (this._recognizerGate)
            {
                if (this._recognizer is null || !this._sessions.TryGetValue(GetSessionKey(workflow.DeviceId, workflow.SessionId), out OnlineAsrSessionState? state))
                {
                    throw new SessionNotInitializedException();
                }

                switch (operation)
                {
                    case StreamingAsrOperation.Start:
                        state.Stream?.Dispose();
                        state.Stream = this._recognizer.CreateStream();
                        state.TurnId = workflow.TurnId;
                        this.AcceptAndDecode(this._recognizer, state.Stream, sampleRate, workflow.Data);
                        break;
                    case StreamingAsrOperation.Audio:
                        if (state.Stream is not null && state.TurnId == workflow.TurnId)
                        {
                            this.AcceptAndDecode(this._recognizer, state.Stream, sampleRate, workflow.Data);
                        }
                        break;
                    case StreamingAsrOperation.Finish:
                        if (state.Stream is not null && state.TurnId == workflow.TurnId)
                        {
                            state.Stream.InputFinished();
                            this.DecodeAvailable(this._recognizer, state.Stream);
                            text = this._recognizer.GetResult(state.Stream).Text;
                            this.TryGetCallback(workflow.DeviceId, workflow.SessionId, out callback);
                            state.Stream.Dispose();
                            state.Stream = null;
                        }
                        break;
                    case StreamingAsrOperation.Abort:
                        state.Stream?.Dispose();
                        state.Stream = null;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operation));
                }
            }

            if (callback is not null && text is not null && !token.IsCancellationRequested)
            {
                callback.OnSpeechTextConverted(workflow.TurnId, true, text);
            }
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            lock (this._recognizerGate)
            {
                this.DisposeRecognizer();
            }
        }

        private void AcceptAndDecode(OnlineRecognizer recognizer, OnlineStream stream, int sampleRate, float[] audioData)
        {
            if (audioData.Length > 0)
            {
                stream.AcceptWaveform(sampleRate, audioData);
            }
            this.DecodeAvailable(recognizer, stream);
        }

        private void DecodeAvailable(OnlineRecognizer recognizer, OnlineStream stream)
        {
            while (recognizer.IsReady(stream))
            {
                recognizer.Decode(stream);
            }
        }

        private void DisposeRecognizer()
        {
            foreach (OnlineAsrSessionState state in this._sessions.Values)
            {
                state.Stream?.Dispose();
            }
            this._sessions.Clear();
            this.ClearRegisteredDevices();
            this._recognizer?.Dispose();
            this._recognizer = null;
        }

        private sealed class OnlineAsrSessionState
        {
            public OnlineStream? Stream { get; set; }
            public long TurnId { get; set; } = -1;
        }
    }
}
