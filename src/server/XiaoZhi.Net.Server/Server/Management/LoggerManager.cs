using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;

namespace XiaoZhi.Net.Server.Management
{
    internal class LoggerManager
    {
        public static IHostBuilder RegisterServices(IHostBuilder builder, XiaoZhiConfig config)
        {
            return builder.ConfigureLogging((context, loggerBuilder) =>
            {
                LogSetting logSetting = config.LogSetting;
                LoggingLevelSwitch levelSwitch = new LoggingLevelSwitch();
                levelSwitch.MinimumLevel = ConvertLogLevel(logSetting.LogLevel);

                LoggerConfiguration loggerConfig = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(levelSwitch)
#if DEBUG
                    .MinimumLevel.Override("Microsoft.SemanticKernel", LogEventLevel.Warning)
#else
                    // 仅排除 SDK 辅助模块，保留核心服务和接入方日志；在 Serilog 层过滤以避免提供器规则覆盖。
                    .Filter.ByExcluding(Matching.FromSource("XiaoZhi.Net.Server.Media"))
                    .Filter.ByExcluding(Matching.FromSource("XiaoZhi.Net.Server.Abstractions"))
                    .Filter.ByExcluding(Matching.FromSource("XiaoZhi.Net.Server.RAG"))
                    .Filter.ByExcluding(Matching.FromSource("XiaoZhi.Net.Server.Vision"))
                    .Filter.ByExcluding(Matching.FromSource("XiaoZhi.Net.Server.I18n"))
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
