using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Preloader.Core;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils;
using MonoMod.Utils;


namespace Doorstop;

internal static class Entrypoint
{
    
    
    
    public static void Start()
    {
        
        var silentExceptionLog = Environment.GetEnvironmentVariable("BEPINEX_PRELOADER_LOG") ??
                                 $"preloader_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";
        Mutex mutex = null;

        try
        {
            EnvVars.LoadVars();

            silentExceptionLog =
                Path.Combine(Path.GetDirectoryName(EnvVars.DOORSTOP_PROCESS_PATH), silentExceptionLog);

            var mutexId = Utility.HashStrings(Process.GetCurrentProcess().ProcessName, EnvVars.DOORSTOP_PROCESS_PATH,
                                              typeof(Entrypoint).FullName);

            mutex = new Mutex(false, $"Global\\{mutexId}");
            mutex.WaitOne();

            UnityPreloaderRunner.PreloaderMain();
        }
        catch (Exception ex)
        {
            File.WriteAllText(silentExceptionLog, ex.ToString());

            try
            {
                if (PlatformHelper.Is(Platform.Windows))
                {
                    MessageBox.Show("Failed to start BepInEx", "BepInEx");
                }
                else if (NotifySend.IsSupported)
                {
                    NotifySend.Send("Failed to start BepInEx", "Check logs for details");
                }
                else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BEPINEX_FAIL_FAST")))
                {
                    
                    return;
                }
            }
            catch (Exception)
            {
                
            }

            Environment.Exit(1);
        }
        finally
        {
            mutex?.ReleaseMutex();
        }
    }
}
