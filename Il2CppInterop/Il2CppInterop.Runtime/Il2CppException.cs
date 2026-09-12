using System;
using System.Text;

namespace Il2CppInterop.Runtime;

public class Il2CppException : Exception
{
    [ThreadStatic] private static byte[] ourMessageBytes;

    public static Func<IntPtr, string> ParseMessageHook;

    public Il2CppException(IntPtr exception) : base(BuildMessage(exception))
    {
    }

    private static unsafe string BuildMessage(IntPtr exception)
    {
        if (ParseMessageHook != null) return ParseMessageHook(exception);
        string builtMessage;
        try
        {
            ourMessageBytes ??= new byte[65536];
            Array.Clear(ourMessageBytes, 0, ourMessageBytes.Length);
            fixed (byte* message = ourMessageBytes)
            {
                IL2CPP.il2cpp_format_exception(exception, message, ourMessageBytes.Length);
            }

            var terminator = Array.IndexOf(ourMessageBytes, (byte)0);
            if (terminator < 0) terminator = ourMessageBytes.Length;
            builtMessage = Encoding.UTF8.GetString(ourMessageBytes, 0, terminator);
        }
        catch (EntryPointNotFoundException)
        {
            builtMessage = $"IL2CPP exception at 0x{exception.ToInt64():X}";
        }

        try
        {
            Il2CppSystem.Exception il2cppException = new(exception);
            return builtMessage + "\n" +
                   "--- BEGIN IL2CPP STACK TRACE ---\n" +
                   $"{il2cppException.ToString(false, true)}\n" +
                   "--- END IL2CPP STACK TRACE ---\n";
        }
        catch (Exception formattingFailure)
        {
            return builtMessage + $"\n[IL2CPP stack formatting unavailable: {formattingFailure.GetType().Name}]";
        }
    }

    public static void RaiseExceptionIfNecessary(IntPtr returnedException)
    {
        if (returnedException == IntPtr.Zero) return;
        throw new Il2CppException(returnedException);
    }
}
