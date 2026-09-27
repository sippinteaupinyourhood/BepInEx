using System;
using System.IO;
using BepInEx.Preloader.Core;
using BepInEx.Unity.Mono.Preloader;


namespace Doorstop;

internal static class Entrypoint
{
    
    
    
    public static void Start()
    {
        
        var silentExceptionLog = $"preloader_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";

        try
        {
            EnvVars.LoadVars();

            var gamePath = Path.GetDirectoryName(EnvVars.DOORSTOP_PROCESS_PATH) ?? ".";
            silentExceptionLog = Path.Combine(gamePath, silentExceptionLog);

            
            
            typeof(Entrypoint).Assembly.GetType($"BepInEx.Unity.Mono.Preloader.{nameof(UnityPreloaderRunner)}")
                              ?.GetMethod(nameof(UnityPreloaderRunner.PreloaderPreMain))
                              ?.Invoke(null, null);
        }
        catch (Exception ex)
        {
            File.WriteAllText(silentExceptionLog, ex.ToString());
        }
    }
}
