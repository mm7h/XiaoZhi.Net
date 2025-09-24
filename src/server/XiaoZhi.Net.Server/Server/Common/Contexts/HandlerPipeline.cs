using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class HandlerPipeline
    {
#if DEBUG
        private const int CHANNEL_CAPACITY = 500;
#else
        private const int CHANNEL_CAPACITY = 1500;
#endif

        private readonly Session _currentSession;

        private ILogger _logger;
        private HelloMessageHandler _helloMessageHandler;
        private TextHandler _textHandler;
        private AudioReceiveHandler _audioReceiveHandler;
        private Audio2TextHandler _audio2TextHandler;
        private DialogueHandler _dialogueHandler;
        private Text2AudioHandler _text2AudioHandler;
        private AudioMixingHandler _audioMixingHandler;
        private AudioSendHandler _audioSendHandler;

        private readonly IList<IHandler> _handlerContainer;

        public HandlerPipeline(Session session, IServiceProvider serviceProvider, ILogger logger)
        {
            this._currentSession = session;
            this._helloMessageHandler = serviceProvider.GetRequiredService<HelloMessageHandler>();
            this._textHandler = serviceProvider.GetRequiredService<TextHandler>();
            this._audioReceiveHandler = serviceProvider.GetRequiredService<AudioReceiveHandler>();
            this._audio2TextHandler = serviceProvider.GetRequiredService<Audio2TextHandler>();
            this._dialogueHandler = serviceProvider.GetRequiredService<DialogueHandler>();
            this._text2AudioHandler = serviceProvider.GetRequiredService<Text2AudioHandler>();
            this._audioMixingHandler = serviceProvider.GetRequiredService<AudioMixingHandler>();
            this._audioSendHandler = serviceProvider.GetRequiredService<AudioSendHandler>();

            this._handlerContainer = [
                this._helloMessageHandler, 
                this._textHandler,
                this._audioReceiveHandler,
                this._audio2TextHandler,
                this._audioMixingHandler,
                this._text2AudioHandler,
                this._dialogueHandler,
                this._audioSendHandler
            ];
            this._logger = logger;
        }

        public void InitHandlerPipeline()
        {
            this._textHandler.OnManualStop += this._audioReceiveHandler.HandleAudio;
            this._audioReceiveHandler.OnNoVoiceCloseConnect += this._dialogueHandler.NoVoiceCloseConnect;

            this.InitializeSendOutter(this._audioReceiveHandler);
            this.InitializeSendOutter(this._audio2TextHandler);
            this.InitializeSendOutter(this._textHandler);
            this.InitializeSendOutter(this._dialogueHandler);
            this.InitializeSendOutter(this._text2AudioHandler);
            this.InitializeSendOutter(this._audioMixingHandler);
            this.InitializeSendOutter(this._audioSendHandler);

            this.BuildHandlersWorkflow(this._audioReceiveHandler, this._audio2TextHandler);
            this.BuildHandlersWorkflow(this._textHandler, this._audio2TextHandler, this._dialogueHandler);
            this.BuildHandlersWorkflow(this._dialogueHandler, this._text2AudioHandler);
            this.BuildHandlersWorkflow(this._text2AudioHandler, this._audioMixingHandler);
            this.BuildHandlersWorkflow(this._audioMixingHandler, this._audioSendHandler);

            this.ScheduleOnAbort(this._textHandler);
            this.ScheduleOnAbort(this._audioReceiveHandler);
            this.ScheduleOnAbort(this._audio2TextHandler);
            this.ScheduleOnAbort(this._dialogueHandler);
            this.ScheduleOnAbort(this._text2AudioHandler);
            this.ScheduleOnAbort(this._audioMixingHandler);
            this.ScheduleOnAbort(this._audioSendHandler);
        }

        public void HandleHelloMessage(JsonObject helloMessage)
        {
            this._helloMessageHandler.Handle(helloMessage);
        }

        public void HandleTextMessage(string data)
        {
            if (this._textHandler is not null)
            {
                this._textHandler.Handle(data);
            }
            else
            {
                this._logger?.LogError("TextHandler is not initialized");
            }
        }

        public async Task HandleBinaryMessageAsync(byte[] data)
        {
            if (this._audioReceiveHandler is not null)
            {
                if (this._currentSession is null || this._currentSession.ShouldIgnore())
                {
                    return;
                }
                try
                {
                    if (!this._currentSession.IsIdle)
                    {
#if DEBUG
                        this._logger?.LogDebug("The previous audio packet is processing, this packet would be ignored, frame size {length}.", data.Length);
#endif
                        return;
                    }

                    await this._audioReceiveHandler.Handle(data);
                }
                catch (Exception ex)
                {
                    this._logger?.LogError(ex, "Failed to process the message packet from device: {deviceId} and session id: {sessionId}.", this._currentSession.DeviceId, this._currentSession.SessionId);
                }
            }
            else
            {
                this._logger?.LogError("AudioReceiveHandler is not initialized");
            }
        }

        public void Release()
        {
            foreach (IDisposable handler in this._handlerContainer)
            {
                handler.Dispose();
            }
            this._handlerContainer.Clear();
        }

        private void InitializeSendOutter(IHandler outHandler)
        {
            outHandler.SendOutter = this._currentSession.SendOutter;
        }

        private void BuildHandlersWorkflow<T>(IOutHandler<T> previous, IInHandler<T> next)
        {
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(CHANNEL_CAPACITY)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            };
            Channel<Workflow<T>> channel = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous.NextWriter = channel.Writer;
            next.PreviousReader = channel.Reader;

            Task.Run(next.Handle);
            this._logger?.LogDebug("Builded the workflow of handlers, previous: {previous} -> next: {next}", previous.GetType().Name, next.GetType().Name);
        }

        private void BuildHandlersWorkflow<T1, T2, T3>(IOutHandler<T1, T2, T3> previous, IInHandler<T1, T2, T3> next)
        {
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(CHANNEL_CAPACITY)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            };
            Channel<Workflow<T1>> channel = Channel.CreateBounded<Workflow<T1>>(boundedChannelOptions);
            previous.NextWriter = channel.Writer;
            next.PreviousReader = channel.Reader;
            Task.Run(next.Handle);

            Channel<Workflow<T2>> channel2 = Channel.CreateBounded<Workflow<T2>>(boundedChannelOptions);
            previous.NextWriter2 = channel2.Writer;
            next.PreviousReader2 = channel2.Reader;
            Task.Run(next.Handle2);

            Channel<Workflow<T3>> channel3 = Channel.CreateBounded<Workflow<T3>>(boundedChannelOptions);
            previous.NextWriter3 = channel3.Writer;
            next.PreviousReader3 = channel3.Reader;
            Task.Run(next.Handle3);

            this._logger?.LogDebug("Builded the workflow of handlers, previous: {previous} -> next: {next}", previous.GetType().Name, next.GetType().Name);
        }

        private void BuildHandlersWorkflow<T>(IOutHandler<T> previous1, IOutHandler<T> previous2, IInHandler<T, T> next)
        {
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(CHANNEL_CAPACITY)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            };
            Channel<Workflow<T>> channel1 = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous1.NextWriter = channel1.Writer;
            next.PreviousReader = channel1.Reader;
            Task.Run(next.Handle);

            Channel<Workflow<T>> channel2 = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous2.NextWriter = channel2.Writer;
            next.PreviousReader2 = channel2.Reader;
            Task.Run(next.Handle2);

            this._logger?.LogDebug("Builded the workflow of handlers, previous: {previous} -> next: {next}", previous1.GetType().Name, next.GetType().Name);
            this._logger?.LogDebug("Builded the workflow of handlers, previous: {previous} -> next: {next}", previous2.GetType().Name, next.GetType().Name);
        }

        private void ScheduleOnAbort(BaseHandler handler)
        {
            handler.OnAbort += (deviceId, sessionId, message) =>
            {
                this._logger?.LogDebug("Device: {deviceId}, session: {sessionId} abort the tasks, message: {message}.", deviceId, sessionId, message);
            };
        }
    }
}
