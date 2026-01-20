using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    internal class HuoshanUnidirectionalTTS : HuoshanTTS<HuoshanUnidirectionalTTS>, ITts
    {
        private const string SERVICE_END_POINT = "wss://openspeech.bytedance.com/api/v3/tts/unidirectional/stream";

        private string? _ttsSessionId = null;

        public HuoshanUnidirectionalTTS(ILogger<HuoshanUnidirectionalTTS> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(HuoshanUnidirectionalTTS);

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new InvalidOperationException("Device/session is not registered.");
            }
            if (this.WebSocketClient is null)
            {
                throw new InvalidOperationException("WebSocket client is not initialized.");
            }

            if (!this.WebSocketClient.IsConnected)
            {
                await this.ConnectAsync(SERVICE_END_POINT, token);
            }

            if (string.IsNullOrEmpty(this._ttsSessionId))
            {
                this._ttsSessionId = Guid.NewGuid().ToString();
                this.ProcessingSegments.TryAdd(this._ttsSessionId, workflow.Data);
            }

            this.StreamingActive = true;
            OutSegment seg = workflow.Data;

            this.TTSEventCallback?.OnBeforeProcessing(seg.Content, seg.IsFirstSegment, seg.IsLastSegment);

            var ttsReq = new Dictionary<string, object>
            {
               { "User", new { Uid = workflow.DeviceId } },
               { "ReqParams",
                    new {
                        Text = seg.Content,
                        Speaker = this.SpeakerId,
                        AudioParams = new {
                            Format = this.AudioEcoding,
                            SampleRate = this.GetTtsSampleRate(),
                            EnableTimestamp = false,
                            this.SpeechRate,
                            this.LoudnessRate,
                            Emotion = this.ConvertEmotion(seg.Emotion)
                        },
                        Additions=
                            JsonHelper.Serialize(new {
                                DisableMarkdownFilter = false,
                                CacheConfig = new
                                {
                                    TextType = 1,
                                    UseCache = true
                                },
                                SectionId = this._ttsSessionId
                            })
                        }
                    }
            };

            await this.TaskRequestAsync(ttsReq);
            token.ThrowIfCancellationRequested(); 
            
            bool finalize = false;
            try
            {
                var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.SessionFinished, token, null);
                await waitTask.ConfigureAwait(false);
                finalize = true;

                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Success);
            }
            catch (OperationCanceledException oex)
            {
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Aborted);
                this.Logger.LogWarning(oex, "TTS synthesis was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                this.Logger.LogError(ex, "TTS synthesis failed.");
                throw;
            }
            finally
            {
                if (!string.IsNullOrEmpty(this._ttsSessionId))
                {
                    this.CloseSessionFile(this._ttsSessionId, finalize);
                    this.ProcessingSegments.Remove(this._ttsSessionId);
                }
                this._ttsSessionId = null;
                this.StreamingActive = false;
            }
        }

        public override void Dispose()
        {
            this.StreamingActive = false;

            try
            {
                this.FinishConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                this.Logger.LogDebug(ex, "FinishConnection during dispose raised an exception.");
            }
            finally
            {
                this.CloseAllSessionFiles(finalize: false);
                this.WebSocketClient?.Dispose();
            }
        }
    }
}
