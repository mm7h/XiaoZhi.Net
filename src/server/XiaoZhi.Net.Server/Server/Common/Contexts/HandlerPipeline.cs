using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class HandlerPipeline
    {
        private readonly Session _currentSession;

        private ILogger? _logger;
        private TextHandler? _textMessageEntry;
        private AudioReceiveHandler? _binaryMessageEntry;
        private AudioSendHandler? _audioSendHandler;
        private readonly IList<IDisposable> _disposableHandlers;

        public HandlerPipeline(Session session)
        {
            this._currentSession = session;
            this._disposableHandlers = new List<IDisposable>(5);
        }

        public void InitHandlerPipeline(IServiceProvider serviceProvider, ILogger logger)
        {
            this._logger = logger;

            TextHandler textHandler = serviceProvider.GetRequiredService<TextHandler>();
            AudioReceiveHandler audioReceiveHandler = serviceProvider.GetRequiredService<AudioReceiveHandler>();
            Audio2TextHandler audio2TextHandler = serviceProvider.GetRequiredService<Audio2TextHandler>();
            DialogueHandler dialogueHandler = serviceProvider.GetRequiredService<DialogueHandler>();
            Text2AudioHandler text2AudioHandler = serviceProvider.GetRequiredService<Text2AudioHandler>();
            AudioSendHandler audioSendHandler = serviceProvider.GetRequiredService<AudioSendHandler>();

            textHandler.OnManualStop += audioReceiveHandler.HandleAudio;
            audioReceiveHandler.OnNoVoiceCloseConnect += dialogueHandler.NoVoiceCloseConnect;

            this.InitializeSendOutter(audioReceiveHandler);
            this.InitializeSendOutter(audio2TextHandler);
            this.InitializeSendOutter(textHandler);
            this.InitializeSendOutter(dialogueHandler);
            this.InitializeSendOutter(text2AudioHandler);
            this.InitializeSendOutter(audioSendHandler);

            this.BuildHandlersWorkflow(audioReceiveHandler, audio2TextHandler);
            this.BuildHandlersWorkflow(textHandler, dialogueHandler);
            this.BuildHandlersWorkflow(audio2TextHandler, dialogueHandler);
            this.BuildHandlersWorkflow(dialogueHandler, text2AudioHandler);
            this.BuildHandlersWorkflow(text2AudioHandler, audioSendHandler);

            this.ScheduleOnAbort(textHandler);
            this.ScheduleOnAbort(audioReceiveHandler);
            this.ScheduleOnAbort(audio2TextHandler);
            this.ScheduleOnAbort(dialogueHandler);
            this.ScheduleOnAbort(text2AudioHandler);
            this.ScheduleOnAbort(audioSendHandler);

            this._textMessageEntry = textHandler;
            this._binaryMessageEntry = audioReceiveHandler;
            this._audioSendHandler = audioSendHandler;

            this._disposableHandlers.Add(textHandler);
            this._disposableHandlers.Add(audioReceiveHandler);
            this._disposableHandlers.Add(audio2TextHandler);
            this._disposableHandlers.Add(dialogueHandler);
            this._disposableHandlers.Add(text2AudioHandler);
        }

        /// <summary>
        /// 将音频数据直接推送到AudioSendHandler处理
        /// </summary>
        /// <param name="audioData">音频数据</param>
        /// <returns>处理任务</returns>
        public async Task PushAudioToSendAsync(float[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
                return;

            if (this._audioSendHandler != null)
            {
                try
                {
                    // 创建一个包含音频数据的工作流
                    var workflow = this._currentSession.ToWorkflow(audioData);

                    // 直接处理音频数据
                    await this._audioSendHandler.Handle(workflow);
                }
                catch (Exception ex)
                {
                    this._logger?.LogError(ex, "Failed to push audio data to AudioSendHandler for device: {deviceId}", this._currentSession.DeviceId);
                }
            }
            else
            {
                this._logger?.LogError("AudioSendHandler is not initialized");
            }
        }

        public void HandleTextMessage(string data)
        {
            if (this._textMessageEntry is not null)
            {
                this._textMessageEntry.Handle(data);
            }
            else
            {
                this._logger?.LogError("TextHandler is not initialized");
            }
        }

        public async Task HandleBinaryMessageAsync(byte[] data)
        {
            if (this._binaryMessageEntry is not null)
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

                    await this._binaryMessageEntry.Handle(data);
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

        private void InitializeSendOutter(IHandler outHandler)
        {
            outHandler.SendOutter = this._currentSession.SendOutter;
        }

        private void BuildHandlersWorkflow<T>(IOutHandler<T> previous, IInHandler<T> next)
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

            Task.Factory.StartNew(async () => await next.Handle(), TaskCreationOptions.LongRunning).ConfigureAwait(false);
            this._logger?.LogDebug("Builded the workflow of handlers, previous: {previous} -> next: {next}", previous.GetType().Name, next.GetType().Name);
        }

        private void BuildHandlersWorkflow<T>(IOutHandler<T> previous, IInHandler<T, T> next)
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
            next.PreviousReader1 = channel.Reader;
            next.PreviousReader2 = channel.Reader;

            Task.Factory.StartNew(async () => await next.Handle1(), TaskCreationOptions.LongRunning).ConfigureAwait(false);
            Task.Factory.StartNew(async () => await next.Handle2(), TaskCreationOptions.LongRunning).ConfigureAwait(false);
            this._logger?.LogDebug("Builded the workflow of handlers, previous: {previous} -> next: {next}", previous.GetType().Name, next.GetType().Name);
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
