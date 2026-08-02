using Microsoft.Agents.AI;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Resources
{
    internal interface IRag : IResource<ModelSetting>
    {
        bool IsReady { get; }
        TextSearchProvider? Create();
    }
}
