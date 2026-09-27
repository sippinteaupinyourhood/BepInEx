using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Preloader.Core;
using BepInEx.Preloader.Core.Logging;
using BepInEx.Unity.IL2CPP.Hook;
using BepInEx.Unity.IL2CPP.Logging;
using BepInEx.Unity.IL2CPP.Utils;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace BepInEx.Unity.IL2CPP;

public class IL2CPPChainloader : BaseChainloader<BasePlugin>
{
    private static RuntimeInvokeDetourDelegate originalInvoke;
    private static int startupGate;
    private static long hookInstalledAt;

    private static readonly ConfigEntry<bool> ConfigUnityLogging = ConfigFile.CoreConfig.Bind(
     "Logging", "UnityLogListening",
     true,
     "Enables showing unity log messages in the BepInEx logging system.");

    private static readonly ConfigEntry<bool> ConfigDiskWriteUnityLog = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "WriteUnityLog",
     false,
     "Include unity log messages in log file output.");


    private static INativeDetour RuntimeInvokeDetour { get; set; }

    public static IL2CPPChainloader Instance { get; set; }

    
    
    
    
    
    public static T AddUnityComponent<T>() where T : Il2CppObjectBase => AddUnityComponent(typeof(T)).Cast<T>();

    
    
    
    
    
    public static Il2CppObjectBase AddUnityComponent(Type t) => Il2CppUtils.AddComponent(t);

    
    
    
    public event Action<PluginInfo, Assembly, BasePlugin> PluginLoad;

    public override void Initialize(string gameExePath = null)
    {
        base.Initialize(gameExePath);
        Instance = this;

        if (!NativeLibrary.TryLoad("GameAssembly", typeof(IL2CPPChainloader).Assembly, null, out var il2CppHandle))
        {
            Logger.Log(LogLevel.Fatal,
                       "Could not locate Il2Cpp game assembly (GameAssembly.dll, UserAssembly.dll or libil2cpp.so). The game might be obfuscated or use a yet unsupported build of Unity.");
            return;
        }

        var runtimeInvokePtr = ResolveRuntimeInvoke(il2CppHandle);
        var directInvokePtr = FollowRelativeJump(runtimeInvokePtr);
        RuntimeInvokeDetourDelegate invokeMethodDetour = OnInvokeMethod;
        RuntimeInvokeDetour =
            INativeDetour.CreateAndApply(directInvokePtr, invokeMethodDetour, out originalInvoke);
        hookInstalledAt = Environment.TickCount64;
    }

    private static IntPtr ResolveRuntimeInvoke(IntPtr il2CppHandle)
    {
        if (NativeLibrary.TryGetExport(il2CppHandle, "il2cpp_runtime_invoke", out var address))
            return address;

        var mapPath = Path.Combine(Paths.GameRootPath, "bepinex-il2cpp-signature-scan.json");
        if (File.Exists(mapPath))
        {
            var json = File.ReadAllText(mapPath);
            var match = Regex.Match(json,
                "\\\"api\\\"\\s*:\\s*\\\"il2cpp_runtime_invoke\\\"(?:(?!\\\"api\\\")[\\s\\S])*?\\\"export\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"(?:(?!\\\"api\\\")[\\s\\S])*?\\\"status\\\"\\s*:\\s*\\\"(?:unique|resolved)\\\"",
                RegexOptions.IgnoreCase);
            if (match.Success && NativeLibrary.TryGetExport(il2CppHandle, match.Groups[1].Value, out address))
                return address;
        }

        if (Il2CppInterop.Runtime.Il2CppExportMap.TryGetAlias("il2cpp_runtime_invoke", out var alias) &&
            NativeLibrary.TryGetExport(il2CppHandle, alias, out address))
            return address;

        if (NativeLibrary.TryGetExport(il2CppHandle, "BkPyAyKoENa", out address) ||
            NativeLibrary.TryGetExport(il2CppHandle, "bXpRzmesLff", out address) ||
            NativeLibrary.TryGetExport(il2CppHandle, "QUnIxupvmev", out address) ||
            NativeLibrary.TryGetExport(il2CppHandle, "oBGsYjJdmzU", out address))
            return address;

        throw new EntryPointNotFoundException("Unable to resolve il2cpp_runtime_invoke");
    }

    private static IntPtr FollowRelativeJump(IntPtr address)
    {
        if (Marshal.ReadByte(address) != 0xE9)
            return address;

        var displacement = Marshal.ReadInt32(address, 1);
        return IntPtr.Add(address, 5 + displacement);
    }

    private static bool IsChainloaderTrigger(IntPtr method)
    {
        if (method == IntPtr.Zero)
            return false;

        var name = Marshal.PtrToStringUTF8(Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name(method));
        if (name != "Internal_ActiveSceneChanged")
            return false;

        var declaringType = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_class(method);
        return declaringType != IntPtr.Zero
               && Marshal.PtrToStringUTF8(Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name(declaringType)) == "SceneManager"
               && Marshal.PtrToStringUTF8(Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_namespace(declaringType)) == "UnityEngine.SceneManagement"
               && Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method) == 2
               && !Il2CppInterop.Runtime.IL2CPP.il2cpp_method_is_instance(method);
    }

    private static void StartChainloader()
    {
        try
        {
            Logger.Log(LogLevel.Info, "Starting interop preload");
            Il2CppInteropManager.PreloadInteropAssemblies();
            Logger.Log(LogLevel.Info, "Starting Unity logging setup");
            SetupUnityLogging();
            Logger.Log(LogLevel.Info, "Starting plugin execution");
            Instance.Execute();
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Fatal, "Unable to execute IL2CPP chainloader, no plugins will be loaded");
            Logger.Log(LogLevel.Error, ex);
        }
    }

    private static IntPtr OnInvokeMethod(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc)
    {
        if (Interlocked.CompareExchange(ref startupGate, 1, 0) != 0)
            return originalInvoke(method, obj, parameters, exc);

        var unhook = false;
        try
        {
            unhook = method != IntPtr.Zero && Environment.TickCount64 - hookInstalledAt >= 10_000;
        }
        finally
        {
            if (!unhook)
                Volatile.Write(ref startupGate, 0);
        }

        var result = originalInvoke(method, obj, parameters, exc);

        if (unhook)
        {
            RuntimeInvokeDetour.Dispose();

            PreloaderLogger.Log.Log(LogLevel.Debug, "Runtime invoke unpatched");
            StartChainloader();
        }

        return result;
    }

    
    private static void SetupUnityLogging()
    {
        if (!ConfigUnityLogging.Value)
            return;

        Logger.Sources.Add(new IL2CPPUnityLogSource());

        Application.CallLogCallback("Test call after applying unity logging hook", "", LogType.Assert, true);
    }

    protected override void InitializeLoggers()
    {
        base.InitializeLoggers();

        if (!ConfigDiskWriteUnityLog.Value) DiskLogListener.BlacklistedSources.Add("Unity");

        ChainloaderLogHelper.RewritePreloaderLogs();

        Logger.Sources.Add(new IL2CPPLogSource());
    }

    public override BasePlugin LoadPlugin(PluginInfo pluginInfo, Assembly pluginAssembly)
    {
        Logger.Log(LogLevel.Info, $"Resolving plugin type {pluginInfo.TypeName}");
        var type = pluginAssembly.GetType(pluginInfo.TypeName);

        Logger.Log(LogLevel.Info, $"Constructing plugin {pluginInfo.Metadata.Name}");
        var pluginInstance = (BasePlugin) Activator.CreateInstance(type);

        Logger.Log(LogLevel.Info, $"Invoking plugin-load handlers for {pluginInfo.Metadata.Name}");
        PluginLoad?.Invoke(pluginInfo, pluginAssembly, pluginInstance);
        Logger.Log(LogLevel.Info, $"Entering Load() for {pluginInfo.Metadata.Name}");
        pluginInstance.Load();
        Logger.Log(LogLevel.Info, $"Load() returned for {pluginInfo.Metadata.Name}");

        return pluginInstance;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr RuntimeInvokeDetourDelegate(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc);

}
