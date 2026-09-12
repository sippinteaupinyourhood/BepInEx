using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Common;
using BepInEx.Unity.IL2CPP.Hook;
using BepInEx.Unity.IL2CPP.Logging;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;
using Il2CppInterop.HarmonySupport;
using Il2CppInterop.Runtime.Startup;
using LibCpp2IL;
using Microsoft.Extensions.Logging;
using Mono.Cecil;
using MonoMod.Utils;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using MSLoggerFactory = Microsoft.Extensions.Logging.LoggerFactory;

namespace BepInEx.Unity.IL2CPP;

internal static partial class Il2CppInteropManager
{
    private sealed class Cpp2IlResult
    {
        public List<AsmResolver.DotNet.AssemblyDefinition> Assemblies { get; set; } = new();
        public Dictionary<string, GeneratorOptions.NativeTypeName> NativeTypeNames { get; } = new();
        public Dictionary<string, string> NativeFieldNames { get; } = new();
        public Dictionary<string, string> NativeMethodNames { get; } = new();
    }
    static Il2CppInteropManager()
    {
        InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
        InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);
        LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();
    }

    private static readonly ConfigEntry<bool> UpdateInteropAssemblies =
        ConfigFile.CoreConfig.Bind("IL2CPP",
                                   "UpdateInteropAssemblies",
                                   true,
                                   new StringBuilder()
                                       .AppendLine("Whether to run Il2CppInterop automatically to generate Il2Cpp support assemblies when they are outdated.")
                                       .AppendLine("If disabled assemblies in `BepInEx/interop` won't be updated between game or BepInEx updates!")
                                       .ToString());

    private static readonly ConfigEntry<bool> LowSpecMode = ConfigFile.CoreConfig.Bind(
        "IL2CPP", "LowSpecMode", false,
        "Limit IL2CPP generation and preload concurrency on systems with constrained memory or CPU resources.");

    private static readonly ConfigEntry<int> LowSpecMaxParallelism = ConfigFile.CoreConfig.Bind(
        "IL2CPP", "LowSpecMaxParallelism", 2,
        "Maximum Cpp2IL, Il2CppInterop, and preload concurrency when LowSpecMode is enabled.");

    private static readonly ConfigEntry<bool> LowSpecFastHashCheck = ConfigFile.CoreConfig.Bind(
        "IL2CPP", "LowSpecFastHashCheck", false,
        "Use file metadata instead of reading the full GameAssembly file during update checks when LowSpecMode is enabled.");

    private static readonly ConfigEntry<string> UnityBaseLibrariesSource = ConfigFile.CoreConfig.Bind(
     "IL2CPP", "UnityBaseLibrariesSource",
     "https://unity.bepinex.dev/libraries/{VERSION}.zip",
     new StringBuilder()
         .AppendLine("URL to a ZIP file with managed Unity base libraries. They are used by Il2CppInterop to generate interop assemblies.")
         .AppendLine("The URL can include {VERSION} template which will be replaced with the game's Unity engine version.")
         .AppendLine("If a .zip file with the same filename as the URL (after template replacement) already exists in unity-libs, it will be used instead of downloading a new copy.")
         .AppendLine("If you want to ensure BepInEx doesn't try to connect to the internet, set this to only the .zip filename (without a URL) and manually place the file in the unity-libs directory.")
         .ToString());

    private static readonly ConfigEntry<string> ConfigUnhollowerDeobfuscationRegex = ConfigFile.CoreConfig.Bind(
     "IL2CPP", "UnhollowerDeobfuscationRegex",
     string.Empty,
     new StringBuilder()
         .AppendLine("The RegEx string to pass to Il2CppAssemblyUnhollower for renaming obfuscated names.")
         .AppendLine("All types and members matching this RegEx will get a name based on their signature,")
         .AppendLine("resulting in names that persist after game updates.")
         .ToString());

    private static readonly ConfigEntry<bool> ScanMethodRefs = ConfigFile.CoreConfig.Bind(
     "IL2CPP", "ScanMethodRefs",
     Environment.Is64BitProcess,
     "If enabled, Il2CppInterop will use xref to find dead methods and generate CallerCount attributes.");

    private static readonly ConfigEntry<bool> DumpDummyAssemblies = ConfigFile.CoreConfig.Bind(
     "IL2CPP", "DumpDummyAssemblies",
     false,
     "If enabled, BepInEx will save dummy assemblies generated by an Cpp2IL dumper into BepInEx/dummy.");

    private static readonly ConfigEntry<string> IL2CPPInteropAssembliesPath = ConfigFile.CoreConfig.Bind(
     "IL2CPP", "IL2CPPInteropAssembliesPath",
     "{BepInEx}",
     new StringBuilder()
         .AppendLine("The path to the folder where IL2CPPInterop assemblies are stored.")
         .AppendLine("Supports the following placeholders:")
         .AppendLine("{BepInEx} - Path to the BepInEx folder.")
         .AppendLine("{ProcessName} - Name of the current process")
         .ToString());
    
    private static readonly ConfigEntry<bool> PreloadIL2CPPInteropAssemblies = ConfigFile.CoreConfig.Bind(
     "IL2CPP", "PreloadIL2CPPInteropAssemblies",
     true,
     new StringBuilder()
         .AppendLine("Automatically load all interop assemblies right before loading plugins.")
         .AppendLine("Some plugins may not work properly without this, but it may cause issues in some games.")
         .ToString());

    private static readonly ConfigEntry<string> GlobalMetadataPath = ConfigFile.CoreConfig.Bind(
     "IL2CPP", "GlobalMetadataPath",
     "{GameDataPath}/il2cpp_data/Metadata/global-metadata.dat",
     new StringBuilder()
         .AppendLine("The path to the IL2CPP metadata file.")
         .AppendLine("Supports the following placeholders:")
         .AppendLine("{BepInEx} - Path to the BepInEx folder.")
         .AppendLine("{ProcessName} - Name of the current process")
         .AppendLine("{GameDataPath} - Path to the game's Data folder.")
         .ToString());

    private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("InteropManager");

    private static string il2cppInteropBasePath;

    private static bool initialized;

    internal static string GlobalMetadataPathOverride;

    public static string GameAssemblyPath => Environment.GetEnvironmentVariable("BEPINEX_GAME_ASSEMBLY_PATH") ??
                                             Path.Combine(Paths.GameRootPath,
                                                          "GameAssembly." + PlatformHelper.LibrarySuffix);

    private static string HashPath => Path.Combine(IL2CPPInteropAssemblyPath, "assembly-hash.txt");

    private static string IL2CPPBasePath {
        get {
            if (il2cppInteropBasePath != null)
                return il2cppInteropBasePath;
            var path = Utility.GetCommandLineArgValue("--unhollowed-path") ?? IL2CPPInteropAssembliesPath.Value;
            il2cppInteropBasePath = path.Replace("{BepInEx}", Paths.BepInExRootPath)
                                     .Replace("{ProcessName}", Paths.ProcessName);
            return il2cppInteropBasePath;
        }
    }

    private static string UnityBaseLibsDirectory => Path.Combine(IL2CPPBasePath, "unity-libs");

    internal static string IL2CPPInteropAssemblyPath => Path.Combine(IL2CPPBasePath, "interop");

    private static string RenameMapPath => Path.Combine(Paths.BepInExRootPath, "DeobfuscationMap.csv.gz");

    private static ILoggerFactory LoggerFactory { get; } = MSLoggerFactory.Create(b =>
    {
        b.AddProvider(new BepInExLoggerProvider())
         .SetMinimumLevel(LogLevel.Trace); // Each BepInEx log listener has its own filtering
    });

    private static string ComputeHash()
    {
        using var md5 = MD5.Create();

        static void HashFile(ICryptoTransform hash, string file)
        {
            const int defaultCopyBufferSize = 81920;
            using var fs = File.OpenRead(file);
            var buffer = new byte[defaultCopyBufferSize];
            int read;
            while ((read = fs.Read(buffer)) > 0)
                hash.TransformBlock(buffer, 0, read, buffer, 0);
        }

        static void HashString(ICryptoTransform hash, string str)
        {
            var buffer = Encoding.UTF8.GetBytes(str);
            hash.TransformBlock(buffer, 0, buffer.Length, buffer, 0);
        }

        if (LowSpecMode.Value && LowSpecFastHashCheck.Value)
        {
            var gameAssembly = new FileInfo(GameAssemblyPath);
            HashString(md5, gameAssembly.Length.ToString());
            HashString(md5, gameAssembly.LastWriteTimeUtc.Ticks.ToString());
        }
        else
        {
            HashFile(md5, GameAssemblyPath);
        }

        if (Directory.Exists(UnityBaseLibsDirectory))
            foreach (var file in Directory.EnumerateFiles(UnityBaseLibsDirectory, "*.dll",
                                                          SearchOption.TopDirectoryOnly)
                                          .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                HashString(md5, Path.GetFileName(file));
                HashFile(md5, file);
            }

        if (File.Exists(RenameMapPath))
        {
            HashFile(md5, RenameMapPath);
        }

        HashFile(md5, typeof(InteropAssemblyGenerator).Assembly.Location);
        HashFile(md5, typeof(Cpp2IlApi).Assembly.Location);
        HashFile(md5, typeof(LibCpp2IlMain).Assembly.Location);
        HashString(md5, "protected-metadata-pipeline-v2");

        md5.TransformFinalBlock(new byte[0], 0, 0);

        return Utility.ByteArrayToString(md5.Hash);
    }

    private static bool CheckIfGenerationRequired()
    {
        static bool NeedGenerationOrSkip()
        {
            if (!UpdateInteropAssemblies.Value)
            {
                var hash = ComputeHash();
                Logger.LogWarning($"Interop assemblies are possibly out of date. To disable this message, create file {HashPath} with the following contents: {hash}");
                return false;
            }

            return true;
        }

        if (!Directory.Exists(IL2CPPInteropAssemblyPath))
            return true;

        if (!File.Exists(HashPath))
            return NeedGenerationOrSkip();

        if (ComputeHash() != File.ReadAllText(HashPath) && NeedGenerationOrSkip())
        {
            Logger.LogInfo("Detected outdated interop assemblies, will regenerate them now");
            return true;
        }

        return false;
    }

    private static Assembly ResolveInteropAssemblies(object sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name);
        return Utility.TryResolveDllAssembly(assemblyName, IL2CPPInteropAssemblyPath, out var foundAssembly)
                   ? foundAssembly
                   : null;
    }

    public static void Initialize()
    {
        if (initialized)
            throw new InvalidOperationException("Already initialized");
        initialized = true;

        Environment.SetEnvironmentVariable("IL2CPP_INTEROP_DATABASES_LOCATION", IL2CPPInteropAssemblyPath);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveInteropAssemblies;

        GenerateInteropAssemblies();
        var interopLogger = LoggerFactory.CreateLogger("Il2CppInterop");

        var unityVersion = UnityInfo.Version;
        Il2CppInteropRuntime.Create(new RuntimeConfiguration
                            {
                                UnityVersion = new Version(unityVersion.Major, unityVersion.Minor, unityVersion.Build),
                                DetourProvider = new Il2CppInteropDetourProvider()
                            })
                            .AddLogger(interopLogger)
                            .AddHarmonySupport()
                            .Start();
    }

    private static void GenerateInteropAssemblies()
    {
        if (!CheckIfGenerationRequired())
            return;

        var stagingPath = IL2CPPInteropAssemblyPath + ".new";
        var backupPath = IL2CPPInteropAssemblyPath + ".previous";
        try
        {
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, true);
            Directory.CreateDirectory(stagingPath);

            AppDomain.CurrentDomain.AddCecilPlatformAssemblies(UnityBaseLibsDirectory);
            DownloadUnityAssemblies();
            var cpp2IlResult = RunCpp2Il();
            var asmResolverAssemblies = cpp2IlResult.Assemblies;

            if (DumpDummyAssemblies.Value)
            {
                var dummyPath = Path.Combine(Paths.BepInExRootPath, "dummy");
                Directory.CreateDirectory(dummyPath);
                foreach (var assemblyDefinition in asmResolverAssemblies)
                    assemblyDefinition.Write(Path.Combine(dummyPath, $"{assemblyDefinition.Name}.dll"));
            }

            RunIl2CppInteropGenerator(cpp2IlResult, stagingPath);

            InteropAssemblyValidator.NormalizeDuplicateTypeNames(stagingPath);

            ValidateGeneratedInteropAssemblies(stagingPath, asmResolverAssemblies.Count);

            File.WriteAllText(Path.Combine(stagingPath, "assembly-hash.txt"), ComputeHash());

            if (Directory.Exists(backupPath))
                Directory.Delete(backupPath, true);
            if (Directory.Exists(IL2CPPInteropAssemblyPath))
                Directory.Move(IL2CPPInteropAssemblyPath, backupPath);

            try
            {
                Directory.Move(stagingPath, IL2CPPInteropAssemblyPath);
            }
            catch
            {
                if (!Directory.Exists(IL2CPPInteropAssemblyPath) && Directory.Exists(backupPath))
                    Directory.Move(backupPath, IL2CPPInteropAssemblyPath);
                throw;
            }

            try
            {
                if (Directory.Exists(backupPath))
                    Directory.Delete(backupPath, true);
            }
            catch (Exception cleanupError)
            {
                Logger.LogWarning($"Generated interop assemblies successfully, but could not remove the previous backup: {cleanupError.Message}");
            }
        }
        catch (Exception e)
        {
            try
            {
                if (Directory.Exists(stagingPath))
                    Directory.Delete(stagingPath, true);
            }
            catch
            {
            }
            Logger.LogError($"Failed to generate Il2Cpp interop assemblies: {e}");
        }
    }

    private static void ValidateGeneratedInteropAssemblies(string outputDirectory, int sourceAssemblyCount)
        => InteropAssemblyValidator.Validate(outputDirectory, sourceAssemblyCount);

    private static void DownloadUnityAssemblies() {
        var unityVersion = UnityInfo.Version;
        var version = $"{unityVersion.Major}.{unityVersion.Minor}.{unityVersion.Build}";
        var source = UnityBaseLibrariesSource.Value.Replace("{VERSION}", version);
        if (string.IsNullOrEmpty(source)) return;

        var baseFolder = Directory.CreateDirectory(UnityBaseLibsDirectory);
        baseFolder.EnumerateFiles("*.dll").Do(a=>a.Delete());

        var uriIsValid = Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        var fileName = Path.GetFileName(uriIsValid ? uri.AbsolutePath : source);
        var zipFilePath = Path.Combine(baseFolder.FullName, fileName);
        if (!File.Exists(zipFilePath))
        {
            // Check if URI is valid before attempting download
            if (!uriIsValid)
                throw new ArgumentException($"Unity base libraries source \"{source}\" is not a valid URL and the .zip file does not exist locally. Either provide a valid HTTP/HTTPS URL, or place the .zip file in the unity-libs directory.");

            Logger.LogMessage($"Downloading unity base libraries from {source}");
            using var httpClient = new HttpClient();

            Stream zipStream;
            try
            {
                zipStream = httpClient.GetStreamAsync(uri).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                throw new IOException(
                    $"Failed to download Unity base libraries for Unity {unityVersion} from {source}: {e.Message}. " +
                    $"Base libraries for this Unity version may not be available on the server yet. Download a matching " +
                    $"base-libraries .zip and place it in the \"{UnityBaseLibsDirectory}\" (unity-libs) directory, or set " +
                    $"the [IL2CPP] UnityBaseLibrariesSource config option to a valid URL.", e);
            }

            using (zipStream)
            {
                try
                {
                    using var writeStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    zipStream.CopyTo(writeStream);
                }
                catch
                {
                    // Delete the incomplete file to avoid issues on next startup
                    try { File.Delete(zipFilePath); } catch { }
                    throw;
                }
            }
        }

        Logger.LogMessage($"Extracting unity base libraries from {zipFilePath}");
        using var readStream = File.OpenRead(zipFilePath);
        using var zipArchive = new ZipArchive(readStream, ZipArchiveMode.Read);
        zipArchive.ExtractToDirectory(UnityBaseLibsDirectory);
    }

    private static Cpp2IlResult RunCpp2Il()
    {
        Cpp2IL.Core.Utils.ParallelismOptions.MaxDegreeOfParallelism = LowSpecMode.Value
            ? Math.Clamp(LowSpecMaxParallelism.Value, 1, 4)
            : -1;
        var metadataPath = GlobalMetadataPathOverride ?? Path.Combine(Paths.GameRootPath,
                                        GlobalMetadataPath.Value
                                                          .Replace("{BepInEx}", Paths.BepInExRootPath)
                                                          .Replace("{ProcessName}", Paths.ProcessName)
                                                          .Replace("{GameDataPath}", Paths.GameDataPath));
        
        Logger.LogMessage("Running Cpp2IL to generate dummy assemblies from " + metadataPath);

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var cpp2IlLogger = BepInEx.Logging.Logger.CreateLogSource("Cpp2IL");

        Cpp2IL.Core.Logging.Logger.VerboseLog += (message, s) =>
            cpp2IlLogger.LogDebug($"[{s}] {message.Trim()}");
        Cpp2IL.Core.Logging.Logger.InfoLog += (message, s) =>
            cpp2IlLogger.LogInfo($"[{s}] {message.Trim()}");
        Cpp2IL.Core.Logging.Logger.WarningLog += (message, s) =>
            cpp2IlLogger.LogWarning($"[{s}] {message.Trim()}");
        Cpp2IL.Core.Logging.Logger.ErrorLog += (message, s) =>
            cpp2IlLogger.LogError($"[{s}] {message.Trim()}");

        var unityVersion = UnityInfo.Version;
        Cpp2IlApi.InitializeLibCpp2Il(GameAssemblyPath, metadataPath, unityVersion, false);

        List<Cpp2IlProcessingLayer> processingLayers = new()
        {
            new StableRenamingProcessingLayer(),
            new AttributeInjectorProcessingLayer(),
        };

        foreach (var cpp2IlProcessingLayer in processingLayers)
        {
            cpp2IlProcessingLayer.PreProcess(Cpp2IlApi.CurrentAppContext, processingLayers);
        }

        foreach (var cpp2IlProcessingLayer in processingLayers)
        {
            cpp2IlProcessingLayer.Process(Cpp2IlApi.CurrentAppContext);
        }

        var result = new Cpp2IlResult
        {
            Assemblies = new AsmResolverDllOutputFormatDefault().BuildAssemblies(Cpp2IlApi.CurrentAppContext)
        };

        foreach (var type in Cpp2IlApi.CurrentAppContext.AllTypes.Where(type => type.Definition != null))
        {
            var assemblyName = type.DeclaringAssembly.Name;
            var typeKey = GeneratorOptions.NativeTypeKey(assemblyName, type.FullName);
            result.NativeTypeNames[typeKey] = new GeneratorOptions.NativeTypeName(type.DefaultNamespace, type.DefaultName);

            foreach (var field in type.Fields.Where(field => field.BackingData != null))
                result.NativeFieldNames[GeneratorOptions.NativeMemberKey(assemblyName, type.FullName, field.Name)] = field.DefaultName;

            foreach (var method in type.Methods.Where(method => method.Definition != null))
                result.NativeMethodNames[GeneratorOptions.NativeMemberKey(assemblyName, type.FullName, method.Name)] = method.DefaultName;
        }

        LibCpp2IlMain.Reset();
        Cpp2IlApi.CurrentAppContext = null;

        stopwatch.Stop();
        Logger.LogInfo($"Cpp2IL finished in {stopwatch.Elapsed}");

        return result;
    }

    private static void RunIl2CppInteropGenerator(Cpp2IlResult cpp2IlResult,
                                                   string outputDirectory)
    {
        var sourceAssemblies = cpp2IlResult.Assemblies;
        var opts = new GeneratorOptions
        {
            GameAssemblyPath = ScanMethodRefs.Value ? GameAssemblyPath : null,
            Source = sourceAssemblies,
            OutputDir = outputDirectory,
            UnityBaseLibsDir = Directory.Exists(UnityBaseLibsDirectory) ? UnityBaseLibsDirectory : null,
            ObfuscatedNamesRegex = !string.IsNullOrEmpty(ConfigUnhollowerDeobfuscationRegex.Value)
                                       ? new Regex(ConfigUnhollowerDeobfuscationRegex.Value)
                                       : null,
            Parallel = !LowSpecMode.Value,
        };
        opts.NamespacesAndAssembliesToPrefix.Add("Newtonsoft");
        foreach (var pair in cpp2IlResult.NativeTypeNames)
            opts.NativeTypeNames[pair.Key] = pair.Value;
        foreach (var pair in cpp2IlResult.NativeFieldNames)
            opts.NativeFieldNames[pair.Key] = pair.Value;
        foreach (var pair in cpp2IlResult.NativeMethodNames)
            opts.NativeMethodNames[pair.Key] = pair.Value;

        if (File.Exists(RenameMapPath))
        {
            Logger.LogInfo("Parsing deobfuscation rename mappings");
            opts.ReadRenameMap(RenameMapPath);
        }

        Logger.LogInfo("Generating interop assemblies");

        var logger = LoggerFactory.CreateLogger("Il2CppInteropGen");

        Il2CppInteropGenerator.Create(opts)
                              .AddLogger(logger)
                              .AddInteropAssemblyGenerator()
                              .Run();
    }

    internal static void PreloadInteropAssemblies()
    {
        if (!PreloadIL2CPPInteropAssemblies.Value)
            return;

        var sw = Stopwatch.StartNew();

        var files = Directory.EnumerateFiles(IL2CPPInteropAssemblyPath);
        var loaded = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = LowSpecMode.Value
                ? Math.Clamp(LowSpecMaxParallelism.Value, 1, 4)
                : -1
        };
        Parallel.ForEach(files, parallelOptions, file =>
        {
            if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)) return;
            if (name.Equals("Il2Cppnetstandard", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                // Do not use LoadFrom since it overrides preloader patches
                Assembly.Load(name);
                Interlocked.Increment(ref loaded);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Failed to preload {file} - {e}");
            }
        });

        Logger.LogDebug($"Preloaded {loaded} interop assemblies in {sw.ElapsedMilliseconds}ms");
    }
}
