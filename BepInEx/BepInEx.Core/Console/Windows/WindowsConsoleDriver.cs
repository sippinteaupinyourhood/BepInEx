using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.ConsoleUtil;
using HarmonyLib;
using Microsoft.Win32.SafeHandles;
using MonoMod.Utils;
using UnityInjector.ConsoleUtil;

namespace BepInEx;

internal class WindowsConsoleDriver : IConsoleDriver
{
    
    
    
    
    private static readonly ConstructorInfo FileStreamCtor = new[]
    {
        AccessTools.Constructor(typeof(FileStream), new[] { typeof(SafeFileHandle), typeof(FileAccess) }),
        AccessTools.Constructor(typeof(FileStream), new[] { typeof(IntPtr), typeof(FileAccess) })
    }.FirstOrDefault(m => m != null);

    private readonly Func<int> getWindowHeight = AccessTools
                                                 .PropertyGetter(typeof(Console), nameof(Console.WindowHeight))
                                                 ?.CreateDelegate<Func<int>>();

    private readonly Func<int> getWindowWidth = AccessTools
                                                .PropertyGetter(typeof(Console), nameof(Console.WindowWidth))
                                                ?.CreateDelegate<Func<int>>();

    private bool useManagedEncoder;

    private int ConsoleWidth
    {
        get
        {
            try
            {
                return getWindowWidth?.Invoke() ?? 0;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }

    private int ConsoleHeight
    {
        get
        {
            try
            {
                return getWindowHeight?.Invoke() ?? 0;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }

    public TextWriter StandardOut { get; private set; }
    public TextWriter ConsoleOut { get; private set; }

    public bool ConsoleActive { get; private set; }
    public bool ConsoleIsExternal => true;

    public void Initialize(bool alreadyActive, bool useManagedEncoder)
    {
        ConsoleActive = alreadyActive;
        this.useManagedEncoder = useManagedEncoder;

        if (ConsoleActive)
        {
            
            ConsoleOut = Console.Out;
            StandardOut = new StreamWriter(Console.OpenStandardOutput());
        }
        else
        {
            StandardOut = Console.Out;
        }
    }

    public void CreateConsole(uint codepage)
    {
        ConsoleWindow.Attach();

        
        
        
        
        
        ConsoleEncoding.ConsoleCodePage = codepage;

        
        
        var stdout = GetOutHandle();
        if (stdout == IntPtr.Zero)
        {
            StandardOut = TextWriter.Null;
            ConsoleOut = TextWriter.Null;
            return;
        }

        var originalOutStream = OpenFileStream(stdout);
        StandardOut = new StreamWriter(originalOutStream, Utility.UTF8NoBom)
        {
            AutoFlush = true
        };

        var consoleOutStream = OpenFileStream(ConsoleWindow.ConsoleOutHandle);
        
        ConsoleOut = new StreamWriter(consoleOutStream,
                                      useManagedEncoder ? Utility.UTF8NoBom : ConsoleEncoding.OutputEncoding)
        {
            AutoFlush = true
        };
        ConsoleActive = true;
    }

    public void PreventClose() => ConsoleWindow.PreventClose();

    public void DetachConsole()
    {
        ConsoleWindow.Detach();

        ConsoleOut.Close();
        ConsoleOut = null;

        ConsoleActive = false;
    }

    public void SetConsoleColor(ConsoleColor color)
    {
        SafeConsole.ForegroundColor = color;
        Kon.ForegroundColor = color;
    }

    public void SetConsoleTitle(string title) => ConsoleWindow.Title = title;

    private static Stream OpenFileStream(IntPtr handle)
    {
        if (ReflectionHelper.IsCore)
        {
            var windowsConsoleStreamType = Type.GetType("System.ConsolePal+WindowsConsoleStream, System.Console", true);
            var constructor = AccessTools.Constructor(windowsConsoleStreamType,
                                                      new[] { typeof(IntPtr), typeof(FileAccess), typeof(bool) });
            return (Stream)constructor.Invoke(new object[] { handle, FileAccess.Write, true });
        }

        var fileHandle = new SafeFileHandle(handle, false);
        var ctorParams = AccessTools.ActualParameters(FileStreamCtor,
                                                      new object[]
                                                      {
                                                          fileHandle, fileHandle.DangerousGetHandle(),
                                                          FileAccess.Write
                                                      });
        return (FileStream) Activator.CreateInstance(typeof(FileStream), ctorParams);
    }

    private IntPtr GetOutHandle()
    {
        switch (ConsoleManager.ConfigConsoleOutRedirectType.Value)
        {
            case ConsoleManager.ConsoleOutRedirectType.ConsoleOut:
                return ConsoleWindow.ConsoleOutHandle;
            case ConsoleManager.ConsoleOutRedirectType.StandardOut:
                return ConsoleWindow.OriginalStdoutHandle;
            case ConsoleManager.ConsoleOutRedirectType.Auto:
            default:
                return ConsoleWindow.OriginalStdoutHandle != IntPtr.Zero
                           ? ConsoleWindow.OriginalStdoutHandle
                           : ConsoleWindow.ConsoleOutHandle;
        }
    }
}
