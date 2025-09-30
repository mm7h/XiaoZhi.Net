using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace XiaoZhi.Net.Server.Management
{
    internal class LoggerManager
    {
        public static IHostBuilder RegisterServices(IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureLogging((context, loggerBuilder) =>
            {
#if !DEBUG
                loggerBuilder.AddFilter("XiaoZhi.Net.Server.Media", LogLevel.None);
                loggerBuilder.AddFilter("XiaoZhi.Net.Server.Media.Abstractions", LogLevel.None);
#endif
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

                loggerBuilder.ClearProviders();
                loggerBuilder.AddSerilog(Log.Logger, dispose: true);
            });
            
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
