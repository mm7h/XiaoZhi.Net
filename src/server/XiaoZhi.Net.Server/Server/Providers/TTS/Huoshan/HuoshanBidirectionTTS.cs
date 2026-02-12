using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Common.Exceptions;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;

namespace XiaoZhi.Net.Server.Providers.TTS
{
    internal class HuoshanBidirectionTTS : HuoshanStreamTTS<HuoshanBidirectionTTS>, ITts
    {
        private const string SERVICE_END_POINT = "wss://openspeech.bytedance.com/api/v3/tts/bidirection";
        private const string TTS_NAMESPACE = "BidirectionalTTS";

        public HuoshanBidirectionTTS(IAudioEditor audioEditor, ILogger<HuoshanBidirectionTTS> logger) : base(audioEditor, logger)
        {
        }

        public override string ModelName => nameof(HuoshanBidirectionTTS);



        public async Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            if (!this.CheckDeviceRegistered(workflow.DeviceId, workflow.SessionId))
            {
                throw new SessionNotInitializedException();
            }
            if (this.WebSocketClient is null)
            {
                throw new InvalidOperationException(Lang.HuoshanBidirectionTTS_SynthesisAsync_ClientNotInit);
            }

            if (!this.WebSocketClient.IsConnected)
            {
                await this.ConnectAsync(SERVICE_END_POINT, token);
                await this.StartConnectionAsync(token);
            }

            OutSegment seg = workflow.Data;

            if (string.IsNullOrEmpty(seg.ParagraphId) || string.IsNullOrEmpty(seg.SentenceId))
            {
                this.Logger.LogWarning(Lang.HuoshanBidirectionTTS_SynthesisAsync_MissingIds);
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
            catch (OperationCanceledException)
            {
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Aborted);
                this.Logger.LogWarning(Lang.HuoshanBidirectionTTS_SynthesisAsync_Canceled);
                throw;
            }
            catch (Exception ex)
            {
                this.TTSEventCallback?.OnProcessed(seg.Content, seg.IsFirstSegment, seg.IsLastSegment, TtsGenerateResult.Failed);
                this.Logger.LogError(ex, Lang.HuoshanBidirectionTTS_SynthesisAsync_Failed);
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
                this.Logger.LogDebug(ex, Lang.HuoshanBidirectionTTS_Dispose_FinishError);
            }
            finally
            {
                this.FailAllWaits(new OperationCanceledException(Lang.HuoshanBidirectionTTS_Dispose_Disposed));
                this.ClearAllSessionAudioBuffers();
                this.TTSEventCallback?.OnProcessed(string.Empty, false, false, TtsGenerateResult.Aborted);
            }
        }
    }
}
