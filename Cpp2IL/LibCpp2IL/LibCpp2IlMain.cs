using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.Primitives;
using LibCpp2IL.Elf;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using LibCpp2IL.NintendoSwitch;
using LibCpp2IL.Reflection;
using LibCpp2IL.Wasm;

namespace LibCpp2IL;

public static class LibCpp2IlMain
{
    private static readonly Regex UnityVersionRegex = new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+[abcfxp][0-9]+$", RegexOptions.Compiled);

    public class LibCpp2IlSettings
    {
        public bool AllowManualMetadataAndCodeRegInput;
        public bool DisableMethodPointerMapping;
        public bool DisableGlobalResolving;
    }

    public static readonly LibCpp2IlSettings Settings = new();

    public static bool Il2CppTypeHasNumMods5Bits;
    public static float MetadataVersion => TheMetadata!.MetadataVersion;
    
    public static Il2CppBinary? Binary;
    public static Il2CppMetadata? TheMetadata;

    public static readonly Dictionary<ulong, List<Il2CppMethodDefinition>> MethodsByPtr = new();

    public static void Reset()
    {
        LibCpp2IlGlobalMapper.Reset();
        MethodsByPtr.Clear();
    }

    public static List<Il2CppMethodDefinition>? GetManagedMethodImplementationsAtAddress(ulong addr)
    {
        MethodsByPtr.TryGetValue(addr, out var ret);

        return ret;
    }

    public static MetadataUsage? GetAnyGlobalByAddress(ulong address)
    {
        if (MetadataVersion >= 27f)
            return LibCpp2IlGlobalMapper.CheckForPost27GlobalAt(address);

        
        var glob = GetLiteralGlobalByAddress(address);
        glob ??= GetMethodGlobalByAddress(address);
        glob ??= GetRawFieldGlobalByAddress(address);
        glob ??= GetRawTypeGlobalByAddress(address);

        return glob;
    }

    public static MetadataUsage? GetLiteralGlobalByAddress(ulong address)
    {
        if (MetadataVersion < 27f)
            return LibCpp2IlGlobalMapper.LiteralsByAddress.GetOrDefault(address);

        return GetAnyGlobalByAddress(address);
    }

    public static string? GetLiteralByAddress(ulong address)
    {
        var literal = GetLiteralGlobalByAddress(address);
        if (literal?.Type != MetadataUsageType.StringLiteral)
            return null;

        return literal.AsLiteral();
    }

    public static MetadataUsage? GetRawTypeGlobalByAddress(ulong address)
    {
        if (MetadataVersion < 27f)
            return LibCpp2IlGlobalMapper.TypeRefsByAddress.GetOrDefault(address);

        return GetAnyGlobalByAddress(address);
    }

    public static Il2CppTypeReflectionData? GetTypeGlobalByAddress(ulong address)
    {
        if (TheMetadata == null) return null;

        var typeGlobal = GetRawTypeGlobalByAddress(address);

        if (typeGlobal?.Type is not (MetadataUsageType.Type or MetadataUsageType.TypeInfo))
            return null;

        return typeGlobal.AsType();
    }

    public static MetadataUsage? GetRawFieldGlobalByAddress(ulong address)
    {
        if (MetadataVersion < 27f)
            return LibCpp2IlGlobalMapper.FieldRefsByAddress.GetOrDefault(address);
        return GetAnyGlobalByAddress(address);
    }

    public static Il2CppFieldDefinition? GetFieldGlobalByAddress(ulong address)
    {
        if (TheMetadata == null) return null;

        var typeGlobal = GetRawFieldGlobalByAddress(address);

        return typeGlobal?.AsField();
    }

    public static MetadataUsage? GetMethodGlobalByAddress(ulong address)
    {
        if (TheMetadata == null) return null;

        if (MetadataVersion < 27f)
            return LibCpp2IlGlobalMapper.MethodRefsByAddress.GetOrDefault(address);

        return GetAnyGlobalByAddress(address);
    }

    public static Il2CppMethodDefinition? GetMethodDefinitionByGlobalAddress(ulong address)
    {
        var global = GetMethodGlobalByAddress(address);

        if (global?.Type == MetadataUsageType.MethodRef)
            return global.AsGenericMethodRef().BaseMethod;

        return global?.AsMethod();
    }

    
    
    
    
    
    
    
    
    
    public static bool Initialize(byte[] binaryBytes, byte[] metadataBytes, UnityVersion unityVersion)
    {
        LibCpp2IlReflection.ResetCaches();

        var start = DateTime.Now;

        LibLogger.InfoNewline("Initializing Metadata...");

        var meta = TheMetadata = Il2CppMetadata.ReadFrom(metadataBytes, unityVersion);

        Il2CppTypeHasNumMods5Bits = meta.MetadataVersion >= 27.2f;

        LibLogger.InfoNewline($"Initialized Metadata in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

        var bin = Binary = LibCpp2IlBinaryRegistry.CreateAndInit(binaryBytes, meta);

        if (!Settings.DisableGlobalResolving && MetadataVersion < 27)
        {
            start = DateTime.Now;
            LibLogger.Info("Mapping Globals...");
            LibCpp2IlGlobalMapper.MapGlobalIdentifiers(meta, bin);
            LibLogger.InfoNewline($"OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
        }

        if (!Settings.DisableMethodPointerMapping)
        {
            start = DateTime.Now;
            LibLogger.Info("Mapping pointers to Il2CppMethodDefinitions...");
            var i = 0;
            foreach (var (method, ptr) in meta.methodDefs.Select(method => (method, ptr: method.MethodPointer)))
            {
                if (!MethodsByPtr.ContainsKey(ptr))
                    MethodsByPtr[ptr] = [];

                MethodsByPtr[ptr].Add(method);
                i++;
            }

            LibLogger.InfoNewline($"Processed {i} OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
        }

        LibCpp2IlReflection.InitCaches();

        return true;
    }

    
    
    
    
    
    
    
    
    
    public static bool LoadFromFile(string pePath, string metadataPath, UnityVersion unityVersion)
    {
        var metadataBytes = File.ReadAllBytes(metadataPath);

        const uint metadataMagic = 0xFAB11BAF;
        if ((metadataBytes.Length < sizeof(uint) || BitConverter.ToUInt32(metadataBytes, 0) != metadataMagic) &&
            Path.GetDirectoryName(metadataPath) is { } metadataDirectory)
        {
            var sidecarPath = Path.Combine(metadataDirectory, "global-metadata-decrypted.dat");
            if (File.Exists(sidecarPath))
            {
                var sidecarBytes = File.ReadAllBytes(sidecarPath);
                if (sidecarBytes.Length >= sizeof(uint) && BitConverter.ToUInt32(sidecarBytes, 0) == metadataMagic)
                {
                    LibLogger.InfoNewline($"Using decrypted metadata sidecar {sidecarPath}; the live metadata remains encrypted for the game runtime.");
                    metadataBytes = sidecarBytes;
                }
            }
        }

        var peBytes = File.ReadAllBytes(pePath);

        return Initialize(peBytes, metadataBytes, unityVersion);
    }

    
    
    
    
    
    
    public static UnityVersion DetermineUnityVersion(string? unityPlayerPath, string? gameDataPath)
    {
        
        
        if (!string.IsNullOrEmpty(gameDataPath))
        {
            LibLogger.VerboseNewline($"DetermineUnityVersion: Have game data path {gameDataPath}, trying to pull version from globalgamemanagers or data.unity3d");

            
            var globalgamemanagersPath = Path.Combine(gameDataPath, "globalgamemanagers");
            if (File.Exists(globalgamemanagersPath))
            {
                LibLogger.VerboseNewline($"DetermineUnityVersion: globalgamemanagers exists, pulling version from it");

                var ggmBytes = File.ReadAllBytes(globalgamemanagersPath);
                return GetVersionFromGlobalGameManagers(ggmBytes);
            }

            
            var dataPath = Path.Combine(gameDataPath, "data.unity3d");
            if (File.Exists(dataPath))
            {
                LibLogger.VerboseNewline($"DetermineUnityVersion: data.unity3d exists, pulling version from it");

                using var dataStream = File.OpenRead(dataPath);
                return GetVersionFromDataUnity3D(dataStream);
            }

            LibLogger.VerboseNewline($"DetermineUnityVersion: No globalgamemanagers or data.unity3d found in game data path.");
        }
        
        if (Environment.OSVersion.Platform == PlatformID.Win32NT && !string.IsNullOrEmpty(unityPlayerPath))
        {
            LibLogger.VerboseNewline($"DetermineUnityVersion: Running on windows so have FileVersionInfo, trying to pull version from unity player {unityPlayerPath}");
            var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath);

            if (unityVer.FileMajorPart > 0)
                return new UnityVersion((ushort)unityVer.FileMajorPart, (ushort)unityVer.FileMinorPart, (ushort)unityVer.FileBuildPart);

            LibLogger.VerboseNewline($"DetermineUnityVersion: FileVersionInfo gave useless result.");
        }

        LibLogger.VerboseNewline($"DetermineUnityVersion: All methods to determine unity version failed!");

        return default;
    }

    
    
    
    
    
    public static UnityVersion GetVersionFromGlobalGameManagers(byte[] ggmBytes)
    {
        var verString = new StringBuilder();
        var idx = 0x14;
        while (ggmBytes[idx] != 0)
        {
            verString.Append(Convert.ToChar(ggmBytes[idx]));
            idx++;
        }

        string unityVer = verString.ToString();

        if (!UnityVersionRegex.IsMatch(unityVer))
        {
            idx = 0x30;
            verString = new StringBuilder();
            while (ggmBytes[idx] != 0)
            {
                verString.Append(Convert.ToChar(ggmBytes[idx]));
                idx++;
            }

            unityVer = verString.ToString().Trim();
        }

        return UnityVersion.Parse(unityVer);
    }

    
    
    
    
    
    public static UnityVersion GetVersionFromDataUnity3D(Stream fileStream)
    {
        
        
        
        

        var verString = new StringBuilder();

        if (fileStream.CanSeek)
            fileStream.Seek(0x12, SeekOrigin.Begin);
        else
        {
            if (fileStream.Read(new byte[0x12], 0, 0x12) != 0x12)
                throw new("Failed to seek to 0x12 in data.unity3d");
        }

        while (true)
        {
            var read = fileStream.ReadByte();
            if (read == 0)
            {
                
                break;
            }

            verString.Append(Convert.ToChar(read));
        }

        var unityVer = verString.ToString().Trim();

        return UnityVersion.Parse(unityVer);
    }
}
