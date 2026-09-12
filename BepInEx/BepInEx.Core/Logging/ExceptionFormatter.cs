using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace BepInEx.Logging;

internal static class ExceptionFormatter
{
    internal static string Format(Exception exception)
    {
        var result = new StringBuilder();
        Append(result, exception, new List<Exception>(), 0);
        return result.ToString();
    }

    private static void Append(StringBuilder result, Exception exception, List<Exception> visited, int depth)
    {
        if (exception == null)
            return;
        if (depth >= 16 || visited.Exists(item => ReferenceEquals(item, exception)))
        {
            result.AppendLine("[Exception chain truncated]");
            return;
        }

        visited.Add(exception);
        try
        {
            result.AppendLine(exception.ToString());
        }
        catch (Exception formattingError)
        {
            result.Append(exception.GetType().FullName).Append(": ");
            try { result.AppendLine(exception.Message); }
            catch { result.AppendLine("[Message unavailable]"); }
            result.Append("[Exception formatting failed: ").Append(formattingError.GetType().FullName);
            try { result.Append(": ").Append(formattingError.Message); }
            catch { }
            result.AppendLine("]");
            Append(result, exception.InnerException, visited, depth + 1);
        }

        if (exception is ReflectionTypeLoadException typeLoad && typeLoad.LoaderExceptions != null)
            foreach (var loaderException in typeLoad.LoaderExceptions)
                Append(result, loaderException, visited, depth + 1);
    }
}
