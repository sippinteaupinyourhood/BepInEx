using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;

namespace BepInEx.Bootstrap;





public interface ICacheable
{
    
    
    
    
    void Save(BinaryWriter bw);

    
    
    
    
    void Load(BinaryReader br);
}





public class CachedAssembly<T> where T : ICacheable
{
    
    
    
    public List<T> CacheItems { get; set; }

    
    
    
    public string Hash { get; set; }
}




public static class TypeLoader
{
    
    
    
    public static readonly DefaultAssemblyResolver CecilResolver;

    
    
    
    public static readonly ReaderParameters ReaderParameters;

    public static HashSet<string> SearchDirectories = new();

    #region Config

    private static readonly ConfigEntry<bool> EnableAssemblyCache = ConfigFile.CoreConfig.Bind(
     "Caching", "EnableAssemblyCache",
     true,
     "Enable/disable assembly metadata cache\nEnabling this will speed up discovery of plugins and patchers by caching the metadata of all types BepInEx discovers.");

    #endregion

    static TypeLoader()
    {
        CecilResolver = new DefaultAssemblyResolver();
        ReaderParameters = new ReaderParameters { AssemblyResolver = CecilResolver };

        CecilResolver.ResolveFailure += CecilResolveOnFailure;
    }

    public static AssemblyDefinition CecilResolveOnFailure(object sender, AssemblyNameReference reference)
    {
        if (!Utility.TryParseAssemblyName(reference.FullName, out var name))
            return null;

        var resolveDirs = new[]
        {
            Paths.BepInExAssemblyDirectory,
            Paths.PluginPath,
            Paths.PatcherPluginPath,
            Paths.ManagedPath
        }.Concat(SearchDirectories);

        foreach (var dir in resolveDirs)
        {
            if (!Directory.Exists(dir))
            {
                Logger.Log(LogLevel.Debug, $"Unable to resolve cecil search directory '{dir}'");
                continue;
            }

            if (Utility.TryResolveDllAssembly(name, dir, ReaderParameters, out var assembly))
                return assembly;
        }

        return AssemblyResolve?.Invoke(sender, reference);
    }

    
    
    
    public static event AssemblyResolveEventHandler AssemblyResolve;

    
    
    
    
    
    
    
    
    
    
    
    
    public static Dictionary<string, List<T>> FindPluginTypes<T>(string directory,
                                                                 Func<TypeDefinition, string, T> typeSelector,
                                                                 Func<AssemblyDefinition, bool> assemblyFilter =
                                                                     null,
                                                                 string cacheName = null)
        where T : ICacheable, new()
    {
        var result = new Dictionary<string, List<T>>();
        var hashes = new Dictionary<string, string>();
        Dictionary<string, CachedAssembly<T>> cache = null;

        if (cacheName != null)
            cache = LoadAssemblyCache<T>(cacheName);

        foreach (var dll in Directory.GetFiles(Path.GetFullPath(directory), "*.dll", SearchOption.AllDirectories))
            try
            {
                using var dllMs = new MemoryStream(File.ReadAllBytes(dll));
                var hash = Utility.HashStream(dllMs);
                hashes[dll] = hash;
                dllMs.Position = 0;
                if (cache != null && cache.TryGetValue(dll, out var cacheEntry))
                    if (hash == cacheEntry.Hash)
                    {
                        result[dll] = cacheEntry.CacheItems;
                        continue;
                    }

                using var ass = AssemblyDefinition.ReadAssembly(dllMs, ReaderParameters);
                Logger.Log(LogLevel.Debug, $"Examining '{dll}'");

                if (!assemblyFilter?.Invoke(ass) ?? false)
                {
                    result[dll] = new List<T>();
                    continue;
                }

                var matches = ass.MainModule.Types
                                 .Select(t => typeSelector(t, dll))
                                 .Where(t => t != null).ToList();
                result[dll] = matches;
            }
            catch (BadImageFormatException e)
            {
                Logger.Log(LogLevel.Debug,
                           $"Skipping loading {dll} because it's not a valid .NET assembly. Full error: {e.Message}");
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e);
            }

        if (cacheName != null)
            SaveAssemblyCache(cacheName, result, hashes);

        return result;
    }

    
    
    
    
    
    
    
    
    
    public static Dictionary<string, CachedAssembly<T>> LoadAssemblyCache<T>(string cacheName)
        where T : ICacheable, new()
    {
        if (!EnableAssemblyCache.Value)
            return null;

        var result = new Dictionary<string, CachedAssembly<T>>();
        try
        {
            var path = Path.Combine(Paths.CachePath, $"{cacheName}_typeloader.dat");
            if (!File.Exists(path))
                return null;

            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                var entriesCount = br.ReadInt32();

                for (var i = 0; i < entriesCount; i++)
                {
                    var entryIdentifier = br.ReadString();
                    var hash = br.ReadString();
                    var itemsCount = br.ReadInt32();
                    var items = new List<T>();

                    for (var j = 0; j < itemsCount; j++)
                    {
                        var entry = new T();
                        entry.Load(br);
                        items.Add(entry);
                    }

                    result[entryIdentifier] = new CachedAssembly<T> { Hash = hash, CacheItems = items };
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning,
                       $"Failed to load cache \"{cacheName}\"; skipping loading cache. Reason: {e.Message}.");
        }

        return result;
    }

    
    
    
    
    
    
    
    public static void SaveAssemblyCache<T>(string cacheName,
                                            Dictionary<string, List<T>> entries,
                                            Dictionary<string, string> hashes)
        where T : ICacheable
    {
        if (!EnableAssemblyCache.Value)
            return;

        try
        {
            if (!Directory.Exists(Paths.CachePath))
                Directory.CreateDirectory(Paths.CachePath);

            var path = Path.Combine(Paths.CachePath, $"{cacheName}_typeloader.dat");

            using var bw = new BinaryWriter(File.OpenWrite(path));
            bw.Write(entries.Count);

            foreach (var kv in entries)
            {
                bw.Write(kv.Key);
                bw.Write(hashes.TryGetValue(kv.Key, out var hash) ? hash : "");
                bw.Write(kv.Value.Count);

                foreach (var item in kv.Value)
                    item.Save(bw);
            }
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning,
                       $"Failed to save cache \"{cacheName}\"; skipping saving cache. Reason: {e.Message}.");
        }
    }

    
    
    
    
    
    public static string TypeLoadExceptionToString(ReflectionTypeLoadException ex)
    {
        var sb = new StringBuilder();
        foreach (var exSub in ex.LoaderExceptions)
        {
            sb.AppendLine(exSub.Message);
            if (exSub is FileNotFoundException exFileNotFound)
            {
                if (!string.IsNullOrEmpty(exFileNotFound.FusionLog))
                {
                    sb.AppendLine("Fusion Log:");
                    sb.AppendLine(exFileNotFound.FusionLog);
                }
            }
            else if (exSub is FileLoadException exLoad)
            {
                if (!string.IsNullOrEmpty(exLoad.FusionLog))
                {
                    sb.AppendLine("Fusion Log:");
                    sb.AppendLine(exLoad.FusionLog);
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
