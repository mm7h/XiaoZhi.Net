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
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Management
{
    internal class HandlerManager : BaseManager
    {
#if DEBUG
        private const int CHANNEL_CAPACITY = 100;
#else
        private const int CHANNEL_CAPACITY = 200;
#endif

        public HandlerManager(IServiceProvider serviceProvider, XiaoZhiConfig config, ILogger<HandlerManager> logger) : base(serviceProvider, config, logger)
        {
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
        public override bool BuildComponent()
        {
            return true;
        }

        public override Task OnSessionConnectedAsync(Session session)
        {
            var helloMessageHandler = this.ServiceProvider.GetRequiredService<HelloMessageHandler>();
            this.InitializeSendOutter(session, helloMessageHandler);

            session.HandlerPipeline.InitHelloMessageHandler(helloMessageHandler);
            return Task.CompletedTask;
        }

        public override Task<bool> OnSessionPropertyInitializingAsync(Session session)
        {
            var textHandler = this.ServiceProvider.GetRequiredService<TextHandler>();
            var audioReceiveHandler = this.ServiceProvider.GetRequiredService<AudioReceiveHandler>();
            var audio2TextHandler = this.ServiceProvider.GetRequiredService<Audio2TextHandler>();
            var dialogueHandler = this.ServiceProvider.GetRequiredService<DialogueHandler>();
            var text2AudioHandler = this.ServiceProvider.GetRequiredService<Text2AudioHandler>();
            var audioProcessorHandler = this.ServiceProvider.GetRequiredService<AudioProcessorHandler>();
            var audioSendHandler = this.ServiceProvider.GetRequiredService<AudioSendHandler>();

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
            audioReceiveHandler.OnNoVoiceCloseConnect += dialogueHandler.NoVoiceCloseConnectAsync;

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
                this.Logger.LogError(Lang.HandlerManager_InitializePrivateConfig_BuildPipelineFailed, session.DeviceId);
                return Task.FromResult(false);
            }

            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, audioReceiveHandler, audio2TextHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, textHandler, audio2TextHandler, dialogueHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, dialogueHandler, text2AudioHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, text2AudioHandler, audioProcessorHandler);
            this.BuildHandlersWorkflow(CHANNEL_CAPACITY, audioProcessorHandler, audioSendHandler);

            session.HandlerPipeline.InitHandlerPipeline(handlerContainer);

            return Task.FromResult(true);
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

            Task.Run(next.HandleAsync);
            this.Logger?.LogDebug(Lang.HandlerManager_BuildHandlersWorkflow_BuiltWorkflow, previous.GetType().Name, next.GetType().Name);
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
            Task.Run(next.HandleAsync);

            Channel<Workflow<T2>> channel2 = Channel.CreateBounded<Workflow<T2>>(boundedChannelOptions);
            previous.NextWriter2 = channel2.Writer;
            next.PreviousReader2 = channel2.Reader;
            Task.Run(next.Handle2Async);

            Channel<Workflow<T3>> channel3 = Channel.CreateBounded<Workflow<T3>>(boundedChannelOptions);
            previous.NextWriter3 = channel3.Writer;
            next.PreviousReader3 = channel3.Reader;
            Task.Run(next.Handle3Async);

            this.Logger?.LogDebug(Lang.HandlerManager_BuildHandlersWorkflow_BuiltWorkflow, previous.GetType().Name, next.GetType().Name);
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
            Task.Run(next.HandleAsync);

            Channel<Workflow<T>> channel2 = Channel.CreateBounded<Workflow<T>>(boundedChannelOptions);
            previous2.NextWriter = channel2.Writer;
            next.PreviousReader2 = channel2.Reader;
            Task.Run(next.Handle2Async);

            this.Logger?.LogDebug(Lang.HandlerManager_BuildHandlersWorkflow_BuiltWorkflow, previous1.GetType().Name, next.GetType().Name);
            this.Logger?.LogDebug(Lang.HandlerManager_BuildHandlersWorkflow_BuiltWorkflow, previous2.GetType().Name, next.GetType().Name);
        }

        private void ScheduleOnAbort(BaseHandler handler)
        {
            handler.OnAbort += (deviceId, sessionId, message) =>
            {
                this.Logger?.LogDebug(Lang.HandlerManager_ScheduleOnAbort_Aborted, deviceId, sessionId, message);
            };
        }
    }
}
