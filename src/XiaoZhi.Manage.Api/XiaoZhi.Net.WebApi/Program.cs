using Serilog;
using Serilog.Events;
using XiaoZhi.Net.WebApi;

Log.Logger = new LoggerConfiguration()
.MinimumLevel.Debug()
.MinimumLevel.Override("Microsoft", LogEventLevel.Information)
.MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Error)
.Enrich.FromLogContext()
.WriteTo.Async(c => c.File("logs/all/log-.txt", rollingInterval: RollingInterval.Day, restrictedToMinimumLevel: LogEventLevel.Debug))
.WriteTo.Async(c => c.File("logs/error/errorlog-.txt", rollingInterval: RollingInterval.Day, restrictedToMinimumLevel: LogEventLevel.Error))
.WriteTo.Async(c => c.Console())
.CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls(builder.Configuration["App:SelfUrl"]);
    builder.Host.UseAutofac();
    builder.Host.UseSerilog(Log.Logger);
    await builder.Services.AddApplicationAsync<XiaoZhiNetWebApiModule>();
    var app = builder.Build();

    await app.InitializeApplicationAsync();
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "error, exit out");
}
finally
{
    Log.CloseAndFlush();
}