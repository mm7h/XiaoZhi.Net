using Microsoft.Extensions.DependencyInjection;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Management
{
    internal sealed class HandlerManager
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddTransient<TextHandler>();
            services.AddTransient<AudioReceiveHandler>();
            services.AddTransient<Audio2TextHandler>();
            services.AddTransient<DialogueHandler>();
            services.AddTransient<Text2AudioHandler>();
            services.AddTransient<AudioSendHandler>();

            services.AddSingleton<HandlerManager>();
        }
    }
}
