using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Handlers;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class HandlerPipeline
    {
        private readonly Session _currentSession;

        private ILogger? _logger;
        private TextHandler? _textHandler;
        private AudioReceiveHandler? _audioReceiveHandler;
        private Audio2TextHandler? _audio2TextHandler;
        private DialogueHandler? _dialogueHandler;
        private AudioSendHandler? _audioSendHandler;
        private Text2AudioHandler? _text2AudioHandler;
        private readonly IList<IDisposable> _disposableHandlers;

        public HandlerPipeline(Session session)
        {
            this._currentSession = session;
            this._disposableHandlers = new List<IDisposable>(5);
        }

        public async Task InitHandlerPipelineAsync(IServiceProvider serviceProvider, ILogger logger)
        {
            this._logger = logger;

            this._textHandler = serviceProvider.GetRequiredService<TextHandler>();
            this._audioReceiveHandler = serviceProvider.GetRequiredService<AudioReceiveHandler>();
            this._audio2TextHandler = serviceProvider.GetRequiredService<Audio2TextHandler>();
            this._dialogueHandler = serviceProvider.GetRequiredService<DialogueHandler>();
            this._text2AudioHandler = serviceProvider.GetRequiredService<Text2AudioHandler>();
            this._audioSendHandler = serviceProvider.GetRequiredService<AudioSendHandler>();

            this._textHandler.OnManualStop += this._audioReceiveHandler.HandleAudio;
            this._audioReceiveHandler.OnNoVoiceCloseConnect += this._dialogueHandler.NoVoiceCloseConnect;

            this.InitializeSendOutter(this._audioReceiveHandler);
            this.InitializeSendOutter(this._audio2TextHandler);
            this.InitializeSendOutter(this._textHandler);
            this.InitializeSendOutter(this._dialogueHandler);
            this.InitializeSendOutter(this._text2AudioHandler);
            this.InitializeSendOutter(this._audioSendHandler);

            await this.BuildHandlersWorkflow(this._audioReceiveHandler, this._audio2TextHandler);
            await this.BuildHandlersWorkflow(this._textHandler, this._audio2TextHandler, this._dialogueHandler);
            await this.BuildHandlersWorkflow(this._dialogueHandler, this._text2AudioHandler);
            await this.BuildHandlersWorkflow(this._text2AudioHandler, this._audioSendHandler);

            this.ScheduleOnAbort(this._textHandler);
            this.ScheduleOnAbort(this._audioReceiveHandler);
            this.ScheduleOnAbort(this._audio2TextHandler);
            this.ScheduleOnAbort(this._dialogueHandler);
            this.ScheduleOnAbort(this._text2AudioHandler);
            this.ScheduleOnAbort(this._audioSendHandler);

            this._disposableHandlers.Add(this._textHandler);
            this._disposableHandlers.Add(this._audioReceiveHandler);
            this._disposableHandlers.Add(this._audio2TextHandler);
            this._disposableHandlers.Add(this._dialogueHandler);
            this._disposableHandlers.Add(this._text2AudioHandler);
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
            foreach (IDisposable handler in this._disposableHandlers)
            {
                handler.Dispose();
            }
            this._disposableHandlers.Clear();
        }

        public void SetAudioPlayerClient(IAudioPlayerClient audioPlayerClient)
        { 
            this._text2AudioHandler?.SetAudioPlayerClient(audioPlayerClient);
        }

        private void InitializeSendOutter(IHandler outHandler)
        {
            outHandler.SendOutter = this._currentSession.SendOutter;
        }

        private async Task BuildHandlersWorkflow<T>(IOutHandler<T> previous, IInHandler<T> next)
        {
#if DEBUG
            int capacity = 100;
#else
            int capacity = 1000;
#endif
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true
            };
            Channel<Workflow<T>> channel = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous.NextWriter = channel.Writer;
            next.PreviousReader = channel.Reader;

            await Task.Factory.StartNew(next.Handle, TaskCreationOptions.LongRunning);
            this._logger?.LogDebug("Builded the workflow of handlers, previous: {previous} -> next: {next}", previous.GetType().Name, next.GetType().Name);
        }

        private async Task BuildHandlersWorkflow<T>(IOutHandler<T> previous1, IOutHandler<T> previous2, IInHandler<T, T> next)
        {
#if DEBUG
            int capacity = 100;
#else
            int capacity = 1000;
#endif
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true
            };
            Channel<Workflow<T>> channel1 = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous1.NextWriter = channel1.Writer;
            next.PreviousReader = channel1.Reader;
            await Task.Factory.StartNew(next.Handle, TaskCreationOptions.LongRunning);

            Channel<Workflow<T>> channel2 = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous2.NextWriter = channel2.Writer;
            next.PreviousReader2 = channel2.Reader;
            await Task.Factory.StartNew(next.Handle2, TaskCreationOptions.LongRunning);

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
