using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

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
#if DEBUG
                    .MinimumLevel.Override("Microsoft.SemanticKernel", LogEventLevel.Warning)
#endif
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

        private static LogEventLevel ConvertLogLevel(string logLevel)
        {

            return logLevel.ToUpper() switch
            {
                "VERB" => LogEventLevel.Verbose,
                "DEBUG" => LogEventLevel.Debug,
                "INFO" => LogEventLevel.Information,
                "WARN" => LogEventLevel.Warning,
                "ERROR" => LogEventLevel.Error,
                "FATAL" => LogEventLevel.Fatal,
                _ => LogEventLevel.Information,
            };
        }
    }
}
