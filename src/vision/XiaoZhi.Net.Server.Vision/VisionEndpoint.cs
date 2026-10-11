using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Vision.Abstractions;
using XiaoZhi.Net.Server.Vision.Abstractions.Common;
using XiaoZhi.Net.Server.Vision.Services;

namespace XiaoZhi.Net.Server.Vision;

/// <summary>
/// 处理设备提交图片并返回视觉描述的 HTTP 接口。
/// </summary>
internal static class VisionEndpoint
{
    public static Task HandleGetAsync(HttpContext context)
    {
        AddCorsHeaders(context.Response);
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync("MCP Vision endpoint is running.", context.RequestAborted);
    }

    public static Task HandleOptionsAsync(HttpContext context)
    {
        AddCorsHeaders(context.Response);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }

    public static async Task HandlePostAsync(HttpContext context)
    {
        ILoggerFactory loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
        ILogger logger = loggerFactory.CreateLogger("XiaoZhi.Net.Server.Vision.Endpoint");
        IResult result;

        try
        {
            result = await ProcessPostAsync(context);
        }
        catch (BadHttpRequestException ex)
        {
            int statusCode = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest;
            result = Error(statusCode, ex.Message);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            result = Error(StatusCodes.Status504GatewayTimeout, "Vision request timed out.");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, Lang.VisionEndpoint_HandlePostAsync_ProcessFailed);
            result = Error(StatusCodes.Status502BadGateway, "Vision model request failed.");
        }

        AddCorsHeaders(context.Response);
        await result.ExecuteAsync(context);
    }

    private static async Task<IResult> ProcessPostAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
        {
            return Error(StatusCodes.Status400BadRequest, "The request must use multipart/form-data.");
        }

        string? accessToken = GetBearerToken(context.Request.Headers.Authorization.ToString());
        string deviceId = context.Request.Headers["Device-Id"].ToString();
        VisionUploadTokenService tokens = context.RequestServices.GetRequiredService<VisionUploadTokenService>();
        if (accessToken is null || !tokens.IsValid(accessToken, deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "Invalid vision access token.");
        }

        VisionServerOptions options = context.RequestServices.GetRequiredService<VisionServerOptions>();
        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
        string question = form["question"].ToString();
        IFormFile? file = form.Files.GetFile("file");

        if (string.IsNullOrWhiteSpace(question))
        {
            return Error(StatusCodes.Status400BadRequest, "The question field is required.");
        }

        if (file is null || file.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, "The file field is required.");
        }

        if (file.Length > options.MaxImageBytes)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, "The uploaded image is too large.");
        }

        if (!string.Equals(file.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return Error(StatusCodes.Status400BadRequest, "Only image/jpeg uploads are supported.");
        }

        byte[] image = await ReadImageAsync(file, options.MaxImageBytes, context.RequestAborted);
        if (!HasJpegSignature(image))
        {
            return Error(StatusCodes.Status400BadRequest, "The uploaded file is not a valid JPEG image.");
        }

        IVisionAnalyzer analyzer = context.RequestServices.GetRequiredService<IVisionAnalyzer>();
        using CancellationTokenSource timeout = new(options.RequestTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            timeout.Token);

        string response = await analyzer.AnalyzeAsync(
            new VisionAnalysisRequest(question, image, "image/jpeg"),
            linked.Token);

        return Results.Ok(new
        {
            success = true,
            action = "RESPONSE",
            response
        });
    }

    private static async Task<byte[]> ReadImageAsync(
        IFormFile file,
        long maxImageBytes,
        CancellationToken cancellationToken)
    {
        await using Stream source = file.OpenReadStream();
        int capacity = (int)Math.Min(file.Length, Math.Min(maxImageBytes, int.MaxValue));
        using MemoryStream destination = new(capacity);
        await source.CopyToAsync(destination, cancellationToken);

        if (destination.Length > maxImageBytes)
        {
            throw new BadHttpRequestException("The uploaded image is too large.");
        }

        return destination.ToArray();
    }

    private static bool HasJpegSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3
        && bytes[0] == 0xFF
        && bytes[1] == 0xD8
        && bytes[2] == 0xFF;

    private static string? GetBearerToken(string authorization)
    {
        const string Scheme = "Bearer ";
        if (!authorization.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string token = authorization[Scheme.Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static void AddCorsHeaders(HttpResponse response)
    {
        response.Headers.AccessControlAllowOrigin = "*";
        response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
        response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type, Device-Id, Client-Id";
    }

    private static IResult Error(int statusCode, string message) =>
        Results.Json(new { success = false, message }, statusCode: statusCode);
}
