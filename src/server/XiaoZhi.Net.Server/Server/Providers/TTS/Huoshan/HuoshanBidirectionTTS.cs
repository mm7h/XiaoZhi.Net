using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal class HuoshanBidirectionTTS : HuoshanTTS<HuoshanBidirectionTTS>, ITts
    {
        private const string SERVICE_END_POINT = "wss://openspeech.bytedance.com/api/v3/tts/bidirection";
        private const string TTS_NAMESPACE = "BidirectionalTTS";

        private string? _ttsSessionId = null;


        public HuoshanBidirectionTTS(ILogger<HuoshanBidirectionTTS> logger) : base(logger)
        {
        }

        public override string ModelName => nameof(HuoshanBidirectionTTS);

        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered())
            {
                throw new SessionNotInitializedException();
            }
            if (this.WebSocketClient is null)
            {
                throw new InvalidOperationException("WebSocket client is not initialized.");
            }

            if (!this.WebSocketClient.IsConnected)
            {
                await this.ConnectAsync(SERVICE_END_POINT, token);
                await this.StartConnectionAsync(token);
            }

            if (string.IsNullOrEmpty(this._ttsSessionId))
            {
                this._ttsSessionId = Guid.NewGuid().ToString();
                this.ProcessingSegments.TryAdd(this._ttsSessionId, workflow.Data);
            }

            this.StreamingActive = true;

            OutSegment seg = workflow.Data;

            if (seg.IsFirstSegment)
            {
                Dictionary<string, object> startReq = new Dictionary<string, object>
                {
                    { "User", new { Uid = workflow.DeviceId } },
                    { "Event", (int)EventType.StartSession },
                    { "Namespace", TTS_NAMESPACE },
                    { "ReqParams",
                        new {
                            Speaker = this.SpeakerId,
                            AudioParams = new {
                                Format = this.AudioEcoding,
                                SampleRate = this.GetTtsSampleRate(),
                                EnableTimestamp = false,
                                this.SpeechRate,
                                this.LoudnessRate,
                            }
                        }
                    },
                    { "Additions",
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
                };
                await this.StartSessionAsync(this._ttsSessionId, JsonHelper.SerializeToUtf8Bytes(startReq), token);
            }
            token.ThrowIfCancellationRequested();

            Dictionary<string, object> ttsReq = new Dictionary<string, object>
            {
                { "User", new { Uid = workflow.DeviceId } },
                { "Event", (int)EventType.TaskRequest },
                { "Namespace", TTS_NAMESPACE },
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
                        }
                    }
                },
            };

            this.TTSEventCallback?.OnBeforeProcessing(seg.Content, seg.IsFirstSegment, seg.IsLastSegment);

            await this.TaskRequestAsync(this._ttsSessionId, JsonHelper.SerializeToUtf8Bytes(ttsReq));
            token.ThrowIfCancellationRequested();

            // Only finish session on last segment (last sentence in paragraph)
            if (seg.IsLastSegment)
            {
                bool finalize = false;
                try
                {
                    await this.FinishSessionAsync(this._ttsSessionId, token);
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
                }
            }

            // Cleanup per-sentence
            this.StreamingActive = false;
        }

        public override void Dispose()
        {
            this._ttsSessionId = null;
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
                this.FailAllWaits(new OperationCanceledException("TTS provider disposed"));
                this.CloseAllSessionFiles(finalize: false);
                this.TTSEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Aborted);
            }
        }
    }
}
