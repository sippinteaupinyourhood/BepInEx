using System;
using BepInEx.Configuration;

namespace BepInEx.Logging;




public class ConsoleLogListener : ILogListener
{
    protected static readonly ConfigEntry<LogLevel> ConfigConsoleDisplayedLevel = ConfigFile.CoreConfig.Bind(
     "Logging.Console", "LogLevels",
     LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info,
     "Only displays the specified log levels in the console output.");

    
    public LogLevel LogLevelFilter => ConfigConsoleDisplayedLevel.Value;

    
    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        ConsoleManager.SetConsoleColor(eventArgs.Level.GetConsoleColor());
        ConsoleManager.ConsoleStream?.Write(eventArgs.ToStringLine());
        ConsoleManager.SetConsoleColor(ConsoleColor.Gray);
    }

    
    public void Dispose() { }
}
