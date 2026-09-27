using System;

namespace BepInEx.Logging;




public class LogEventArgs : EventArgs
{
    
    
    
    
    
    
    public LogEventArgs(object data, LogLevel level, ILogSource source)
    {
        Data = data;
        Level = level;
        Source = source;
    }

    
    
    
    public object Data { get; }

    
    
    
    public LogLevel Level { get; }

    
    
    
    public ILogSource Source { get; }

    
    public override string ToString() => $"[{Level,-7}:{Source.SourceName,10}] {Data}";

    
    
    
    
    public string ToStringLine() => $"[{Level,-7}:{Source.SourceName,10}] {Data}{Environment.NewLine}";
}
