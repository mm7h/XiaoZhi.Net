using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using XiaoZhi.Net.Server.Resources;
using XiaoZhi.Net.Server.Resources.DeviceBinding;
using XiaoZhi.Net.Server.Resources.Musics;
using XiaoZhi.Net.Server.Resources.OnnxModels;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD;

namespace XiaoZhi.Net.Server.Management
{
    internal class ResourceManager
    {
        private readonly XiaoZhiConfig _config;
        public ResourceManager(XiaoZhiConfig config)
        {
            this._config = config;
        }
        public static IHostBuilder RegisterServices(IHostBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IDeviceBinding, DefaultDeviceBinding>();
                services.AddSingleton<IMusics, MusicProvider>();
                services.AddSingleton<IVadOnnxModel, SileroOnnx>();

                services.AddSingleton<ResourceManager>();
            });
        }

        public bool BuildComponent(IServiceProvider serviceProvider)
        {
            #region DeviceBinding
            IDeviceBinding deviceBinding = serviceProvider.GetRequiredService<IDeviceBinding>();
            if (!deviceBinding.Load(this._config.DeviceBindSetting))
            {
                return false;
            }
            #endregion

            #region Musics
            IMusics musics = serviceProvider.GetRequiredService<IMusics>();
            if (!musics.Load(this._config.MusicProviderSetting))
            {
                return false;
            }
            #endregion

            #region Onnx models
            IVadOnnxModel vadOnnxModel = serviceProvider.GetRequiredService<IVadOnnxModel>();
            if (!vadOnnxModel.Load(this.GetSelectedSetting("VAD", this._config)))
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

        public void Dispose(IServiceProvider serviceProvider)
        {
            IList<IDisposable> resources = new List<IDisposable>
            {
                serviceProvider.GetRequiredService<IDeviceBinding>(),
                serviceProvider.GetRequiredService<IMusics>(),
                serviceProvider.GetRequiredService<IVadOnnxModel>()
            };

            foreach (IDisposable resource in resources)
            {
                resource.Dispose();
            }
        }
    }
}
