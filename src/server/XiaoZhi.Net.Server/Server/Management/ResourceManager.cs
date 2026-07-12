using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Resources;
using XiaoZhi.Net.Server.Resources.DeviceBinding;
using XiaoZhi.Net.Server.Resources.Musics;
using XiaoZhi.Net.Server.Resources.OnnxModels;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD;

namespace XiaoZhi.Net.Server.Management
{
    internal class ResourceManager : BaseManager
    {
        public ResourceManager(IServiceProvider serviceProvider, XiaoZhiConfig config, ILogger<ResourceManager> logger) : base(serviceProvider, config, logger)
        {
        }
        public static IHostBuilder RegisterServices(IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IDeviceBinding, DefaultDeviceBinding>();
                services.AddSingleton<IMusicFileProvider, MusicProvider>();
                services.AddSingleton<IVadOnnxModel, SileroOnnx>();

                services.AddSingleton<ResourceManager>();
            });
        }

        public override bool BuildComponent()
        {
            #region DeviceBinding
            IDeviceBinding deviceBinding = this.ServiceProvider.GetRequiredService<IDeviceBinding>();
            if (!deviceBinding.Load(this.Config.DeviceBindSetting))
            {
                return false;
            }
            #endregion

            #region Musics
            IMusicFileProvider musics = this.ServiceProvider.GetRequiredService<IMusicFileProvider>();
            if (!musics.Load(this.Config.MusicProviderSetting))
            {
                return false;
            }
            #endregion

            #region Onnx models
            IVadOnnxModel vadOnnxModel = this.ServiceProvider.GetRequiredService<IVadOnnxModel>();
            if (!vadOnnxModel.Load(this.GetSelectedSetting("VAD", this.Config)))
            {
                return false;
            }
            #endregion

            return true;
        }
        private ModelSetting GetSelectedSetting(string selectedModelType, XiaoZhiConfig config)
        {
            string selectedModel = config.SelectedSettings[selectedModelType];
            Dictionary<string, string> setting = config.ConfiguredSettings[selectedModelType][selectedModel];

            ModelSetting modelSetting = new ModelSetting
            {
                ModelName = selectedModel,
                Config = setting
            };

            return modelSetting;
        }

        public override void Dispose()
        {
            IList<IDisposable> resources = new List<IDisposable>
            {
                this.ServiceProvider.GetRequiredService<IDeviceBinding>(),
                this.ServiceProvider.GetRequiredService<IMusicFileProvider>(),
                this.ServiceProvider.GetRequiredService<IVadOnnxModel>()
            };

            foreach (IDisposable resource in resources)
            {
                resource.Dispose();
            }
        }
    }
}
