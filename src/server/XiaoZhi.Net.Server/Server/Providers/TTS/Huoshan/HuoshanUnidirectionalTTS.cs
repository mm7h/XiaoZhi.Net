using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Enums;
using XiaoZhi.Net.Server.Providers.TTS.Huoshan.Protocols.Models;

namespace XiaoZhi.Net.Server.Providers.TTS.Huoshan
{
    [Obsolete]
    internal class HuoshanUnidirectionalTTS : HuoshanStreamTTS<HuoshanUnidirectionalTTS>, ITts
    {
        private readonly ConcurrentQueue<OutSegment> segmentsCache;
        private const string SERVICE_END_POINT = "wss://openspeech.bytedance.com/api/v3/tts/unidirectional/stream";

        public HuoshanUnidirectionalTTS(IAudioEditor audioEditor, ILogger<HuoshanUnidirectionalTTS> logger) : base(audioEditor, logger)
        {
            this.segmentsCache = new ConcurrentQueue<OutSegment>();
        }

        public override string ModelName => nameof(HuoshanUnidirectionalTTS);

        public Task SynthesisAsync(Workflow<OutSegment> workflow, CancellationToken token)
        {
            this.Logger.LogWarning("{ModelName} is deprecated and will be removed in future versions. Please consider using the latest TTS models.", this.ModelName);
            return Task.CompletedTask;
            /*
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

            OutSegment seg = workflow.Data;

            if (string.IsNullOrEmpty(seg.ParagraphId) || string.IsNullOrEmpty(seg.SentenceId))
            {
                this.Logger.LogWarning("Failed to process segment due to missing paragraph id or sentence id.");
                return;
            }

            this.ProcessingSegments.TryAdd(seg.SentenceId, workflow.Data);

            this.StreamingActive = true;
            

            this.TTSEventCallback?.OnBeforeProcessing(seg.Content, seg.IsFirstSegment, seg.IsLastSegment);

            var ttsReq = new
            {
                User = new { Uid = workflow.DeviceId },
                ReqParams = new
                {
                    Text = seg.Content,
                    Speaker = this.SpeakerId,
                    AudioParams = new
                    {
                        Format = this.AudioEncoding,
                        SampleRate = this.GetTtsSampleRate(),
                        EnableTimestamp = false,
                        this.SpeechRate,
                        this.LoudnessRate,
                        Emotion = this.ConvertEmotion(seg.Emotion)
                    },
                    Additions =
                        JsonHelper.Serialize(new
                        {
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

            await this.TaskRequestAsync(ttsReq);
            token.ThrowIfCancellationRequested(); 
            
            try
            {
                var waitTask = this.WaitForEventAsync(MsgType.FullServerResponse, EventType.SessionFinished, token, null);
                await waitTask.ConfigureAwait(false);

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
            */
        }

        protected override (string, Emotion) GetSubtitle(Message message, bool isSentenceStart)
        {
            string sentence = JsonObject.Parse(message.Payload)?["text"]?.GetValue<string>() ?? string.Empty;
            if (isSentenceStart)
            {
                if (this.segmentsCache.TryPeek(out OutSegment? seg) && seg is not null)
                {
                    return (sentence, seg.Emotion);
                }
            }
            else
            {
                if (this.segmentsCache.TryDequeue(out OutSegment? seg) && seg is not null)
                {
                    return (sentence, seg.Emotion);
                }
            }
            return (sentence, Emotion.Neutral);
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
                this.ClearAllSessionAudioBuffers();
                this.WebSocketClient?.Dispose();
            }
        }
    }
}
