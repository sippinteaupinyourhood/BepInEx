using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.IL2CPP.RuntimeFixes;
using BepInEx.Logging;
using BepInEx.Preloader.Core;
using BepInEx.Preloader.Core.Logging;
using BepInEx.Preloader.Core.Patching;
using BepInEx.Preloader.RuntimeFixes;
using BepInEx.Unity.Common;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace BepInEx.Unity.IL2CPP;

public static class Preloader
{
    private static PreloaderConsoleListener PreloaderLog { get; set; }

    internal static ManualLogSource Log => PreloaderLogger.Log;

    // TODO: This is not needed, maybe remove? (Instance is saved in IL2CPPChainloader itself)
    private static IL2CPPChainloader Chainloader { get; set; }

    public static void Run()
    {
        try
        {
            HarmonyBackendFix.Initialize();
            ConsoleSetOutFix.Apply();
            UnityInfo.Initialize(Paths.ExecutablePath, Paths.GameDataPath);

            ConsoleManager.Initialize(false, true);

            PreloaderLog = new PreloaderConsoleListener();
            Logger.Listeners.Add(PreloaderLog);

            if (ConsoleManager.ConsoleEnabled)
            {
                ConsoleManager.CreateConsole();
                Logger.Listeners.Add(new ConsoleLogListener());
                InstallFreeConsoleHook();
            }

            RedirectStdErrFix.Apply();

            ChainloaderLogHelper.PrintLogInfo(Log);

            Logger.Log(LogLevel.Info, $"Running under Unity {UnityInfo.Version}");
            Logger.Log(LogLevel.Info, $"Runtime version: {Environment.Version}");
            Logger.Log(LogLevel.Info, $"Runtime information: {RuntimeInformation.FrameworkDescription}");

            Logger.Log(LogLevel.Debug, $"Game executable path: {Paths.ExecutablePath}");
            Logger.Log(LogLevel.Debug, $"Interop assembly directory: {Il2CppInteropManager.IL2CPPInteropAssemblyPath}");
            Logger.Log(LogLevel.Debug, $"BepInEx root path: {Paths.BepInExRootPath}");

            if (PlatformHelper.Is(Platform.Wine) && !Environment.Is64BitProcess)
            {
                if (!NativeLibrary.TryGetExport(NativeLibrary.Load("ntdll"), "RtlRestoreContext", out var _))
                {
                    Logger.Log(LogLevel.Warning,
                               "Your wine version doesn't support CoreCLR properly, expect crashes! Upgrade to wine 7.16 or higher.");
                }
            }

            NativeLibrary.SetDllImportResolver(typeof(Il2CppInterop.Runtime.IL2CPP).Assembly, DllImportResolver);

            string decMetaPath = null;
            try
            {
                var metaPath = Path.Combine(Paths.GameDataPath,
                    "il2cpp_data", "Metadata", "global-metadata.dat");
                decMetaPath = VRChatMetadataDecryptor.TryDecrypt(metaPath);
                if (decMetaPath != null)
                {
                    Log.LogInfo("[VRCDecrypt] Encrypted VRChat metadata detected — decrypted to temp file");
                    Il2CppInteropManager.GlobalMetadataPathOverride = decMetaPath;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[VRCDecrypt] Metadata decryption failed ({ex.GetType().Name})");
            }

            Il2CppInteropManager.Initialize();

            if (decMetaPath != null)
                try { File.Delete(decMetaPath); } catch { }

            using (var assemblyPatcher = new AssemblyPatcher((data, _) => Assembly.Load(data)))
            {
                assemblyPatcher.AddPatchersFromDirectory(Paths.PatcherPluginPath);

                Log.LogInfo($"{assemblyPatcher.PatcherContext.PatcherPlugins.Count} patcher plugin{(assemblyPatcher.PatcherContext.PatcherPlugins.Count == 1 ? "" : "s")} loaded");

                assemblyPatcher.LoadAssemblyDirectories(Il2CppInteropManager.IL2CPPInteropAssemblyPath);

                Log.LogInfo($"{assemblyPatcher.PatcherContext.PatcherPlugins.Count} assemblies discovered");

                assemblyPatcher.PatchAndLoad();
            }


            Logger.Listeners.Remove(PreloaderLog);


            Chainloader = new IL2CPPChainloader();

            Chainloader.Initialize();
        }
        catch (Exception ex)
        {
            Log.Log(LogLevel.Fatal, ex);

            throw;
        }
    }

    // Keep alive so GC doesn't collect the delegate
    private static NativeDetour _freeConsoleDetour;
    private static FreeConsoleDelegate _freeConsoleHookDelegate;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool FreeConsoleDelegate();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpLib, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    private static void InstallFreeConsoleHook()
    {
        try
        {
            var k32 = LoadLibraryEx("kernel32.dll", IntPtr.Zero, 0x800);
            var target = GetProcAddress(k32, "FreeConsole");
            if (target == IntPtr.Zero) return;
            _freeConsoleHookDelegate = static () => true;
            _freeConsoleDetour = new NativeDetour(target, Marshal.GetFunctionPointerForDelegate(_freeConsoleHookDelegate));
            Log.LogInfo("FreeConsole hooked — console window protected");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"FreeConsole hook failed: {ex.Message}");
        }
    }

    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == "GameAssembly")
        {
            return NativeLibrary.Load(Il2CppInteropManager.GameAssemblyPath, assembly, searchPath);
        }

        return IntPtr.Zero;
    }
}

internal static class VRChatMetadataDecryptor
{
    private const int HeaderSize   = 368;        // encrypted header size
    private const int NewHdrSize   = 304;        // standard IL2CPP header size
    private const uint EncMagic    = 0x4E4F7E5D;
    private const uint Unity6Build1899EncryptedMagic = 0x2E1C2E3D;
    private const int XorBase      = 0x51;

    // All field values are byte offsets into the HeaderTransform'd header buffer.
    // StdOff < 0 means section is not mapped directly (found by scan instead).
    private static readonly (int SrcOff, int SrcAdj, int Len, int Seed, int Key, bool MinusSeed, int StdOff)[] Passes =
    {
        // SrcOff   SrcAdj  LenFld  Seed    KeyConst  MinusSeed  StdOff (section)
        ( 184,      -60,     80,    80,    +117,      true,     168 ),
        (   8,      +28,    192,   192,    +117,      true,      16 ),
        ( 260,      -24,    272,   272,    -117,      false,     40 ),
        ( 236,      -20,    172,   172,    -117,      false,     96 ),
        ( 200,      -28,    212,   212,    +117,      true,       8 ),
        ( 244,      -48,     16,    16,    -117,      false,     24 ),
        ( 232,      +36,     76,    76,    +117,      true,      48 ),
    };

    // Plain (non-XOR) sections: only need offset + size from the decrypted header.
    private static readonly (int StdOff, int OffFld, int OffAdj, int LenFld)[] PlainSections =
    {
        ( 160, 104, 28, 240 ),
    };


    private static int I32(byte[] b, int off) => BitConverter.ToInt32(b, off);

    private static void SetHdr(byte[] newHdr, int stdOff, int fileOff, int size)
    {
        BitConverter.GetBytes(fileOff).CopyTo(newHdr, stdOff);
        BitConverter.GetBytes(size).CopyTo(newHdr, stdOff + 4);
    }

    public static string TryDecrypt(string srcPath)
    {
        if (!File.Exists(srcPath)) return null;

        var data = File.ReadAllBytes(srcPath);
        if (data.Length >= 384 && BitConverter.ToUInt32(data, 0) == Unity6Build1899EncryptedMagic)
            return DecryptUnity6Build1899(data);

        if (BitConverter.ToUInt32(data, 0) != EncMagic) return null;

        var output = (byte[])data.Clone();
        var hdr = HeaderTransform(data);

        DecryptSections(output, hdr);
        RewriteHeader(output, hdr);

        var tempPath = Path.Combine(Path.GetTempPath(),
            $"bepinex-vrchat-metadata-{Environment.ProcessId}-{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(tempPath, output);
        return tempPath;
    }

    private static string DecryptUnity6Build1899(byte[] data)
    {
        var output = (byte[])data.Clone();
        var header = new byte[384];
        Array.Copy(data, header, header.Length);
        for (var index = 0; index < header.Length; index++)
            header[index] ^= (byte)((0x29 + index) & 0xFF);

        DecryptUnity6Build1899Section(output, header, 144, -28, 340, 340, true);
        DecryptUnity6Build1899Section(output, header, 376, -52, 360, 360, true);
        DecryptUnity6Build1899Section(output, header, 348, -36, 64, 64, false);
        DecryptUnity6Build1899Section(output, header, 236, 52, 212, 212, false);
        DecryptUnity6Build1899Section(output, header, 356, -28, 32, 32, false);
        DecryptUnity6Build1899Section(output, header, 20, 20, 92, 92, false);
        DecryptUnity6Build1899Section(output, header, 336, 60, 12, 12, true);

        var normalized = new byte[NewHdrSize];
        BitConverter.GetBytes(0xFAB11BAFu).CopyTo(normalized, 0);
        BitConverter.GetBytes(31).CopyTo(normalized, 4);
        var sections = new (int Offset, int Size)[]
        {
            (384, 441016), (441400, 2003060), (2444460, 8318916), (10763376, 23064),
            (10786440, 950360), (11736800, 12206196), (23942996, 116136), (24059132, 422940),
            (24482072, 460900), (24942972, 149096), (25092068, 4190292), (29282360, 2006400),
            (31288760, 208848), (31497608, 12120), (31509728, 121088), (31630816, 57256),
            (31688072, 64180), (31752252, 1700168), (33452420, 314632), (33767052, 3342944),
            (37109996, 9160), (37119156, 14656), (37133812, 19816), (37153628, 6276),
            (37159904, 1986468), (39146372, 816072), (39962444, 151420), (40113864, 106952),
            (40220816, 0), (40220816, 0), (40220816, 16220)
        };
        for (var index = 0; index < sections.Length; index++)
            SetHdr(normalized, 8 + index * 8, sections[index].Offset, sections[index].Size);

        ValidateUnity6Build1899Header(normalized, output.Length);
        Array.Copy(normalized, output, normalized.Length);

        var tempPath = Path.Combine(Path.GetTempPath(),
            $"bepinex-vrchat-metadata-{Environment.ProcessId}-{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(tempPath, output);
        return tempPath;
    }

    private static void DecryptUnity6Build1899Section(byte[] output, byte[] header, int sourceField,
                                                       int adjustment, int lengthField, int seedField,
                                                       bool subtractSeed)
    {
        var offset = I32(header, sourceField) + adjustment;
        var length = I32(header, lengthField);
        if (offset < 0 || length < 0 || offset > output.Length - length)
            throw new InvalidDataException("Invalid encrypted metadata section.");

        var seed = header[seedField];
        for (var index = 0; index < length; index++)
        {
            var key = subtractSeed
                ? (byte)((index - seed + 58) & 0xFF)
                : (byte)((seed + index - 58) & 0xFF);
            output[offset + index] ^= key;
        }
    }

    private static void ValidateUnity6Build1899Header(byte[] header, int fileLength)
    {
        var previousEnd = header.Length;
        for (var offset = 8; offset <= 248; offset += 8)
        {
            var sectionOffset = I32(header, offset);
            var sectionSize = I32(header, offset + 4);
            if (sectionOffset < header.Length || sectionSize < 0 ||
                sectionOffset > fileLength - sectionSize || sectionOffset < previousEnd)
                throw new InvalidDataException("Invalid normalized metadata layout.");
            previousEnd = sectionOffset + sectionSize;
        }
    }

    private static byte[] HeaderTransform(byte[] data)
    {
        var buf = new byte[HeaderSize];
        Array.Copy(data, 0, buf, 0, HeaderSize);
        for (int p = 0; p < HeaderSize; p++)
            buf[p] ^= (byte)((XorBase - p) & 0xFF);
        return buf;
    }

    private static void DecryptSections(byte[] output, byte[] hdr)
    {
        foreach (var (srcOff, srcAdj, lenFld, seedFld, key, minusSeed, _) in Passes)
        {
            int off    = I32(hdr, srcOff) + srcAdj;
            int length = I32(hdr, lenFld);
            byte seed  = hdr[seedFld];
            for (int i = 0; i < length; i++)
            {
                byte k = minusSeed
                    ? (byte)((i - seed + key) & 0xFF)
                    : (byte)((seed + i + key) & 0xFF);
                output[off + i] ^= k;
            }
        }
    }

    private static void RewriteHeader(byte[] output, byte[] hdr)
    {
        var newHdr = new byte[NewHdrSize];
        BitConverter.GetBytes(0xFAB11BAFu).CopyTo(newHdr, 0);
        BitConverter.GetBytes(29).CopyTo(newHdr, 4);

        foreach (var (srcOff, srcAdj, lenFld, _, _, _, stdOff) in Passes)
        {
            if (stdOff < 0) continue;
            SetHdr(newHdr, stdOff, I32(hdr, srcOff) + srcAdj, I32(hdr, lenFld));
        }
        foreach (var (stdOff, offFld, offAdj, lenFld) in PlainSections)
            SetHdr(newHdr, stdOff, I32(hdr, offFld) + offAdj, I32(hdr, lenFld));

        int typeOffset = BitConverter.ToInt32(newHdr, 160);
        int typeSize = BitConverter.ToInt32(newHdr, 164);
        int imageOffset = BitConverter.ToInt32(newHdr, 168);
        SetHdr(newHdr, 176, typeOffset + typeSize, imageOffset - typeOffset - typeSize);

        var (parameterOffset, parameterSize) = ScanForParameters(output, newHdr);
        if (parameterOffset > 0)
            SetHdr(newHdr, 88, parameterOffset, parameterSize);
        var (eventOffset, eventSize) = ScanForEvents(output, newHdr);
        if (eventOffset > 0)
            SetHdr(newHdr, 32, eventOffset, eventSize);

        Array.Copy(newHdr, 0, output, 0, NewHdrSize);
    }

    private static (int Offset, int Size) ScanForParameters(byte[] data, byte[] header)
    {
        int stringsSize = BitConverter.ToInt32(header, 28);
        return Scan(data, 12, 1000, (buffer, position) =>
        {
            int name = BitConverter.ToInt32(buffer, position);
            uint token = BitConverter.ToUInt32(buffer, position + 4);
            int type = BitConverter.ToInt32(buffer, position + 8);
            return name >= 0 && name < stringsSize && (token & 0xFF000000) == 0x08000000 && type >= 0 && type < 1_000_000;
        });
    }

    private static (int Offset, int Size) ScanForEvents(byte[] data, byte[] header)
    {
        int stringsSize = BitConverter.ToInt32(header, 28);
        return Scan(data, 24, 100, (buffer, position) =>
        {
            int name = BitConverter.ToInt32(buffer, position);
            int type = BitConverter.ToInt32(buffer, position + 4);
            int add = BitConverter.ToInt32(buffer, position + 8);
            int remove = BitConverter.ToInt32(buffer, position + 12);
            return name >= 0 && name < stringsSize && type >= -1 && type < 1_000_000 &&
                   (add == -1 || add >= 0 && add < 1_000_000) &&
                   (remove == -1 || remove >= 0 && remove < 1_000_000);
        });
    }

    private static (int Offset, int Size) Scan(byte[] data, int stride, int minimum, Func<byte[], int, bool> validator)
    {
        int bestStart = 0;
        int bestCount = 0;
        for (int phase = 0; phase < stride; phase += 4)
        {
            int currentStart = 0;
            int currentCount = 0;
            for (int position = NewHdrSize + phase; position + stride <= data.Length; position += stride)
            {
                if (validator(data, position))
                {
                    if (currentCount == 0) currentStart = position;
                    currentCount++;
                    if (currentCount > bestCount)
                    {
                        bestStart = currentStart;
                        bestCount = currentCount;
                    }
                }
                else
                {
                    currentCount = 0;
                }
            }
        }
        return bestCount >= minimum ? (bestStart, bestCount * stride) : (0, 0);
    }

}
