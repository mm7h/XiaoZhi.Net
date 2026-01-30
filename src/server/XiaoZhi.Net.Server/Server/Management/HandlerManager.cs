using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Management
{
    internal class HandlerManager
    {
#if DEBUG
        private const int CHANNEL_CAPACITY = 100;
#else
        private const int CHANNEL_CAPACITY = 200;
#endif

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<HandlerManager> _logger;

        public HandlerManager(IServiceProvider serviceProvider, ILogger<HandlerManager> logger)
        {
            this._serviceProvider = serviceProvider;
            this._logger = logger;
        }

        public static IHostBuilder RegisterServices(IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddTransient<HelloMessageHandler>();
                services.AddTransient<TextHandler>();
                services.AddTransient<AudioReceiveHandler>();
                services.AddTransient<Audio2TextHandler>();
                services.AddTransient<DialogueHandler>();
                services.AddTransient<Text2AudioHandler>();
                services.AddTransient<AudioProcessorHandler>();
                services.AddTransient<AudioSendHandler>();

                services.AddSingleton<HandlerManager>();
            });
        }

        public void InitializeHelloMessageHandler(Session session)
        {
            var helloMessageHandler = this._serviceProvider.GetRequiredService<HelloMessageHandler>();
            this.InitializeSendOutter(session, helloMessageHandler);

            session.HandlerPipeline.InitHelloMessageHandler(helloMessageHandler);
        }

        public bool InitializePrivateConfig(Session session)
        {
            var textHandler = this._serviceProvider.GetRequiredService<TextHandler>();
            var audioReceiveHandler = this._serviceProvider.GetRequiredService<AudioReceiveHandler>();
            var audio2TextHandler = this._serviceProvider.GetRequiredService<Audio2TextHandler>();
            var dialogueHandler = this._serviceProvider.GetRequiredService<DialogueHandler>();
            var text2AudioHandler = this._serviceProvider.GetRequiredService<Text2AudioHandler>();
            var audioProcessorHandler = this._serviceProvider.GetRequiredService<AudioProcessorHandler>();
            var audioSendHandler = this._serviceProvider.GetRequiredService<AudioSendHandler>();

            IDictionary<string, IHandler> handlerContainer = new Dictionary<string, IHandler>
            {
                [textHandler.HandlerName] = textHandler,
                [audioReceiveHandler.HandlerName] = audioReceiveHandler,
                [audio2TextHandler.HandlerName] = audio2TextHandler,
                [dialogueHandler.HandlerName] = dialogueHandler,
                [text2AudioHandler.HandlerName] = text2AudioHandler,
                [audioProcessorHandler.HandlerName] = audioProcessorHandler,
                [audioSendHandler.HandlerName] = audioSendHandler
            };

            textHandler.OnManualStop += audioReceiveHandler.HandleManualStop;
            audioReceiveHandler.OnNoVoiceCloseConnect += dialogueHandler.NoVoiceCloseConnect;

            this.InitializeSendOutter(session, audioReceiveHandler);
            this.InitializeSendOutter(session, audio2TextHandler);
            this.InitializeSendOutter(session, textHandler);
            this.InitializeSendOutter(session, dialogueHandler);
            this.InitializeSendOutter(session, text2AudioHandler);
            this.InitializeSendOutter(session, audioProcessorHandler);
            this.InitializeSendOutter(session, audioSendHandler);

            this.ScheduleOnAbort(textHandler);
            this.ScheduleOnAbort(audioReceiveHandler);
            this.ScheduleOnAbort(audio2TextHandler);
            this.ScheduleOnAbort(dialogueHandler);
            this.ScheduleOnAbort(text2AudioHandler);
            this.ScheduleOnAbort(audioProcessorHandler);
            this.ScheduleOnAbort(audioSendHandler);

            bool buildResults = handlerContainer.Values
               .AsParallel()
               .Select(h => h.Build(session.PrivateProvider))
               .All(result => result);

            if (!buildResults)
            {
                this._logger.LogError("Failed to build the handler pipeline for device: {deviceId}.", session.DeviceId);
                return false;
            }

            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, audioReceiveHandler, audio2TextHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, textHandler, audio2TextHandler, dialogueHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, dialogueHandler, text2AudioHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, text2AudioHandler, audioProcessorHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, audioProcessorHandler, audioSendHandler);

            session.HandlerPipeline.InitHandlerPipeline(handlerContainer);

            return true;
        }

        private void InitializeSendOutter(Session session, IHandler outHandler)
        {
            outHandler.SendOutter = session.SendOutter;
        }

        private void BuildHandlersWorkflow<T>(int channelCapacity, IOutHandler<T> previous, IInHandler<T> next)
        {
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(channelCapacity)
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

        private void BuildHandlersWorkflow<T1, T2, T3>(int channelCapacity, IOutHandler<T1, T2, T3> previous, IInHandler<T1, T2, T3> next)
        {
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(channelCapacity)
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

        private void BuildHandlersWorkflow<T>(int channelCapacity, IOutHandler<T> previous1, IOutHandler<T> previous2, IInHandler<T, T> next)
        {
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(channelCapacity)
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
