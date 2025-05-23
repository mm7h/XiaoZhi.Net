using Microsoft.AspNetCore.Cors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Volo.Abp;
using Volo.Abp.AspNetCore.Authentication.JwtBearer;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Modularity;
using XiaoZhi.Net.Application;
using XiaoZhi.Net.WebApi.Extensions;

namespace XiaoZhi.Net.WebApi
{
    [DependsOn(
        typeof(XiaoZhiNetApplicationModule),

        typeof(AbpAspNetCoreMvcModule),
        typeof(AbpAspNetCoreAuthenticationJwtBearerModule),
        typeof(AbpAspNetCoreSerilogModule)
        )]
    public class XiaoZhiNetWebApiModule : AbpModule
    {
        private const string DefaultCorsPolicyName = "Default";
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            var configuration = context.Services.GetConfiguration();
            var host = context.Services.GetHostingEnvironment();
            var service = context.Services;

            //动态Api
            Configure<AbpAspNetCoreMvcOptions>(options =>
            {
                options.ConventionalControllers.Create(typeof(XiaoZhiNetApplicationModule).Assembly, options => options.RemoteServiceName = "default");
                //统一前缀
                options.ConventionalControllers.ConventionalControllerSettings.ForEach(x => x.RootPath = "api/app");
            });

            //Swagger
            context.Services.AddSwaggerGen<XiaoZhiNetWebApiModule>(options =>
            {
                options.SwaggerDoc("default", new OpenApiInfo { Title = "XiaoZhi.Net.WebApi", Version = "v1", Description = "小智后台管理服务" });
            });

            //跨域
            context.Services.AddCors(options =>
            {
                options.AddPolicy(DefaultCorsPolicyName, builder =>
                {
                    builder
                        .WithOrigins(
                            configuration["App:CorsOrigins"]!
                                .Split(";", StringSplitOptions.RemoveEmptyEntries)
                                .Select(o => o.RemovePostFix("/"))
                                .ToArray()
                        )
                        .WithAbpExposedHeaders()
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var service = context.ServiceProvider;

            var env = context.GetEnvironment();
            var app = context.GetApplicationBuilder();

            app.UseRouting();

            //跨域
            app.UseCors(DefaultCorsPolicyName);

            //鉴权
            app.UseAuthentication();

            //swagger
            app.UseSwaggerGen();

            //授权
            app.UseAuthorization();

            //日志记录
            app.UseAbpSerilogEnrichers();

            //终节点
            app.UseConfiguredEndpoints();
        }
    }
}
