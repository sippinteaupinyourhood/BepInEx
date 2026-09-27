using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace BepInEx.Logging;




public class DiskLogListener : ILogListener
{
    public static HashSet<string> BlacklistedSources = new();

    
    
    
    
    
    
    
    
    
    
    
    public DiskLogListener(string localPath,
                           LogLevel displayedLogLevel = LogLevel.Info,
                           bool appendLog = false,
                           bool delayedFlushing = true,
                           int fileLimit = 5)
    {
        DisplayedLogLevel = displayedLogLevel;

        var counter = 1;

        FileStream fileStream;

        while (!Utility.TryOpenFileStream(Path.Combine(Paths.BepInExRootPath, localPath),
                                          appendLog ? FileMode.Append : FileMode.Create, out fileStream,
                                          share: FileShare.Read, access: FileAccess.Write))
        {
            if (counter == fileLimit)
            {
                Logger.Log(LogLevel.Error, "Couldn't open a log file for writing. Skipping log file creation");

                return;
            }

            Logger.Log(LogLevel.Warning, $"Couldn't open log file '{localPath}' for writing, trying another...");

            localPath = $"LogOutput.{counter++}.log";
        }

        LogWriter = TextWriter.Synchronized(new StreamWriter(fileStream, Utility.UTF8NoBom));

        if (delayedFlushing) FlushTimer = new Timer(o => { LogWriter?.Flush(); }, null, 2000, 2000);

        InstantFlushing = !delayedFlushing;
    }

    
    
    
    public LogLevel DisplayedLogLevel { get; }

    
    
    
    public TextWriter LogWriter { get; protected set; }

    
    
    
    private Timer FlushTimer { get; }

    private bool InstantFlushing { get; }

    
    public LogLevel LogLevelFilter => DisplayedLogLevel;

    
    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (LogWriter == null)
            return;

        if (BlacklistedSources.Contains(eventArgs.Source.SourceName))
            return;

        LogWriter.WriteLine(eventArgs.ToString());

        if (InstantFlushing)
            LogWriter.Flush();
    }

    
    public void Dispose()
    {
        FlushTimer?.Dispose();

        try
        {
            LogWriter?.Flush();
            LogWriter?.Dispose();
        }
        catch (ObjectDisposedException) { }
    }

    ~DiskLogListener()
    {
        Dispose();
    }
}
