using System;

namespace BepInEx.Logging;




public interface ILogListener : IDisposable
{
    
    
    
    
    
    
    
    
    
    
    LogLevel LogLevelFilter { get; }

    
    
    
    
    
    void LogEvent(object sender, LogEventArgs eventArgs);
}
