using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Helpers;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers;
using XiaoZhi.Net.Server.Providers.ASR;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class Audio2TextHandler : BaseHandler, IInHandler<float[]>, IOutHandler<string>, IAsrEventCallback
    {
        private readonly ObjectPool<Workflow<float[]>> _audioBufferWorkflowPool;
        private readonly ObjectPool<Workflow<string>> _stringWorkflowPool;
        private IAsr? _asr;

        public Audio2TextHandler(ObjectPool<Workflow<float[]>> circularBufferWorkflowPool,
            ObjectPool<Workflow<string>> stringWorkflowPool,
            XiaoZhiConfig config,
            ILogger<Audio2TextHandler> logger) : base(config, logger)
        {
            this._audioBufferWorkflowPool = circularBufferWorkflowPool;
            this._stringWorkflowPool = stringWorkflowPool;
        }

        public override string HandlerName => nameof(Audio2TextHandler);
        public ChannelReader<Workflow<float[]>> PreviousReader { get; set; } = null!;
        public ChannelWriter<Workflow<string>> NextWriter { get; set; } = null!;

        public override bool Build(PrivateProvider privateProvider)
        {
            Session session = this.SendOutter.GetSession();
            if (privateProvider.Asr is null)
            {
                this.Logger.LogError(Lang.Audio2TextHandler_Build_AsrNotConfigured, session.DeviceId);
                return false;
            }
            this._asr = privateProvider.Asr;
            this._asr.RegisterDevice(session.DeviceId, session.SessionId, this);
            this.RegisterCancellationToken();
            this.Builded = true;
            return true;
        }

        public async Task HandleAsync()
        {
            await foreach (var workflow in this.PreviousReader.ReadAllAsync())
            {
                try
                {
                    await this.HandleAsync(workflow);
                }
                finally
                {
                    this._audioBufferWorkflowPool.Return(workflow);
                }
            }
        }

        public async Task HandleAsync(Workflow<float[]> workflow)
        {
            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }

            if (!this.CheckWorkflowValid(workflow))
            {
                return;
            }

            if (this._asr is null)
            {
                this.Logger.LogError(Lang.Audio2TextHandler_Build_AsrNotConfigured, session.DeviceId);
                return;
            }
            if (this._asr.IsStreaming)
            {
                // Streaming ASR receives PCM frames directly from AudioReceiveHandler and
                // invokes this handler only after it has a final result.
                return;
            }
            try
            {
                if (!session.IsDeviceBinded)
                {
                    var notBindWorkflow = this._stringWorkflowPool.Get();
                    notBindWorkflow.Initialize(session, "NOT_BIND");
                    try
                    {
                        await this.NextWriter.WriteAsync(notBindWorkflow, this.HandlerToken);
                    }
                    catch (OperationCanceledException)
                    {
                        this._stringWorkflowPool.Return(notBindWorkflow);
                    }
                    return;
                }

                await this._asr.ConvertSpeechTextAsync(workflow, session.AudioSetting.SampleRate, session.AudioSetting.FrameSize, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.Audio2TextHandler_Handle_Cancelled, session.DeviceId);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.Audio2TextHandler_Handle_ProcessFailed, session.DeviceId);
            }
        }

        public void OnSpeechTextConverted(long turnId, bool success, string speechText)
        {
            this.ObserveSpeechResult(this.OnSpeechTextConvertedAsync(turnId, success, speechText));
        }

        private async Task OnSpeechTextConvertedAsync(long turnId, bool success, string speechText)
        {
            if (this.HandlerToken.IsCancellationRequested)
            {
                return;
            }

            Session session = this.SendOutter.GetSession();
            if (session is null || session.ShouldIgnore())
            {
                return;
            }
            if (turnId != session.TurnId)
            {
                this.Logger.LogDebug(Lang.Audio2TextHandler_OnSpeechTextConverted_StaleResult,
                    session.DeviceId, turnId, session.TurnId);
                return;
            }

            if (!success)
            {
                this.Logger.LogError(Lang.Audio2TextHandler_OnSpeechTextConverted_ConvertFailed);
                return;
            }
            if (string.IsNullOrWhiteSpace(speechText) || string.IsNullOrWhiteSpace(DialogueHelper.GetStringNoPunctuationOrEmoji(speechText)))
            {
                session.Reset();
                this.Logger.LogDebug(Lang.Audio2TextHandler_OnSpeechTextConverted_NoSpeak, session.DeviceId);
                return;
            }

            await this.SendOutter.SendSttMessageAsync(speechText);
            this.Logger.LogDebug(Lang.Audio2TextHandler_OnSpeechTextConverted_SpeakText, session.DeviceId, speechText);

            var nextWorkflow = this._stringWorkflowPool.Get();
            nextWorkflow.Initialize(session, speechText);

            try
            {
                await this.NextWriter.WriteAsync(nextWorkflow, this.HandlerToken);
            }
            catch (OperationCanceledException)
            {
                this._stringWorkflowPool.Return(nextWorkflow);
            }
        }

        private void ObserveSpeechResult(Task task)
        {
            _ = task.ContinueWith(
                completed => this.Logger.LogError(completed.Exception, Lang.Audio2TextHandler_ObserveSpeechResult_Failed),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public override void Dispose()
        {
            if (this._asr is not null)
            {
                Session session = this.SendOutter.GetSession();
                if (session is not null)
                {
                    this._asr.UnregisterDevice(session.DeviceId, session.SessionId);
                }
            }

            this.NextWriter.Complete();
            base.Dispose();
        }
    }
}
