using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace XiaoZhi.Net.Server.Management
{
    internal class LoggerManager
    {

        public LoggerManager()
        {
        }
        public static void RegisterServices(IServiceCollection services, XiaoZhiConfig config)
        {
            LogSetting logSetting = config.LogSetting;
            LoggingLevelSwitch levelSwitch = new LoggingLevelSwitch();
            levelSwitch.MinimumLevel = ConvertLogLevel(logSetting.LogLevel);

            LoggerConfiguration loggerConfig = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Async(a => a.File
                (
                    path: logSetting.LogFilePath,
                    outputTemplate: logSetting.OutputTemplate,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: logSetting.RetainedFileCountLimit
                ))
                .WriteTo.Async(a => a.Console(
                    outputTemplate: logSetting.OutputTemplate,
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code,
                    applyThemeToRedirectedOutput: true
                ));
            Log.Logger = loggerConfig.CreateLogger();
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders(); // 清除默认日志提供程序
                loggingBuilder.AddSerilog(dispose: true); // 添加 Serilog 提供程序
            });
            services.AddSingleton(Log.Logger);
        }

        private static Serilog.Events.LogEventLevel ConvertLogLevel(string logLevel)
        {

            return logLevel.ToUpper() switch
            {
                "VERB" => Serilog.Events.LogEventLevel.Verbose,
                "DEBUG" => Serilog.Events.LogEventLevel.Debug,
                "INFO" => Serilog.Events.LogEventLevel.Information,
                "WARN" => Serilog.Events.LogEventLevel.Warning,
                "ERROR" => Serilog.Events.LogEventLevel.Error,
                "FATAL" => Serilog.Events.LogEventLevel.Fatal,
                _ => Serilog.Events.LogEventLevel.Information,
            };
        }
    }
}
