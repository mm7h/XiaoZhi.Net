using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XiaoZhi.Net.Server.Abstractions;
using XiaoZhi.Net.Server.Abstractions.Mcp;
using XiaoZhi.Net.Server.Vision;
using XiaoZhi.Net.Server.Vision.Abstractions;
using XiaoZhi.Net.Server.Vision.Abstractions.Common;
using XiaoZhi.Net.Server.Vision.Services;

#pragma warning disable IDE0130 // Expose optional module extensions beside the core server builder API.
namespace XiaoZhi.Net.Server.Vision.Extensions;

/// <summary>
/// 为服务器构建器提供可选 Vision 模块的注册扩展。
/// </summary>
public static class VisionServerBuilderExtensions
{
    private const string RegistrationKey = "XiaoZhi.Net.Server.Vision.Registered";

    /// <summary>
    /// 添加可选的 Vision HTTP 接口、设备 MCP 能力和 OpenAI 兼容视觉分析器。
    /// </summary>
    public static IServerBuilder WithVision(this IServerBuilder builder, Action<VisionServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.HostBuilder.Properties.ContainsKey(RegistrationKey))
        {
            throw new InvalidOperationException("WithVision() can only be called once for a server builder.");
        }

        VisionServerOptions options = new();
        configure?.Invoke(options);
        Validate(options);
        builder.HostBuilder.Properties[RegistrationKey] = true;

        builder.HostBuilder.ConfigureServices((_, services) =>
        {
            services.AddSingleton(options);
            services.AddSingleton<VisionUploadTokenService>();
            services.AddSingleton<IDeviceMcpCapabilityContributor, VisionMcpCapabilityContributor>();
            services.AddSingleton<IVisionAnalyzer, OpenAICompatibleVisionAnalyzer>();
        });

        builder.HostBuilder.ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseUrls(options.ListenUrl);
            webBuilder.ConfigureServices(services =>
            {
                services.AddRouting();
                services.Configure<FormOptions>(formOptions =>
                {
                    formOptions.MultipartBodyLengthLimit = options.MaxImageBytes + 64 * 1024;
                });
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet(options.ExplainPath, VisionEndpoint.HandleGetAsync);
                    endpoints.MapPost(options.ExplainPath, VisionEndpoint.HandlePostAsync);
                    endpoints.MapMethods(options.ExplainPath, ["OPTIONS"], VisionEndpoint.HandleOptionsAsync);
                });
            });
        });

        return builder;
    }

    private static void Validate(VisionServerOptions options)
    {
        ValidateHttpUrl(options.ListenUrl, "Vision ListenUrl");
        ValidateHttpUrl(options.PublicExplainUrl, "Vision PublicExplainUrl");
        ValidateHttpUrl(options.Model.Endpoint, "Vision model Endpoint");

        if (string.IsNullOrWhiteSpace(options.ExplainPath) || !options.ExplainPath.StartsWith('/'))
        {
            throw new ArgumentException("Vision ExplainPath must start with '/'.", nameof(options));
        }

        if (Encoding.UTF8.GetByteCount(options.UploadTokenSigningKey) < 32)
        {
            throw new ArgumentException("Vision UploadTokenSigningKey must be at least 32 bytes.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Model.ApiKey) || string.IsNullOrWhiteSpace(options.Model.ModelName))
        {
            throw new ArgumentException("Vision model ApiKey and ModelName are required.", nameof(options));
        }

        if (options.MaxImageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Vision MaxImageBytes must be greater than zero.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Vision RequestTimeout must be greater than zero.");
        }
    }

    private static void ValidateHttpUrl(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"{name} must be an absolute HTTP or HTTPS URL.", nameof(value));
        }
    }
}
#pragma warning restore IDE0130
