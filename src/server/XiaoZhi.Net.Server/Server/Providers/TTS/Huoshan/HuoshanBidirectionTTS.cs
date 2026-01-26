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
    internal class HuoshanBidirectionTTS : HuoshanStreamTTS<HuoshanBidirectionTTS>, ITts
    {
        private const string SERVICE_END_POINT = "wss://openspeech.bytedance.com/api/v3/tts/bidirection";
        private const string TTS_NAMESPACE = "BidirectionalTTS";

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

            OutSegment seg = workflow.Data;

            if (string.IsNullOrEmpty(seg.ParagraphId) || string.IsNullOrEmpty(seg.SentenceId))
            {
                this.Logger.LogWarning("Failed to process segment due to missing paragraph id or sentence id.");
                return;
            }

            this.ProcessingSegments.TryAdd(seg.SentenceId, workflow.Data);

            this.StreamingActive = true;
            Dictionary<string, object> startReq = new Dictionary<string, object>
            {
                { "User", new { Uid = workflow.DeviceId } },
                { "Event", (int)EventType.StartSession },
                { "Namespace", TTS_NAMESPACE },
                { "ReqParams",
                    new {
                        Speaker = this.SpeakerId,
                        AudioParams = new {
                            Format = this.AudioEncoding,
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
                        SectionId = seg.ParagraphId
                    })
                }
            };
            await this.StartSessionAsync(seg.SentenceId, JsonHelper.SerializeToUtf8Bytes(startReq), token);

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
                            Format = this.AudioEncoding,
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

            await this.TaskRequestAsync(seg.SentenceId, JsonHelper.SerializeToUtf8Bytes(ttsReq));
            token.ThrowIfCancellationRequested();

            try
            {
                await this.FinishSessionAsync(seg.SentenceId, token);

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
                this.ProcessingSegments.Remove(seg.SentenceId);
                this.StreamingActive = false;
            }
        }

        public override void Dispose()
        {
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
