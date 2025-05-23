using Volo.Abp.Caching;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;
using XiaoZhi.Net.Domain.Share;

namespace XiaoZhi.Net.Domain
{
    [DependsOn(
        typeof(XiaoZhiNetDomainShareModule),

        typeof(AbpDddDomainModule),
        typeof(AbpCachingModule)
        )]
    public class XiaoZhiNetDomainModule : AbpModule
    {

    }
}
