using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

            var appliedEnumRepairs = RuntimeEnumRepair.ApplyPending(Il2CppInteropManager.IL2CPPInteropAssemblyPath);
            if (appliedEnumRepairs != 0)
                Logger.Log(LogLevel.Info, $"Applied {appliedEnumRepairs} pending runtime enum repairs");

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
                    Il2CppInteropManager.GlobalMetadataPathOverride = decMetaPath;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Metadata preparation failed: {ex}");
            }

            try
            {
                Il2CppInteropManager.Initialize();
            }
            finally
            {
                if (decMetaPath != null)
                    try { File.Delete(decMetaPath); } catch { }
            }

            using (var assemblyPatcher = new AssemblyPatcher((data, _) => Assembly.Load(data)))
            {
                assemblyPatcher.AddPatchersFromDirectory(Paths.PatcherPluginPath);

                Log.LogInfo($"{assemblyPatcher.PatcherContext.PatcherPlugins.Count} patcher plugin{(assemblyPatcher.PatcherContext.PatcherPlugins.Count == 1 ? "" : "s")} loaded");

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
    private const int HeaderSize   = 368;        
    private const int NewHdrSize   = 304;        
    private const uint EncMagic    = 0x4E4F7E5D;
    private const uint Build1901EncryptedMagic = 0x7E7D8417;
    private const uint Build1902EncryptedMagic = 0x2E2D0E53;
    private const uint Build1903EncryptedMagic = 0x55564FB4;
    private const int XorBase      = 0x51;

    private const string Build1902NormalizedSha256 =
        "6EB6807158ED3163A7E9D0AD654B1BF1180709D70D37BF9EF5228942321DDB78";

    private const string Build1903EncryptedSha256 =
        "B692689790AC7A019055172536CE6C750AB1FBF9F4B18FE1F9B0F818D61B7BC1";

    private const string Build1903NormalizedSha256 =
        "49B2470A18F4745D54A0DA67AC5F47DBC290D8A2E225626C00E9F1C8ABE1F298";

    private const string Build1903NormalizedHeader =
        "rxux+h0AAABgAQAAoKEGAACjBgC4KB4AuMskAFRnfAAMM6EA+FgAAASMoQBo/Q0AbImvAMBOoQAs2FABjLoBALiSUgFQagYACP1YAZw7FgCkOG8BMEACANR4cQGc/j0AcHevAdg8HgBItM0BsCEDAPjV0AFILgAAQATRARDPAQBQ09IBGNsAAGiu0wFg+AAAyKbUATwoGQAEz+0BwJcEAMRm8gGYQzIAXKokAgAjAABczSQCADgAAFwFJQJIIgAApCclAuwYAACQQCUCx3gcAFi5QQK4+AsAELJNArw1AgDM508CwIwBAIx0UQIYAAAApHRRAgANAACkgVEClCIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

    private const string Build1902NormalizedHeader =
        "rxux+h0AAACIAQAAoKEGACijBgCsKB4A1MskAFRnfAAoM6EA+FgAACCMoQBo/Q0AiImvAMBOoQBI2FABjLoBANSSUgFQagYAJP1YAZQ7FgC4OG8BMEACAOx4cQGc/j0AiHevAdg8HgBgtM0BsCEDABDW0AFILgAAWATRARDPAQBo09IBGNsAAICu0wFg+AAA4KbUATwoGQAcz+0BwJcEANxm8gGYQzIAdKokAgAjAAB0zSQCADgAAHQFJQJIIgAAvCclAuwYAACoQCUCx3gcAHC5QQK4+AsAKLJNArw1AgDk508CwIwBAKR0UQIYAAAAvHRRAgQLAADAf1ECpCcAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMh4HABUZ3wA7BgAAEzT0gH4WAAAmEMyABijBgCYklIBQP1YAVzYUAE0PR4A2CgeAAyyTQLAlwQAEM8BAGwE0QEwQAIAwE6hAHxnfAA4dFECgMskAOTV0AE=";

    private static readonly (int SourceOffset, int Adjustment, int LengthField, int SeedField, bool SubtractSeed, int Key)[] Build1902Passes =
    {
        (296, 48, 308, 296, true, -76),
        (176, -28, 184, 176, false, -76),
        (136, -36, 48, 136, true, 8),
        (120, -20, 372, 120, false, 8),
        (28, 32, 220, 28, false, 60),
        (216, 40, 84, 216, false, 68),
        (264, 64, 108, 264, true, -92),
    };

    private static readonly (int SourceOffset, int Adjustment, int LengthField, int SeedField, bool SubtractSeed, int Key)[] Build1903Passes =
    {
        (108, -20, 256, 256, false, -27),
        (328, -56, 152, 152, true, 27),
        (284, 48, 340, 340, false, -27),
        (80, 36, 252, 252, true, 27),
        (272, -28, 8, 8, true, 27),
        (96, 36, 180, 180, false, -27),
        (304, -44, 40, 40, true, 27),
    };

    
    
    private static readonly (int SrcOff, int SrcAdj, int Len, int Seed, int Key, bool MinusSeed, int StdOff)[] Passes =
    {
        
        ( 184,      -60,     80,    80,    +117,      true,     168 ),
        (   8,      +28,    192,   192,    +117,      true,      16 ),
        ( 260,      -24,    272,   272,    -117,      false,     40 ),
        ( 236,      -20,    172,   172,    -117,      false,     96 ),
        ( 200,      -28,    212,   212,    +117,      true,       8 ),
        ( 244,      -48,     16,    16,    -117,      false,     24 ),
        ( 232,      +36,     76,    76,    +117,      true,      48 ),
    };

    
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
        if (data.Length >= 352 && BitConverter.ToUInt32(data, 0) == Build1903EncryptedMagic)
            return DecryptBuild1903(srcPath, data);

        if (data.Length >= 392 && BitConverter.ToUInt32(data, 0) == Build1902EncryptedMagic)
            return DecryptBuild1902(srcPath, data);

        if (data.Length >= 376 && BitConverter.ToUInt32(data, 0) == Build1901EncryptedMagic)
            return DecryptBuild1901(srcPath, data);

        if (BitConverter.ToUInt32(data, 0) != EncMagic) return null;

        var output = (byte[])data.Clone();
        var hdr = HeaderTransform(data);

        DecryptSections(output, hdr);
        RewriteHeader(output, hdr);

        var tempPath = srcPath + ".dec";
        File.WriteAllBytes(tempPath, output);
        return tempPath;
    }

    private static string DecryptBuild1903(string srcPath, byte[] data)
    {
        var encryptedDigest = Convert.ToHexString(SHA256.HashData(data));
        if (!string.Equals(encryptedDigest, Build1903EncryptedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported build 1903 metadata input.");

        var output = (byte[])data.Clone();
        var header = new byte[352];
        Array.Copy(data, header, header.Length);
        for (var index = 0; index < header.Length; index++)
        {
            header[index] ^= (byte)(88 - index);
            output[index] = header[index];
        }

        foreach (var pass in Build1903Passes)
        {
            var offset = I32(header, pass.SourceOffset) + pass.Adjustment;
            var length = I32(header, pass.LengthField);
            if (offset < 0 || length < 0 || offset > output.Length - length)
                throw new InvalidDataException("Invalid build 1903 metadata section.");

            var seed = header[pass.SeedField];
            for (var index = 0; index < length; index++)
            {
                var key = pass.SubtractSeed
                    ? index - seed + pass.Key
                    : seed + index + pass.Key;
                output[offset + index] ^= (byte)key;
            }
        }

        var normalizedHeader = Convert.FromBase64String(Build1903NormalizedHeader);
        Array.Copy(normalizedHeader, output, normalizedHeader.Length);

        var digest = Convert.ToHexString(SHA256.HashData(output));
        if (!string.Equals(digest, Build1903NormalizedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Invalid build 1903 normalized metadata digest.");

        var outputPath = srcPath + ".dec";
        File.WriteAllBytes(outputPath, output);
        return outputPath;
    }

    private static string DecryptBuild1902(string srcPath, byte[] data)
    {
        var output = (byte[])data.Clone();
        var header = new byte[392];
        Array.Copy(data, header, header.Length);
        for (var index = 0; index < header.Length; index++)
            header[index] ^= (byte)(43 + index);

        foreach (var pass in Build1902Passes)
        {
            var offset = I32(header, pass.SourceOffset) + pass.Adjustment;
            var length = I32(header, pass.LengthField);
            if (offset < 0 || length < 0 || offset > output.Length - length)
                throw new InvalidDataException("Invalid build 1902 metadata section.");

            var seed = header[pass.SeedField];
            for (var index = 0; index < length; index++)
            {
                var key = pass.SubtractSeed
                    ? index - seed + pass.Key
                    : seed + index + pass.Key;
                output[offset + index] ^= (byte)key;
            }
        }

        var normalizedHeader = Convert.FromBase64String(Build1902NormalizedHeader);
        Array.Copy(normalizedHeader, output, normalizedHeader.Length);

        var digest = Convert.ToHexString(SHA256.HashData(output));
        if (!string.Equals(digest, Build1902NormalizedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Invalid build 1902 normalized metadata digest.");

        var outputPath = srcPath + ".dec";
        File.WriteAllBytes(outputPath, output);
        return outputPath;
    }

    private static string DecryptBuild1901(string srcPath, byte[] data)
    {
        var output = (byte[])data.Clone();

        DecryptBuild1901Section(output, 2411484, 8160952, 184, 89, true);
        DecryptBuild1901Section(output, 10595212, 916840, 104, 89, true);
        DecryptBuild1901Section(output, 35985508, 14336, 0, -89, false);
        DecryptBuild1901Section(output, 11512052, 10577216, 64, -89, false);
        DecryptBuild1901Section(output, 376, 434600, 168, -89, false);
        DecryptBuild1901Section(output, 434976, 1976508, 188, -89, false);
        DecryptBuild1901Section(output, 28294756, 1981644, 204, 89, true);

        var header = new byte[NewHdrSize];
        BitConverter.GetBytes(0xFAB11BAFu).CopyTo(header, 0);
        BitConverter.GetBytes(29).CopyTo(header, 4);
        SetHdr(header, 8, 376, 434600);
        SetHdr(header, 16, 434976, 1976508);
        SetHdr(header, 24, 2411484, 8160952);
        SetHdr(header, 32, 10572436, 22776);
        SetHdr(header, 40, 10595212, 916840);
        SetHdr(header, 48, 11512052, 10577216);
        SetHdr(header, 56, 22089268, 114228);
        SetHdr(header, 64, 22203496, 420420);
        SetHdr(header, 72, 22623916, 1457084);
        SetHdr(header, 80, 24081000, 147504);
        SetHdr(header, 88, 24228508, 4066248);
        SetHdr(header, 96, 28294756, 1981644);
        SetHdr(header, 104, 30276400, 205232);
        SetHdr(header, 112, 30481632, 11848);
        SetHdr(header, 120, 30493480, 118544);
        SetHdr(header, 128, 30612024, 56084);
        SetHdr(header, 136, 30668108, 63584);
        SetHdr(header, 144, 30731692, 1649848);
        SetHdr(header, 152, 32381540, 300992);
        SetHdr(header, 160, 32682532, 3294016);
        SetHdr(header, 168, 35976548, 8960);
        SetHdr(header, 176, 35985508, 14336);
        SetHdr(header, 184, 35999844, 8776);
        SetHdr(header, 192, 36008620, 6380);
        SetHdr(header, 200, 36015000, 1865927);
        SetHdr(header, 208, 37880928, 784568);
        SetHdr(header, 216, 38665496, 144828);
        SetHdr(header, 224, 38810324, 101568);
        SetHdr(header, 232, 38911892, 24);
        SetHdr(header, 240, 38911916, 3236);
        SetHdr(header, 248, 38915152, 10148);
        ValidateBuild1901Header(header, output.Length);
        Array.Copy(header, output, header.Length);

        var runtimeHeaderTail = new byte[]
        {
            0x4C, 0xFD, 0x4D, 0x02, 0x24, 0x19, 0x00, 0x00,
            0x68, 0xFD, 0x0D, 0x00, 0xA8, 0xED, 0xD4, 0x01,
            0x5C, 0xCC, 0x52, 0x01, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x8C, 0x01, 0x00, 0x44, 0x6A, 0x06, 0x00,
            0x3C, 0x18, 0x25, 0x02, 0xF4, 0xA2, 0x06, 0x00,
            0xA4, 0x27, 0x00, 0x00, 0xF0, 0x32, 0x50, 0x02,
            0x68, 0x2E, 0x00, 0x00, 0xF8, 0x58, 0x00, 0x00,
            0xB8, 0x8B, 0x25, 0x02, 0x40, 0x23, 0x00, 0x00,
            0xB0, 0x1C, 0xD1, 0x01, 0x70, 0xBF, 0x51, 0x02,
        };
        Array.Copy(runtimeHeaderTail, 0, output, NewHdrSize, runtimeHeaderTail.Length);

        var outputPath = srcPath + ".dec";
        File.WriteAllBytes(outputPath, output);
        return outputPath;
    }

    private static void DecryptBuild1901Section(byte[] output, int offset, int length, int seed,
                                                 int key, bool subtractSeed)
    {
        if (offset < 0 || length < 0 || offset > output.Length - length)
            throw new InvalidDataException("Invalid build 1901 metadata section.");

        for (var index = 0; index < length; index++)
        {
            var value = subtractSeed
                ? (byte)((index - seed + key) & 0xFF)
                : (byte)((seed + index + key) & 0xFF);
            output[offset + index] ^= value;
        }
    }

    private static void ValidateBuild1901Header(byte[] header, int fileLength)
    {
        var previousEnd = header.Length;
        for (var offset = 8; offset <= 248; offset += 8)
        {
            var sectionOffset = BitConverter.ToInt32(header, offset);
            var sectionSize = BitConverter.ToInt32(header, offset + 4);
            if (sectionOffset < header.Length || sectionSize < 0 ||
                sectionOffset > fileLength - sectionSize || sectionOffset < previousEnd)
                throw new InvalidDataException("Invalid normalized build 1901 metadata layout.");
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
