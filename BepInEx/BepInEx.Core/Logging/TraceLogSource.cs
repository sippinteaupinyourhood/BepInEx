using System.Diagnostics;

namespace BepInEx.Logging;





public class TraceLogSource : TraceListener
{
    private static TraceLogSource traceListener;

    
    
    
    protected TraceLogSource()
    {
        LogSource = new ManualLogSource("Trace");
    }

    
    
    
    public static bool IsListening { get; private set; }

    
    
    
    protected ManualLogSource LogSource { get; }

    
    
    
    
    public static ILogSource CreateSource()
    {
        if (traceListener == null)
        {
            traceListener = new TraceLogSource();
            Trace.Listeners.Add(traceListener);
            IsListening = true;
        }

        return traceListener.LogSource;
    }

    
    
    
    
    public override void Write(string message) => LogSource.Log(LogLevel.Info, message);

    
    
    
    
    public override void WriteLine(string message) => LogSource.Log(LogLevel.Info, message);

    
    public override void TraceEvent(TraceEventCache eventCache,
                                    string source,
                                    TraceEventType eventType,
                                    int id,
                                    string format,
                                    params object[] args) =>
        TraceEvent(eventCache, source, eventType, id, string.Format(format, args));

    
    public override void TraceEvent(TraceEventCache eventCache,
                                    string source,
                                    TraceEventType eventType,
                                    int id,
                                    string message)
    {
        var level = eventType switch
        {
            TraceEventType.Critical    => LogLevel.Fatal,
            TraceEventType.Error       => LogLevel.Error,
            TraceEventType.Warning     => LogLevel.Warning,
            TraceEventType.Information => LogLevel.Info,
            _                          => LogLevel.Debug
        };
        LogSource.Log(level, $"{message}".Trim());
    }
}
