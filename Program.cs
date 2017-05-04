using System;

namespace SerilogWithFiltering
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.Extensions.Primitives;

    using Serilog;
    using Serilog.Extensions.Logging;

    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class Program
    {
        public static void Main(string[] args)
        {
            var cb = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "settings.json"), true, true);

            var cfg = cb.Build();
            var logCfg = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.LiterateConsole(
                outputTemplate:"[{Timestamp:HH\\:mm\\:ss} {Level:u3}]({SourceContext}) {Message}{NewLine}{Exception}"
                );
            var lf = new LoggerFactory();
            lf.AddProvider(new FilteredProvider(new SerilogLoggerProvider(logCfg.CreateLogger()), cfg.GetSection("Logging")));

            while (true)
            {
                var l = lf.CreateLogger(Console.ReadLine());
                
                l.LogTrace("Trace: {currentValue}", 1);
                l.LogDebug("Debug: {@json}",new {a = 1, b = "2"});
                l.LogInformation("Information");
                l.LogWarning("warning");
                l.LogError("Error");
                l.LogCritical("Critical");
            }
        }
    }

    public class FilteredProvider : ILoggerProvider
    {
        private readonly ILoggerProvider inner;

        private readonly IConfiguration configuration;

        public FilteredProvider(ILoggerProvider inner, IConfiguration configuration)
        {
            this.inner = inner;
            this.configuration = configuration;
        }

        public void Dispose()
        {
            this.inner.Dispose();
        }

        public ILogger CreateLogger(string categoryName)
        {
            var cfg = this.configuration.GetSection(categoryName);

            return new InnerLogger(this.inner.CreateLogger(categoryName), cfg);
        }

        private class InnerLogger : ILogger
        {
            private readonly ILogger inner;

            private HashSet<LogLevel> enabledLevels;
            
            public InnerLogger(ILogger inner, IConfigurationSection cfg)
            {
                this.inner = inner;
                ChangeToken.OnChange(cfg.GetReloadToken, this.UpdateLogLevel, cfg);
                this.UpdateLogLevel(cfg);
            }

            private void UpdateLogLevel(IConfigurationSection cfg)
            {
                var levels = new HashSet<LogLevel>();
                if (!cfg.GetValue("enabled", true))
                {
                    this.enabledLevels = levels;
                    return;
                }

                LogLevel level;

                if (cfg.Value != null || cfg["minimumLevel"] != null)
                {
                    level = (LogLevel)Enum.Parse(typeof(LogLevel), cfg.Value ?? cfg["minimumLevel"]);
                    levels = FromLogLevel(level);
                }
                else
                {
                    var configuredLevels = cfg.GetSection("levels").Get<LogLevel[]>();
                    if (configuredLevels == null)
                    {
                        levels = FromLogLevel(LogLevel.Trace);
                    }
                    else
                    {
                        levels = new HashSet<LogLevel>(configuredLevels);
                    }
                }

                this.enabledLevels = levels;
            }

            private static HashSet<LogLevel> FromLogLevel(LogLevel level)
            {
                var levels = new HashSet<LogLevel>();
                for (var i = 0; i < (int)LogLevel.None; i++)
                {
                    if (i >= (int)level)
                    {
                        levels.Add((LogLevel)i);
                    }
                }

                return levels;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                var enabled = this.enabledLevels.Contains(logLevel);
                if (!enabled)
                {
                    return;
                }

                this.inner.Log(logLevel, eventId, state, exception, formatter);
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public IDisposable BeginScope<TState>(TState state)
            {
                return this.inner.BeginScope(state);
            }
        }
    }
}
