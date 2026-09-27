using System;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Logging;

namespace BepInEx.Core.Logging.Interpolation;










[InterpolatedStringHandler]
public class BepInExLogInterpolatedStringHandler
{
    
    private const int GUESSED_LENGTH_PER_HOLE = 11;

    
    private readonly StringBuilder sb;

    
    
    
    
    
    
    
    public BepInExLogInterpolatedStringHandler(int literalLength,
                                               int formattedCount,
                                               LogLevel logLevel,
                                               out bool isEnabled)
    {
        Enabled = (logLevel & Logger.ListenedLogLevels) != LogLevel.None;
        isEnabled = Enabled;
        sb = Enabled ? new StringBuilder(literalLength + formattedCount * GUESSED_LENGTH_PER_HOLE) : null;
    }

    
    
    
    public bool Enabled { get; }

    
    
    
    
    public void AppendLiteral(string s)
    {
        if (!Enabled)
            return;
        sb.Append(s);
    }

    
    
    
    
    
    public void AppendFormatted<T>(T t)
    {
        if (!Enabled)
            return;

        sb.Append(t);
    }

    
    
    
    
    
    
    public void AppendFormatted<T>(T t, string format) where T : IFormattable
    {
        if (!Enabled)
            return;

        sb.Append(t?.ToString(format, null));
    }

    
    
    
    
    
    public void AppendFormatted(IntPtr t, string format)
    {
        if (!Enabled)
            return;

        sb.Append(t.ToString(format));
    }

    
    public override string ToString() => sb?.ToString() ?? string.Empty;
}


[InterpolatedStringHandler]
public class BepInExFatalLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
{
    
    public BepInExFatalLogInterpolatedStringHandler(int literalLength,
                                                    int formattedCount,
                                                    out bool isEnabled) : base(literalLength, formattedCount,
                                                                               LogLevel.Fatal, out isEnabled) { }
}


[InterpolatedStringHandler]
public class BepInExErrorLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
{
    
    public BepInExErrorLogInterpolatedStringHandler(int literalLength,
                                                    int formattedCount,
                                                    out bool isEnabled) : base(literalLength, formattedCount,
                                                                               LogLevel.Error, out isEnabled) { }
}


[InterpolatedStringHandler]
public class BepInExWarningLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
{
    
    public BepInExWarningLogInterpolatedStringHandler(int literalLength,
                                                      int formattedCount,
                                                      out bool isEnabled) : base(literalLength, formattedCount,
        LogLevel.Warning, out isEnabled) { }
}


[InterpolatedStringHandler]
public class BepInExMessageLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
{
    
    public BepInExMessageLogInterpolatedStringHandler(int literalLength,
                                                      int formattedCount,
                                                      out bool isEnabled) : base(literalLength, formattedCount,
        LogLevel.Message, out isEnabled) { }
}


[InterpolatedStringHandler]
public class BepInExInfoLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
{
    
    public BepInExInfoLogInterpolatedStringHandler(int literalLength,
                                                   int formattedCount,
                                                   out bool isEnabled) : base(literalLength, formattedCount,
                                                                              LogLevel.Info, out isEnabled) { }
}


[InterpolatedStringHandler]
public class BepInExDebugLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
{
    
    public BepInExDebugLogInterpolatedStringHandler(int literalLength,
                                                    int formattedCount,
                                                    out bool isEnabled) : base(literalLength, formattedCount,
                                                                               LogLevel.Debug, out isEnabled) { }
}
