using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Constants;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal sealed class Text2AudioHandler : BaseHandler, IInHandler<OutSegment>, IOutHandler<float[]>
    {
        private readonly ITts _tts;
        public Text2AudioHandler([FromKeyedServices(GlobalProviderNames.GLOBAL_TTS)] ITts tts, XiaoZhiConfig config, ILogger<Text2AudioHandler> logger) : base(config, logger)
        {
            Console.WriteLine("666666666666666666666");
            this._tts = tts;
            this._tts.OnBeforeProcessing += this.TTS_OnBeforeProcessing;
            this._tts.OnProcessing += this.TTS_OnProcessing;
            this._tts.OnProcessed += this.TTS_OnProcessed;
        }


        public override string HandlerName => nameof(Text2AudioHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;
        public ChannelReader<Workflow<OutSegment>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<float[]>> NextWriter { get; set; } = null!;

        public async Task Handle()
        {
            await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async Task Handle(Workflow<OutSegment> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            try
            {
                if (string.IsNullOrEmpty(workflow.Data.Content))
                {
                    this.Logger.LogInformation("No tts required, the query text is empty.");
                    return;
                }

                if (session.PrivateProvider is not null && session.PrivateProvider.Tts is not null)
                {
                    await session.PrivateProvider.Tts.SynthesisAsync(workflow, session, session.SessionCtsToken);
                }
                else
                {
                    await this._tts.SynthesisAsync(workflow, session, session.SessionCtsToken);
                }

            }
            catch (OperationCanceledException)
            {
                session.SentenceTimeAxisContext.Reset();

                await this.SendOutter.SendLlmMessageAsync(Emotion.Confident);
                await this.SendOutter.SendTtsMessageAsync("stop");

                this.FireAbort(session.DeviceId, session.SessionId, "text to audio");
            }
        }
        public void Dispose()
        {
            Console.WriteLine("7777777777777");
            this._tts.OnBeforeProcessing -= this.TTS_OnBeforeProcessing;
            this._tts.OnProcessing -= this.TTS_OnProcessing;
            this._tts.OnProcessed -= this.TTS_OnProcessed;
            this.NextWriter.Complete();
        }

        private async void TTS_OnBeforeProcessing(string sessionId, OutSegment segment)
        {
            if (segment.IsFirst)
            {
                this.Logger.LogInformation("Send the first audio from segment: {content}", segment.Content);
                await this.SendOutter.SendTtsMessageAsync("start");
                await this.SendOutter.SendLlmMessageAsync(Emotion.Cool);
            }
            Session session = this.SendOutter.GetSession();
            if (session != null)
            {
                Func<Task> sendSentenceAction = new Func<Task>(async () =>
                {
                    if (session.ShouldIgnore())
                    {
                        return;
                    }
                    await this.SendOutter.SendTtsMessageAsync("sentence_start", segment.Content);
                });
                await session.SentenceTimeAxisContext.AddSendSentenceActionAsync(sendSentenceAction, session.SessionCtsToken);
            }
        }

        private async void TTS_OnProcessing(string sessionId, float[] audioFrame)
        {
            await this.NextWriter.WriteAsync(new Workflow<float[]>(sessionId, audioFrame));
        }

        private async void TTS_OnProcessed(string sessionId, OutSegment segment, int duration)
        {
            Session session = this.SendOutter.GetSession();
            if (session != null)
            {
                Func<Task> sendSentenceAction = new Func<Task>(async () =>
                {
                    if (session.ShouldIgnore())
                    {
                        return;
                    }
                    await Task.Delay(duration, session.SessionCtsToken);
                    await this.SendOutter.SendTtsMessageAsync("sentence_end", segment.Content);

                    if (segment.IsLast)
                    {
                        session.SentenceTimeAxisContext.Reset();
                        await this.SendOutter.SendLlmMessageAsync(Emotion.Confident);
                        await this.SendOutter.SendTtsMessageAsync("stop");

                        if (session.CloseAfterChat)
                            await this.SendOutter.CloseSessionAsync("Close Chat");
                    }
                });
                await session.SentenceTimeAxisContext.AddSendSentenceActionAsync(sendSentenceAction, session.SessionCtsToken);
            }
        }
    }
}
