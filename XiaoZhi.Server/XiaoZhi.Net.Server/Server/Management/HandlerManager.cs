using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Threading.Channels;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Management
{
    internal sealed class HandlerManager
    {
        private readonly ILogger _logger;

        public HandlerManager(ILogger logger)
        {
            this._logger = logger;
        }

        public static void RegisterServices(IServiceCollection services, XiaoZhiConfig config)
        {
            services.AddSingleton<AuthHandler>();
            services.AddSingleton<SocketHandler>();
            services.AddSingleton<TextHandler>();
            services.AddSingleton<AudioReceiveHandler>();
            services.AddSingleton<Audio2TextHandler>();
            services.AddSingleton<DialogueHandler>();
            services.AddSingleton<Text2AudioHandler>();
            services.AddSingleton<AudioSendHandler>();

            services.AddSingleton<HandlerManager>();
        }

        public void Initialize(IServiceProvider serviceProvider)
        {
            AuthHandler authHandler = serviceProvider.GetRequiredService<AuthHandler>();
            SocketHandler socketHandler = serviceProvider.GetRequiredService<SocketHandler>();
            TextHandler textHandler = serviceProvider.GetRequiredService<TextHandler>();
            AudioReceiveHandler audioReceiveHandler = serviceProvider.GetRequiredService<AudioReceiveHandler>();
            Audio2TextHandler audio2TextHandler = serviceProvider.GetRequiredService<Audio2TextHandler>();
            DialogueHandler dialogueHandler = serviceProvider.GetRequiredService<DialogueHandler>();
            Text2AudioHandler text2AudioHandler = serviceProvider.GetRequiredService<Text2AudioHandler>();
            AudioSendHandler audioSendHandler = serviceProvider.GetRequiredService<AudioSendHandler>();

            socketHandler.OnDeviceConnected += dialogueHandler.InitializePrompt;
            socketHandler.OnTextPacket += textHandler.Handle;

            textHandler.OnManualStop += audioReceiveHandler.HandleAudio;
            audioReceiveHandler.OnNoVoiceCloseConnect += dialogueHandler.NoVoiceCloseConnect;

            this.InitializeHandlersWorkflow(socketHandler, audioReceiveHandler);
            this.InitializeHandlersWorkflow(audioReceiveHandler, audio2TextHandler);
            this.InitializeHandlersWorkflow(textHandler, dialogueHandler);
            this.InitializeHandlersWorkflow(audio2TextHandler, dialogueHandler);
            this.InitializeHandlersWorkflow(dialogueHandler, text2AudioHandler);
            this.InitializeHandlersWorkflow(text2AudioHandler, audioSendHandler);

            this.ScheduleOnAbort(socketHandler);
            this.ScheduleOnAbort(textHandler);
            this.ScheduleOnAbort(authHandler);
            this.ScheduleOnAbort(audioReceiveHandler);
            this.ScheduleOnAbort(audio2TextHandler);
            this.ScheduleOnAbort(dialogueHandler);
            this.ScheduleOnAbort(text2AudioHandler);
            this.ScheduleOnAbort(audioSendHandler);

        }

        private void InitializeHandlersWorkflow<T>(IOutHandler<T> previous, IInHandler<T> next)
        {
#if DEBUG
            int capacity = 100;
#else
            int capacity = 1000;
#endif
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = false
            };
            Channel<Workflow<T>> channel = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous.NextWriter = channel.Writer;
            next.PreviousReader = channel.Reader;
            next.Handle();
            this._logger.Debug($"Initialized the workflow of handlers, previous: {previous.GetType().Name} -> next: {next.GetType().Name}");
        }

        private void ScheduleOnAbort(BaseHandler handler)
        {
            handler.OnAbort += (deviceId, sessionId, message) =>
            {
                this._logger.Debug($"Device: {deviceId}, session: {sessionId} abort the tasks, message: {message}.");
            };
        }

        public void Dispose(IServiceProvider serviceProvider)
        {
            SocketHandler socketHandler = serviceProvider.GetRequiredService<SocketHandler>();
            TextHandler textHandler = serviceProvider.GetRequiredService<TextHandler>();
            AudioReceiveHandler audioReceiveHandler = serviceProvider.GetRequiredService<AudioReceiveHandler>();
            Audio2TextHandler audio2TextHandler = serviceProvider.GetRequiredService<Audio2TextHandler>();
            DialogueHandler dialogueHandler = serviceProvider.GetRequiredService<DialogueHandler>();
            Text2AudioHandler text2AudioHandler = serviceProvider.GetRequiredService<Text2AudioHandler>();

            socketHandler.Dispose();
            textHandler.Dispose();
            audioReceiveHandler.Dispose();
            audio2TextHandler.Dispose();
            dialogueHandler.Dispose();
            text2AudioHandler.Dispose();
        }
    }
}
