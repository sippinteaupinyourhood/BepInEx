using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace BepInEx.Preloader.Core.Logging;




public class PreloaderConsoleListener : ILogListener
{
    private static readonly ConfigEntry<LogLevel> ConfigConsoleDisplayedLevel = ConfigFile.CoreConfig.Bind(
     "Logging.Console", "LogLevels",
     LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info,
     "Which log levels to show in the console output.");

    
    
    
    public static List<LogEventArgs> LogEvents { get; } = new();

    
    public LogLevel LogLevelFilter => ConfigConsoleDisplayedLevel.Value;

    
    public void LogEvent(object sender, LogEventArgs eventArgs) => LogEvents.Add(eventArgs);

    
    public void Dispose() { }
}
