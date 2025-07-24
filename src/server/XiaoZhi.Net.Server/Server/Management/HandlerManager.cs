using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XiaoZhi.Net.Server.Handlers;

namespace XiaoZhi.Net.Server.Management
{
    internal sealed class HandlerManager
    {
        public static void RegisterServices(HostApplicationBuilder builder)
        {
            IServiceCollection services = builder.Services;
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
