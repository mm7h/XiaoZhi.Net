using Volo.Abp.Modularity;
using XiaoZhi.Net.Domain.Share;

namespace XiaoZhi.Net.Application.Contracts
{
    [DependsOn(typeof(XiaoZhiNetDomainShareModule))]
    public class XiaoZhiNetApplicationContractsModule : AbpModule
    {

    }
}
