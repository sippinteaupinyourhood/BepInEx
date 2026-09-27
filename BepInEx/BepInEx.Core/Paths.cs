using System.IO;
using System.Linq;
using System.Reflection;
using MonoMod.Utils;
using SemanticVersioning;

namespace BepInEx;




public static class Paths
{
    
    
    
    
    public static Version BepInExVersion { get; } =
        Version.Parse(MetadataHelper.GetAttributes<AssemblyInformationalVersionAttribute>(typeof(Paths).Assembly)[0]
                                    .InformationalVersion);

    
    
    
    public static Version DisplayBepInExVersion { get; } =
        new(BepInExVersion.Major, BepInExVersion.Minor, BepInExVersion.Patch, BepInExVersion.PreRelease);

    
    
    
    public static string ManagedPath { get; private set; }

    
    
    
    public static string GameDataPath { get; private set; }

    
    
    
    public static string BepInExAssemblyDirectory { get; private set; }

    
    
    
    public static string BepInExAssemblyPath { get; private set; }

    
    
    
    public static string BepInExRootPath { get; private set; }

    
    
    
    public static string ExecutablePath { get; private set; }

    
    
    
    
    public static string GameRootPath { get; private set; }

    
    
    
    public static string ConfigPath { get; private set; }

    
    
    
    public static string BepInExConfigPath { get; private set; }

    
    
    
    public static string CachePath { get; private set; }

    
    
    
    public static string PatcherPluginPath { get; private set; }

    
    
    
    
    
    
    public static string PluginPath { get; private set; }

    
    
    
    public static string ProcessName { get; private set; }

    
    
    
    public static string[] DllSearchPaths { get; private set; }

    public static void SetExecutablePath(string executablePath,
                                         string bepinRootPath = null,
                                         string managedPath = null,
                                         bool gameDataRelativeToManaged = false,
                                         string[] dllSearchPath = null)
    {
        ExecutablePath = executablePath;
        ProcessName = Path.GetFileNameWithoutExtension(executablePath);

        GameRootPath = PlatformHelper.Is(Platform.MacOS)
                           ? Utility.ParentDirectory(executablePath, 4)
                           : Path.GetDirectoryName(executablePath);

        if (managedPath != null && gameDataRelativeToManaged)
        {
            GameDataPath = Path.GetDirectoryName(managedPath);
        }
        else
        {
            
            
            
            GameDataPath = Path.Combine(GameRootPath, $"{ProcessName}_Data");
            if (!Directory.Exists(GameDataPath))
                GameDataPath = Path.Combine(GameRootPath, "Data");
        }
        
        if (string.IsNullOrEmpty(GameDataPath) || !Directory.Exists(GameDataPath))
            throw new DirectoryNotFoundException("Failed to extract valid GameDataPath from executablePath: " + executablePath);

        ManagedPath = managedPath ?? Path.Combine(GameDataPath, "Managed");
        BepInExRootPath = bepinRootPath ?? Path.Combine(GameRootPath, "BepInEx");
        ConfigPath = Path.Combine(BepInExRootPath, "config");
        BepInExConfigPath = Path.Combine(ConfigPath, "BepInEx.cfg");
        PluginPath = Path.Combine(BepInExRootPath, "plugins");
        PatcherPluginPath = Path.Combine(BepInExRootPath, "patchers");
        BepInExAssemblyDirectory = Path.Combine(BepInExRootPath, "core");
        BepInExAssemblyPath = Path.Combine(BepInExAssemblyDirectory,
                                           $"{Assembly.GetExecutingAssembly().GetName().Name}.dll");
        CachePath = Path.Combine(BepInExRootPath, "cache");
        DllSearchPaths = (dllSearchPath ?? new string[0]).Concat(new[] { ManagedPath }).Distinct().ToArray();
    }

    internal static void SetPluginPath(string pluginPath) =>
        PluginPath = Utility.CombinePaths(BepInExRootPath, pluginPath);
}
