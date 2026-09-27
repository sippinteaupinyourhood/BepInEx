using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;

namespace BepInEx.Bootstrap;

public abstract class BaseChainloader<TPlugin>
{
    protected static readonly string CurrentAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;
    protected static readonly Version CurrentAssemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;

    private static Regex allowedGuidRegex { get; } = new(@"^[a-zA-Z0-9\._\-]+$");

    
    
    
    
    
    
    public static PluginInfo ToPluginInfo(TypeDefinition type, string assemblyLocation)
    {
        if (type.IsInterface || type.IsAbstract)
            return null;

        try
        {
            if (!type.IsSubtypeOf(typeof(TPlugin)))
                return null;
        }
        catch (AssemblyResolutionException)
        {
            
            return null;
        }

        var metadata = BepInPlugin.FromCecilType(type);

        
        if (metadata == null)
        {
            Logger.Log(LogLevel.Warning, $"Skipping over type [{type.FullName}] as no metadata attribute is specified");
            return null;
        }

        if (string.IsNullOrEmpty(metadata.GUID) || !allowedGuidRegex.IsMatch(metadata.GUID))
        {
            Logger.Log(LogLevel.Warning,
                       $"Skipping type [{type.FullName}] because its GUID [{metadata.GUID}] is of an illegal format.");
            return null;
        }

        if (metadata.Version == null)
        {
            Logger.Log(LogLevel.Warning, $"Skipping type [{type.FullName}] because its version is invalid.");
            return null;
        }

        if (metadata.Name == null)
        {
            Logger.Log(LogLevel.Warning, $"Skipping type [{type.FullName}] because its name is null.");
            return null;
        }

        var filters = BepInProcess.FromCecilType(type);
        var dependencies = BepInDependency.FromCecilType(type);
        var incompatibilities = BepInIncompatibility.FromCecilType(type);

        var bepinVersion =
            type.Module.AssemblyReferences.FirstOrDefault(reference => reference.Name == "BepInEx.Core")?.Version ??
            new Version();

        return new PluginInfo
        {
            Metadata = metadata,
            Processes = filters,
            Dependencies = dependencies,
            Incompatibilities = incompatibilities,
            TypeName = type.FullName,
            TargettedBepInExVersion = bepinVersion,
            Location = assemblyLocation
        };
    }

    protected static bool HasBepinPlugins(AssemblyDefinition ass)
    {
        if (ass.MainModule.AssemblyReferences.All(r => r.Name != CurrentAssemblyName))
            return false;
        if (ass.MainModule.GetTypeReferences().All(r => r.FullName != typeof(BepInPlugin).FullName))
            return false;

        return true;
    }

    protected static bool PluginTargetsWrongBepin(PluginInfo pluginInfo)
    {
        var pluginTarget = pluginInfo.TargettedBepInExVersion;
        
        if (pluginTarget.Major != CurrentAssemblyVersion.Major) return true;
        if (pluginTarget.Minor > CurrentAssemblyVersion.Minor) return true;
        if (pluginTarget.Minor < CurrentAssemblyVersion.Minor) return false;
        return pluginTarget.Build > CurrentAssemblyVersion.Build;
    }

    #region Contract

    protected virtual string ConsoleTitle => $"BepInEx {Paths.BepInExVersion} - {Paths.ProcessName}";

    private bool _initialized;

    
    
    
    public Dictionary<string, PluginInfo> Plugins { get; } = new();

    
    
    
    
    public List<string> DependencyErrors { get; } = new();

    
    
    
    public event Action<PluginInfo> PluginLoaded;

    
    
    
    public event Action Finished;

    public virtual void Initialize(string gameExePath = null)
    {
        if (_initialized)
            throw new InvalidOperationException("Chainloader cannot be initialized multiple times");

        
        if (gameExePath != null)
            
            
            Paths.SetExecutablePath(gameExePath);

        InitializeLoggers();

        if (!Directory.Exists(Paths.PluginPath))
            Directory.CreateDirectory(Paths.PluginPath);

        if (!Directory.Exists(Paths.PatcherPluginPath))
            Directory.CreateDirectory(Paths.PatcherPluginPath);

        _initialized = true;

        Logger.Log(LogLevel.Message, "Chainloader initialized");
    }

    protected virtual void InitializeLoggers()
    {
        if (ConsoleManager.ConsoleEnabled && !ConsoleManager.ConsoleActive)
            ConsoleManager.CreateConsole();

        if (ConsoleManager.ConsoleActive)
        {
            if (!Logger.Listeners.Any(x => x is ConsoleLogListener))
                Logger.Listeners.Add(new ConsoleLogListener());

            ConsoleManager.SetConsoleTitle(ConsoleTitle);
        }

        if (ConfigDiskLogging.Value)
            Logger.Listeners.Add(new DiskLogListener("LogOutput.log", ConfigDiskLoggingDisplayedLevel.Value,
                                                     ConfigDiskAppend.Value, ConfigDiskLoggingInstantFlushing.Value,
                                                     ConfigDiskLoggingFileLimit.Value));

        if (!TraceLogSource.IsListening)
            Logger.Sources.Add(TraceLogSource.CreateSource());

        if (!Logger.Sources.Any(x => x is HarmonyLogSource))
            Logger.Sources.Add(new HarmonyLogSource());
    }

    
    
    
    
    
    
    
    
    
    protected IList<PluginInfo> DiscoverPluginsFrom(string path, string cacheName = "chainloader")
    {
        var pluginsToLoad =
            TypeLoader.FindPluginTypes(path, ToPluginInfo, HasBepinPlugins, cacheName);
        return pluginsToLoad.SelectMany(p => p.Value).ToList();
    }

    
    
    
    
    protected virtual IList<PluginInfo> DiscoverPlugins()
    {
        return DiscoverPluginsFrom(Paths.PluginPath);
    }

    
    
    
    
    
    
    protected virtual IList<PluginInfo> ModifyLoadOrder(IList<PluginInfo> plugins)
    {
        
        var dependencyDict =
            new SortedDictionary<string, IEnumerable<string>>(StringComparer.InvariantCultureIgnoreCase);
        var pluginsByGuid = new Dictionary<string, PluginInfo>();

        foreach (var pluginInfoGroup in plugins.GroupBy(info => info.Metadata.GUID))
        {
            if (Plugins.TryGetValue(pluginInfoGroup.Key, out var loadedPlugin))
            {
                Logger.Log(LogLevel.Warning,
                           $"Skipping [{pluginInfoGroup.Key}] because a plugin with a similar GUID ([{loadedPlugin}]) has been already loaded.");
                continue;
            }

            PluginInfo loadedVersion = null;
            foreach (var pluginInfo in pluginInfoGroup.OrderByDescending(x => x.Metadata.Version))
            {
                if (loadedVersion != null)
                {
                    Logger.Log(LogLevel.Warning,
                               $"Skipping [{pluginInfo}] because a newer version exists ({loadedVersion})");
                    continue;
                }

                
                var filters = pluginInfo.Processes.ToList();
                var invalidProcessName = filters.Count != 0 &&
                                         filters.All(x => !string.Equals(x.ProcessName.Replace(".exe", ""),
                                                                         Paths.ProcessName,
                                                                         StringComparison
                                                                             .InvariantCultureIgnoreCase));

                if (invalidProcessName)
                {
                    Logger.Log(LogLevel.Warning,
                               $"Skipping [{pluginInfo}] because of process filters ({string.Join(", ", pluginInfo.Processes.Select(p => p.ProcessName).ToArray())})");
                    continue;
                }

                loadedVersion = pluginInfo;
                dependencyDict[pluginInfo.Metadata.GUID] = pluginInfo.Dependencies.Select(d => d.DependencyGUID);
                pluginsByGuid[pluginInfo.Metadata.GUID] = pluginInfo;
            }
        }

        foreach (var pluginInfo in pluginsByGuid.Values.ToList())
            if (pluginInfo.Incompatibilities.Any(incompatibility =>
                                                     pluginsByGuid.ContainsKey(incompatibility.IncompatibilityGUID)
                                                  || Plugins.ContainsKey(incompatibility.IncompatibilityGUID))
               )
            {
                pluginsByGuid.Remove(pluginInfo.Metadata.GUID);
                dependencyDict.Remove(pluginInfo.Metadata.GUID);

                var incompatiblePluginsNew = pluginInfo.Incompatibilities.Select(x => x.IncompatibilityGUID)
                                                       .Where(x => pluginsByGuid.ContainsKey(x));
                var incompatiblePluginsExisting = pluginInfo.Incompatibilities.Select(x => x.IncompatibilityGUID)
                                                            .Where(x => Plugins.ContainsKey(x));
                var incompatiblePlugins = incompatiblePluginsNew.Concat(incompatiblePluginsExisting).ToArray();
                var message =
                    $@"Could not load [{pluginInfo}] because it is incompatible with: {string.Join(", ", incompatiblePlugins)}";
                DependencyErrors.Add(message);
                Logger.Log(LogLevel.Error, message);
            }
            else if (PluginTargetsWrongBepin(pluginInfo))
            {
                var message =
                    $@"Plugin [{pluginInfo}] targets a wrong version of BepInEx ({pluginInfo.TargettedBepInExVersion}) and might not work until you update";
                DependencyErrors.Add(message);
                Logger.Log(LogLevel.Warning, message);
            }

        

        var emptyDependencies = new string[0];

        
        
        var sortedPlugins = Utility.TopologicalSort(dependencyDict.Keys,
                                                    x =>
                                                        dependencyDict.TryGetValue(x, out var deps)
                                                            ? deps
                                                            : emptyDependencies).ToList();

        return sortedPlugins.Where(pluginsByGuid.ContainsKey).Select(x => pluginsByGuid[x]).ToList();
    }

    
    
    
    public virtual void Execute()
    {
        try
        {
            var plugins = DiscoverPlugins();
            Logger.Log(LogLevel.Info, $"{plugins.Count} plugin{(plugins.Count == 1 ? "" : "s")} to load");
            LoadPlugins(plugins);

            try
            {
                Finished?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, $"Error occurred in a Chainloader.Finished event handler: {ExceptionFormatter.Format(ex)}");
            }
        }
        catch (Exception ex)
        {
            try
            {
                ConsoleManager.CreateConsole();
            }
            catch { }

            Logger.Log(LogLevel.Error, $"Error occurred loading plugins: {ExceptionFormatter.Format(ex)}");
        }

        Logger.Log(LogLevel.Message, "Chainloader startup complete");
    }

    private IList<PluginInfo> LoadPlugins(IList<PluginInfo> plugins)
    {
        var sortedPlugins = ModifyLoadOrder(plugins);

        var invalidPlugins = new HashSet<string>();
        var processedPlugins = new Dictionary<string, SemanticVersioning.Version>();
        var loadedAssemblies = new Dictionary<string, Assembly>();
        var loadedPlugins = new List<PluginInfo>();

        foreach (var plugin in sortedPlugins)
        {
            var dependsOnInvalidPlugin = false;
            var missingDependencies = new List<BepInDependency>();
            var incompatibleDependencies = new List<KeyValuePair<BepInDependency, string>>();
            foreach (var dependency in plugin.Dependencies)
            {
                static bool IsHardDependency(BepInDependency dep) =>
                    (dep.Flags & BepInDependency.DependencyFlags.HardDependency) != 0;

                
                var dependencyExists =
                    processedPlugins.TryGetValue(dependency.DependencyGUID, out var pluginVersion);
                
                if (!dependencyExists)
                {
                    dependencyExists = Plugins.TryGetValue(dependency.DependencyGUID, out var pluginInfo);
                    pluginVersion = pluginInfo?.Metadata.Version;
                }

                
                if (!dependencyExists)
                {
                    if (IsHardDependency(dependency))
                        missingDependencies.Add(dependency);
                    continue;
                }

                
                if (dependency.VersionRange != null && !dependency.VersionRange.IsSatisfied(pluginVersion))
                {
                    if (IsHardDependency(dependency))
                        incompatibleDependencies.Add(new KeyValuePair<BepInDependency, string>(dependency, pluginVersion?.ToString()));
                    continue;
                }

                
                if (invalidPlugins.Contains(dependency.DependencyGUID) && IsHardDependency(dependency))
                {
                    dependsOnInvalidPlugin = true;
                    break;
                }
            }

            processedPlugins.Add(plugin.Metadata.GUID, plugin.Metadata.Version);

            if (dependsOnInvalidPlugin)
            {
                var message =
                    $"Skipping [{plugin}] because it has a dependency that was not loaded. See previous errors for details.";
                DependencyErrors.Add(message);
                Logger.Log(LogLevel.Warning, message);
                continue;
            }

            if (missingDependencies.Count != 0 || incompatibleDependencies.Count != 0)
            {
                if (missingDependencies.Count != 0)
                {
                    var message = $@"Could not load [{plugin}] because it has missing dependencies: {
                        string.Join(", ", missingDependencies.Select(s => s.VersionRange == null ? s.DependencyGUID : $"{s.DependencyGUID} ({s.VersionRange})").ToArray())
                    }. Install the listed plugin(s) and restart the game.";
                    DependencyErrors.Add(message);
                    Logger.Log(LogLevel.Error, message);
                }

                if (incompatibleDependencies.Count != 0)
                {
                    var message = $@"Could not load [{plugin}] because the following dependencies are installed with an incompatible version: {
                        string.Join(", ", incompatibleDependencies.Select(s => $"{s.Key.DependencyGUID} (found {s.Value}, requires {s.Key.VersionRange})").ToArray())
                    }. Update the listed plugin(s) to a version that satisfies the requirement.";
                    DependencyErrors.Add(message);
                    Logger.Log(LogLevel.Error, message);
                }

                invalidPlugins.Add(plugin.Metadata.GUID);
                continue;
            }

            try
            {
                Logger.Log(LogLevel.Info, $"Loading [{plugin}]");

                if (!loadedAssemblies.TryGetValue(plugin.Location, out var ass))
                    loadedAssemblies[plugin.Location] = ass = Assembly.LoadFrom(plugin.Location);

                Plugins[plugin.Metadata.GUID] = plugin;
                TryRunModuleCtor(plugin, ass);
                plugin.Instance = LoadPlugin(plugin, ass);
                loadedPlugins.Add(plugin);

                PluginLoaded?.Invoke(plugin);
            }
            catch (Exception ex)
            {
                invalidPlugins.Add(plugin.Metadata.GUID);
                Plugins.Remove(plugin.Metadata.GUID);

                Logger.Log(LogLevel.Error,
                           $"Error loading [{plugin}]: {ExceptionFormatter.Format(ex)}");
            }
        }

        return loadedPlugins;
    }

    
    
    
    
    
    
    
    
    
    public IList<PluginInfo> LoadPlugins(params string[] pluginsPaths)
    {
        
        var plugins = new List<PluginInfo>();
        foreach (var pluginsPath in pluginsPaths)
            plugins.AddRange(DiscoverPluginsFrom(pluginsPath));
        return LoadPlugins(plugins);
    }

    private static void TryRunModuleCtor(PluginInfo plugin, Assembly assembly)
    {
        try
        {
            RuntimeHelpers.RunModuleConstructor(assembly.GetType(plugin.TypeName).Module.ModuleHandle);
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning,
                       $"Couldn't run Module constructor for {assembly.FullName}::{plugin.TypeName}: {ExceptionFormatter.Format(e)}");
        }
    }

    public abstract TPlugin LoadPlugin(PluginInfo pluginInfo, Assembly pluginAssembly);

    #endregion

    #region Config

    private static readonly ConfigEntry<bool> ConfigDiskAppend = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "AppendLog",
     false,
     "Appends to the log file instead of overwriting, on game startup.");

    private static readonly ConfigEntry<bool> ConfigDiskLogging = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "Enabled",
     true,
     "Enables writing log messages to disk.");

    private static readonly ConfigEntry<LogLevel> ConfigDiskLoggingDisplayedLevel = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "LogLevels",
     LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info,
     "Only displays the specified log levels in the disk log output.");

    private static readonly ConfigEntry<bool> ConfigDiskLoggingInstantFlushing = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "InstantFlushing",
     false,
     new StringBuilder()
         .AppendLine("If true, instantly writes any received log entries to disk.")
         .AppendLine("This incurs a major performance hit if a lot of log messages are being written, however it is really useful for debugging crashes.")
         .ToString());

    private static readonly ConfigEntry<int> ConfigDiskLoggingFileLimit = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "ConcurrentFileLimit",
     5,
     new StringBuilder()
         .AppendLine("The maximum amount of concurrent log files that will be written to disk.")
         .AppendLine("As one log file is used per open game instance, you may find it necessary to increase this limit when debugging multiple instances at the same time.")
         .ToString());

    #endregion
}
