using System;
using System.Runtime.CompilerServices;
using BepInEx.Core.Logging.Interpolation;

namespace BepInEx.Logging;




public class ManualLogSource : ILogSource
{
    
    
    
    
    public ManualLogSource(string sourceName)
    {
        SourceName = sourceName;
    }

    
    public string SourceName { get; }

    
    public event EventHandler<LogEventArgs> LogEvent;

    
    public void Dispose() { }

    
    
    
    
    
    public void Log(LogLevel level, object data) => LogEvent?.Invoke(this, new LogEventArgs(data, level, this));

    
    
    
    
    
    public void Log(LogLevel level,
                    [InterpolatedStringHandlerArgument("level")]
                    BepInExLogInterpolatedStringHandler logHandler)
    {
        if (logHandler.Enabled)
            LogEvent?.Invoke(this, new LogEventArgs(logHandler.ToString(), level, this));
    }

    
    
    
    
    public void LogFatal(object data) => Log(LogLevel.Fatal, data);

    
    
    
    
    public void LogFatal(BepInExFatalLogInterpolatedStringHandler logHandler) => Log(LogLevel.Fatal, logHandler);

    
    
    
    
    public void LogError(object data) => Log(LogLevel.Error, data);

    
    
    
    
    public void LogError(BepInExErrorLogInterpolatedStringHandler logHandler) => Log(LogLevel.Error, logHandler);

    
    
    
    
    public void LogWarning(object data) => Log(LogLevel.Warning, data);

    
    
    
    
    public void LogWarning(BepInExWarningLogInterpolatedStringHandler logHandler) => Log(LogLevel.Warning, logHandler);

    
    
    
    
    public void LogMessage(object data) => Log(LogLevel.Message, data);

    
    
    
    
    public void LogMessage(BepInExMessageLogInterpolatedStringHandler logHandler) => Log(LogLevel.Message, logHandler);

    
    
    
    
    public void LogInfo(object data) => Log(LogLevel.Info, data);

    
    
    
    
    public void LogInfo(BepInExInfoLogInterpolatedStringHandler logHandler) => Log(LogLevel.Info, logHandler);

    
    
    
    
    public void LogDebug(object data) => Log(LogLevel.Debug, data);

    
    
    
    
    public void LogDebug(BepInExDebugLogInterpolatedStringHandler logHandler) => Log(LogLevel.Debug, logHandler);
}
