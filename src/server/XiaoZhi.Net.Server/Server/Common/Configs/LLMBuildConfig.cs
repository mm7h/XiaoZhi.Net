using System.Collections.Generic;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Common.Configs
{
    internal record LLMBuildConfig(
        Dictionary<string, ModelSetting> AgentSettings,
        PrivateProvider SessionPrivateProvider,
        string? MemoryInstruction);
}
