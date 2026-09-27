using System;
using System.IO;

namespace BepInEx.Preloader.Core;





public static class EnvVars
{
    
    
    
    
    public static string DOORSTOP_INVOKE_DLL_PATH { get; private set; }

    
    
    
    public static string DOORSTOP_MANAGED_FOLDER_DIR { get; private set; }

    
    
    
    public static string DOORSTOP_PROCESS_PATH { get; private set; }

    
    
    
    public static string[] DOORSTOP_DLL_SEARCH_DIRS { get; private set; }

    
    
    
    public static string DOORSTOP_MONO_LIB_PATH { get; private set; }

    internal static void LoadVars()
    {
        DOORSTOP_INVOKE_DLL_PATH = Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH");
        DOORSTOP_MANAGED_FOLDER_DIR = Environment.GetEnvironmentVariable("DOORSTOP_MANAGED_FOLDER_DIR");
        DOORSTOP_PROCESS_PATH = Environment.GetEnvironmentVariable("DOORSTOP_PROCESS_PATH");
        DOORSTOP_MONO_LIB_PATH = Environment.GetEnvironmentVariable("DOORSTOP_MONO_LIB_PATH");
        DOORSTOP_DLL_SEARCH_DIRS =
            Environment.GetEnvironmentVariable("DOORSTOP_DLL_SEARCH_DIRS")?.Split(Path.PathSeparator) ??
            new string[0];
    }
}
