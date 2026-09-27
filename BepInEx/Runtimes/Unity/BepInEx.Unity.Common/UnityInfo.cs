using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using AssetRipper.Primitives;
using MonoMod.Utils;

[assembly: InternalsVisibleTo("BepInEx.Unity.Mono.Preloader")]
[assembly: InternalsVisibleTo("BepInEx.Unity.Mono")]
[assembly: InternalsVisibleTo("BepInEx.Unity.IL2CPP")]

namespace BepInEx.Unity.Common;




public static class UnityInfo
{
    
    private static readonly ManagerLookup[] ManagerVersionLookup =
    {
        new("globalgamemanagers", 0x14, 0x30),
        new("data.unity3d", 0x12),
        new("mainData", 0x14)
    };

    private static bool initialized;

    
    
    
    public static string PlayerPath { get; private set; }

    
    
    
    public static string GameDataPath { get; private set; }

    
    
    
    
    
    
    
    public static UnityVersion Version { get; private set; }

    internal static void Initialize(string unityPlayerPath, string gameDataPath)
    {
        if (initialized)
            return;
        PlayerPath = Path.GetFullPath(unityPlayerPath ?? throw new ArgumentNullException(nameof(unityPlayerPath)));
        GameDataPath = Path.GetFullPath(gameDataPath ?? throw new ArgumentNullException(nameof(gameDataPath)));

        DetermineVersion();
        initialized = true;
    }

    internal static void SetRuntimeUnityVersion(string version) => Version = UnityVersion.Parse(version);

    private static void DetermineVersion()
    {
        
        foreach (var lookup in ManagerVersionLookup)
            if (lookup.TryLookup(out var version))
            {
                Version = version;
                return;
            }

        
        if (PlatformHelper.Is(Platform.Windows))
            try
            {
                var version = FileVersionInfo.GetVersionInfo(PlayerPath);
                
                var simpleVersion = new Version(version.FileVersion);
                Version = new UnityVersion((ushort) simpleVersion.Major, (ushort) simpleVersion.Minor,
                                           (ushort) simpleVersion.Build);
                return;
            }
            catch (Exception)
            {
                
                
            }

        
        
        
        var managed = Path.Combine(GameDataPath, "Managed");
        if (File.Exists(Path.Combine(managed, "UnityEngine.CoreModule.dll")))
            Version = new UnityVersion(2017, 0, 0, UnityVersionType.Experimental);

        Version = default;
    }

    private class ManagerLookup
    {
        private readonly string filePath;
        private readonly int[] lookupOffsets;

        public ManagerLookup(string filePath, params int[] lookupOffsets)
        {
            this.filePath = filePath;
            this.lookupOffsets = lookupOffsets;
        }

        public bool TryLookup(out UnityVersion version)
        {
            var path = Path.Combine(GameDataPath, filePath);
            if (!File.Exists(path))
            {
                version = default;
                return false;
            }

            using var fs = File.OpenRead(path);
            foreach (var offset in lookupOffsets)
            {
                var sb = new StringBuilder();
                fs.Position = offset;

                byte b;
                while ((b = (byte) fs.ReadByte()) != 0)
                    sb.Append((char) b);

                try
                {
                    version = UnityVersion.Parse(sb.ToString());
                    return true;
                }
                catch (Exception e)
                {
                    
                }
            }

            version = default;
            return false;
        }
    }
}
