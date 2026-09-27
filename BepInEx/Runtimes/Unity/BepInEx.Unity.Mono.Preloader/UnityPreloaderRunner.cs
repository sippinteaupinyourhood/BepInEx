using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Preloader.Core;
using BepInEx.Preloader.RuntimeFixes;
using BepInEx.Unity.Mono.Preloader.RuntimeFixes;
using BepInEx.Unity.Mono.Preloader.Utils;

namespace BepInEx.Unity.Mono.Preloader;

internal static class UnityPreloaderRunner
{
    
    
    
    
    
    
    private static readonly string[] CriticalAssemblies =
    {
        "Mono.Cecil.dll",
        "Mono.Cecil.Mdb.dll",
        "Mono.Cecil.Pdb.dll",
        "Mono.Cecil.Rocks.dll",
        "MonoMod.Utils.dll",
        "MonoMod.RuntimeDetour.dll",
        "0Harmony.dll"
    };

    private static void LoadCriticalAssemblies()
    {
        foreach (var criticalAssembly in CriticalAssemblies)
            try
            {
                MonoAssemblyHelper.Load(Path.Combine(Paths.BepInExAssemblyDirectory, criticalAssembly));
            }
            catch (Exception)
            {
                
                
            }
    }

    public static void PreloaderPreMain()
    {
        PlatformUtils.SetPlatform();

        var bepinPath = Utility.ParentDirectory(Path.GetFullPath(EnvVars.DOORSTOP_INVOKE_DLL_PATH), 2);

        Paths.SetExecutablePath(EnvVars.DOORSTOP_PROCESS_PATH,
                                bepinPath,
                                EnvVars.DOORSTOP_MANAGED_FOLDER_DIR,
                                true,
                                EnvVars.DOORSTOP_DLL_SEARCH_DIRS);

        LoadCriticalAssemblies();
        AppDomain.CurrentDomain.AssemblyResolve += LocalResolve;
        PreloaderMain();
    }

    private static void PreloaderMain()
    {
        if (UnityPreloader.ConfigApplyRuntimePatches.Value)
        {
            XTermFix.Apply();
            ConsoleSetOutFix.Apply();
        }

        UnityPreloader.Run();
    }

    private static Assembly LocalResolve(object sender, ResolveEventArgs args)
    {
        if (!Utility.TryParseAssemblyName(args.Name, out var assemblyName))
            return null;

        
        

        var validAssemblies = AppDomain.CurrentDomain
                                       .GetAssemblies()
                                       .Select(a => new
                                       {
                                           assembly = a,
                                           name = Utility.TryParseAssemblyName(a.FullName, out var name)
                                                      ? name
                                                      : null
                                       })
                                       .Where(a => a.name != null && a.name.Name == assemblyName.Name)
                                       .OrderByDescending(a => a.name.Version)
                                       .ToList();

        
        
        var foundMatch = validAssemblies.FirstOrDefault(a => a.name.Version == assemblyName.Version) ??
                         validAssemblies.FirstOrDefault();
        var foundAssembly = foundMatch?.assembly;

        if (foundAssembly != null)
            return foundAssembly;

        if (MonoAssemblyHelper.TryResolveDllAssembly(assemblyName, Paths.BepInExAssemblyDirectory, out foundAssembly)
         || MonoAssemblyHelper.TryResolveDllAssembly(assemblyName, Paths.PatcherPluginPath, out foundAssembly)
         || MonoAssemblyHelper.TryResolveDllAssembly(assemblyName, Paths.PluginPath, out foundAssembly))
            return foundAssembly;

        return null;
    }
}
