using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.ComponentModel;
using System.Text;
using Volo.Abp.AspNetCore.Mvc;

namespace XiaoZhi.Net.WebApi.Extensions
{
    public static class SwaggerExtensions
    {
        public static IServiceCollection AddSwaggerGen<Program>(this IServiceCollection services, Action<SwaggerGenOptions>? action = null)
        {

            var serviceProvider = services.BuildServiceProvider();
            var mvcOptions = serviceProvider.GetRequiredService<IOptions<AbpAspNetCoreMvcOptions>>();

            var mvcSettings = mvcOptions.Value.ConventionalControllers.ConventionalControllerSettings.DistinctBy(x => x.RemoteServiceName);


            services.AddAbpSwaggerGen(options =>
            {
                if (action is not null)
                {
                    action.Invoke(options);
                }

                // 配置分组,还需要去重,支持重写,如果外部传入后，将以外部为准
                foreach (var setting in mvcSettings.OrderBy(x => x.RemoteServiceName))
                {
                    if (!options.SwaggerGeneratorOptions.SwaggerDocs.ContainsKey(setting.RemoteServiceName))
                    {
                        options.SwaggerDoc(setting.RemoteServiceName, new OpenApiInfo { Title = setting.RemoteServiceName, Version = "v1" });
                    }
                }

                // 根据分组名称过滤 API 文档
                options.DocInclusionPredicate((docName, apiDesc) =>
                {
                    if (apiDesc.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
                    {
                        var settingOrNull = mvcSettings.Where(x => x.Assembly == controllerActionDescriptor.ControllerTypeInfo.Assembly).FirstOrDefault();
                        if (settingOrNull is not null)
                        {
                            return docName == settingOrNull.RemoteServiceName;
                        }
                    }
                    return false;
                });

                options.CustomSchemaIds(type => type.FullName);
                var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                if (basePath is not null)
                {
                    foreach (var item in Directory.GetFiles(basePath, "*.xml"))
                    {
                        options.IncludeXmlComments(item, true);
                    }
                }

                options.AddSecurityDefinition("JwtBearer", new OpenApiSecurityScheme()
                {
                    Description = "直接输入Token即可",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer"
                });
                var scheme = new OpenApiSecurityScheme()
                {
                    Reference = new OpenApiReference() { Type = ReferenceType.SecurityScheme, Id = "JwtBearer" }
                };
                options.AddSecurityRequirement(new OpenApiSecurityRequirement()
                {
                    [scheme] = new string[0]
                });

                options.OperationFilter<AddRequiredHeaderParameter>();
                options.SchemaFilter<EnumSchemaFilter>();
            });



            return services;
        }

        public static IApplicationBuilder UseSwaggerGen(this IApplicationBuilder app, params SwaggerModel[] swaggerModels)
        {
            var mvcOptions = app.ApplicationServices.GetRequiredService<IOptions<AbpAspNetCoreMvcOptions>>().Value;

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                foreach (var setting in mvcOptions.ConventionalControllers.ConventionalControllerSettings)
                {
                    c.SwaggerEndpoint($"/swagger/{setting.RemoteServiceName}/swagger.json", setting.RemoteServiceName);
                }
                if (mvcOptions.ConventionalControllers.ConventionalControllerSettings.Count == 0 && swaggerModels.Length == 0)
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "XiaoZhi.Net.WebApi");
                }
                else
                {
                    foreach (var k in swaggerModels)
                    {
                        c.SwaggerEndpoint(k.Url, k.Name);
                    }
                }

            });
            return app;
        }
    }


    /// <summary>
    /// Swagger文档枚举字段显示枚举属性和枚举值,以及枚举描述
    /// </summary>
    file class EnumSchemaFilter : ISchemaFilter
    {
        /// <summary>
        /// 实现接口
        /// </summary>
        /// <param name="model"></param>
        /// <param name="context"></param>

        public void Apply(OpenApiSchema model, SchemaFilterContext context)
        {
            if (context.Type.IsEnum)
            {
                model.Enum.Clear();
                model.Type = "string";
                model.Format = null;


                StringBuilder stringBuilder = new StringBuilder();
                Enum.GetNames(context.Type)
                    .ToList()
                    .ForEach(name =>
                    {
                        Enum e = (Enum)Enum.Parse(context.Type, name);
                        var descrptionOrNull = GetEnumDescription(e);
                        model.Enum.Add(new OpenApiString(name));
                        stringBuilder.Append($"【枚举：{name}{(descrptionOrNull is null ? string.Empty : $"({descrptionOrNull})")}={Convert.ToInt64(Enum.Parse(context.Type, name))}】<br />");
                    });
                model.Description = stringBuilder.ToString();
            }
        }

        private static string? GetEnumDescription(Enum value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attributes = (DescriptionAttribute[])fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Length > 0 ? attributes[0].Description : null;
        }

    }


    file class AddRequiredHeaderParameter : IOperationFilter
    {
        public static string HeaderKey { get; set; } = "__tenant";
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            //if (operation.Parameters == null)
            //    operation.Parameters = new List<OpenApiParameter>();

            //operation.Parameters.Add(new OpenApiParameter
            //{
            //    Name = HeaderKey,
            //    In = ParameterLocation.Header,
            //    Required = false,
            //    AllowEmptyValue = true,
            //    Description = "租户id或者租户名称（可空为默认租户）"
            //});
        }
    }

    public class SwaggerModel
    {
        public SwaggerModel(string name)
        {
            this.Name = name;
            this.Url = "/swagger/v1/swagger.json";
        }
        public SwaggerModel(string url, string name)
        {
            this.Url = url;
            this.Name = name;
        }
        public string Url { get; set; }
        public string Name { get; set; }
    }
}
