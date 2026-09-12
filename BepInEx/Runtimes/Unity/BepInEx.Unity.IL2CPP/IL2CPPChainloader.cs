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
    private static RuntimeInvokeDetourDelegate directInvoke;
    private static int startupGate;

    private static readonly ConfigEntry<bool> ConfigUnityLogging = ConfigFile.CoreConfig.Bind(
     "Logging", "UnityLogListening",
     false,
     "Enables showing unity log messages in the BepInEx logging system.");

    private static readonly ConfigEntry<bool> ConfigDiskWriteUnityLog = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "WriteUnityLog",
     false,
     "Include unity log messages in log file output.");


    private static INativeDetour RuntimeInvokeDetour { get; set; }

    public static IL2CPPChainloader Instance { get; set; }

    /// <summary>
    ///     Register and add a Unity Component (for example MonoBehaviour) into BepInEx global manager.
    ///     Automatically registers the type with Il2Cpp type system if it isn't initialised already.
    /// </summary>
    /// <typeparam name="T">Type of the component to add.</typeparam>
    public static T AddUnityComponent<T>() where T : Il2CppObjectBase => AddUnityComponent(typeof(T)).Cast<T>();

    /// <summary>
    ///     Register and add a Unity Component (for example MonoBehaviour) into BepInEx global manager.
    ///     Automatically registers the type with Il2Cpp type system if it isn't initialised already.
    /// </summary>
    /// <param name="t">Type of the component to add</param>
    public static Il2CppObjectBase AddUnityComponent(Type t) => Il2CppUtils.AddComponent(t);

    /// <summary>
    ///     Occurs after a plugin is instantiated and just before <see cref="BasePlugin.Load"/> is called.
    /// </summary>
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
        directInvoke = Marshal.GetDelegateForFunctionPointer<RuntimeInvokeDetourDelegate>(directInvokePtr);
        RuntimeInvokeDetourDelegate invokeMethodDetour = OnInvokeMethod;
        RuntimeInvokeDetour =
            INativeDetour.CreateAndApply(runtimeInvokePtr, invokeMethodDetour, out originalInvoke);
    }

    private static IntPtr ResolveRuntimeInvoke(IntPtr il2CppHandle)
    {
        if (NativeLibrary.TryGetExport(il2CppHandle, "il2cpp_runtime_invoke", out var address))
            return address;

        if (NativeLibrary.TryGetExport(il2CppHandle, "UPSmEOKKvNa", out address))
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

        if (NativeLibrary.TryGetExport(il2CppHandle, "QUnIxupvmev", out address) ||
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
        try
        {
            var methodName = Marshal.PtrToStringAnsi(Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name(method));
            if (methodName != "Internal_ActiveSceneChanged") return false;

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Fatal, "Unable to execute IL2CPP chainloader, no plugins will be loaded");
            Logger.Log(LogLevel.Error, ex);
            return false;
        }
    }

    private static void StartChainloader()
    {
        try
        {
            SetupUnityLogging();
            Il2CppInteropManager.PreloadInteropAssemblies();
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
            return directInvoke(method, obj, parameters, exc);

        var unhook = false;
        try
        {
            unhook = IsChainloaderTrigger(method);
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

    // JIT-compiled only when called here, so OnInvokeMethod needs no interop assemblies present to JIT.
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
        var type = pluginAssembly.GetType(pluginInfo.TypeName);

        var pluginInstance = (BasePlugin) Activator.CreateInstance(type);

        PluginLoad?.Invoke(pluginInfo, pluginAssembly, pluginInstance);
        pluginInstance.Load();

        return pluginInstance;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr RuntimeInvokeDetourDelegate(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc);

}
