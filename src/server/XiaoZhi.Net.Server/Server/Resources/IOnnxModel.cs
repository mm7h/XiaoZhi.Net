using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IOnnxModel : IResource<ModelSetting>
    {
        string ModelType { get; }
        string ModelName { get; }
    }
}
