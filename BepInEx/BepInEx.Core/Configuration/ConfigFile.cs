using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using BepInEx.Logging;

namespace BepInEx.Configuration;




public class ConfigFile : IDictionary<ConfigDefinition, ConfigEntryBase>
{
    private readonly BepInPlugin _ownerMetadata;

    
    public ConfigFile(string configPath, bool saveOnInit) : this(configPath, saveOnInit, null) { }

    
    
    
    
    
    
    public ConfigFile(string configPath, bool saveOnInit, BepInPlugin ownerMetadata)
    {
        _ownerMetadata = ownerMetadata;

        if (configPath == null) throw new ArgumentNullException(nameof(configPath));
        configPath = Path.GetFullPath(configPath);
        ConfigFilePath = configPath;

        if (File.Exists(ConfigFilePath))
            Reload();
        else if (saveOnInit) TrySave();
    }

    public static ConfigFile CoreConfig { get; } = new(Paths.BepInExConfigPath, true);

    
    
    
    protected Dictionary<ConfigDefinition, ConfigEntryBase> Entries { get; } = new();

    private Dictionary<ConfigDefinition, string> OrphanedEntries { get; } = new();

    
    
    
    [Obsolete("Use Keys instead")]
    public ReadOnlyCollection<ConfigDefinition> ConfigDefinitions
    {
        get
        {
            lock (_ioLock)
            {
                return Entries.Keys.ToList().AsReadOnly();
            }
        }
    }

    
    
    
    
    public string ConfigFilePath { get; }

    
    
    
    
    public bool SaveOnConfigSet { get; set; } = true;

    
    public ConfigEntryBase this[ConfigDefinition key]
    {
        get
        {
            lock (_ioLock)
            {
                return Entries[key];
            }
        }
    }

    
    
    
    
    public ConfigEntryBase this[string section, string key] => this[new ConfigDefinition(section, key)];

    
    public IEnumerator<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetEnumerator()
    {
        
        lock (_ioLock)
            return Entries.ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>.Add(
        KeyValuePair<ConfigDefinition, ConfigEntryBase> item)
    {
        lock (_ioLock)
        {
            Entries.Add(item.Key, item.Value);
        }
    }

    
    public bool Contains(KeyValuePair<ConfigDefinition, ConfigEntryBase> item)
    {
        lock (_ioLock)
        {
            return ((ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>) Entries).Contains(item);
        }
    }

    void ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>.CopyTo(
        KeyValuePair<ConfigDefinition, ConfigEntryBase>[] array,
        int arrayIndex)
    {
        lock (_ioLock)
        {
            ((ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>) Entries).CopyTo(array, arrayIndex);
        }
    }

    bool ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>.Remove(
        KeyValuePair<ConfigDefinition, ConfigEntryBase> item)
    {
        lock (_ioLock)
        {
            return Entries.Remove(item.Key);
        }
    }

    
    public int Count
    {
        get
        {
            lock (_ioLock)
            {
                return Entries.Count;
            }
        }
    }

    
    public bool IsReadOnly => false;

    
    public bool ContainsKey(ConfigDefinition key)
    {
        lock (_ioLock)
        {
            return Entries.ContainsKey(key);
        }
    }

    
    public void Add(ConfigDefinition key, ConfigEntryBase value) =>
        throw new InvalidOperationException("Directly adding a config entry is not supported");

    
    public bool Remove(ConfigDefinition key)
    {
        lock (_ioLock)
        {
            return Entries.Remove(key);
        }
    }

    
    public void Clear()
    {
        lock (_ioLock)
        {
            Entries.Clear();
        }
    }

    bool IDictionary<ConfigDefinition, ConfigEntryBase>.TryGetValue(ConfigDefinition key, out ConfigEntryBase value)
    {
        lock (_ioLock)
        {
            return Entries.TryGetValue(key, out value);
        }
    }

    
    ConfigEntryBase IDictionary<ConfigDefinition, ConfigEntryBase>.this[ConfigDefinition key]
    {
        get
        {
            lock (_ioLock)
            {
                return Entries[key];
            }
        }
        set => throw new InvalidOperationException("Directly setting a config entry is not supported");
    }

    
    
    
    
    public ICollection<ConfigDefinition> Keys
    {
        get
        {
            lock (_ioLock)
            {
                return Entries.Keys.ToArray();
            }
        }
    }

    
    
    
    
    public ICollection<ConfigEntryBase> Values
    {
        get
        {
            lock (_ioLock)
            {
                return Entries.Values.ToArray();
            }
        }
    }

    
    
    
    
    
    
    [Obsolete("Use Values instead")]
    public ConfigEntryBase[] GetConfigEntries()
    {
        lock (_ioLock)
        {
            return Entries.Values.ToArray();
        }
    }

    #region Save/Load

    private readonly object _ioLock = new();

    
    
    
    public bool GenerateSettingDescriptions { get; set; } = true;

    
    
    
    public void Reload()
    {
        lock (_ioLock)
        {
            OrphanedEntries.Clear();

            var currentSection = string.Empty;

            foreach (var rawLine in File.ReadAllLines(ConfigFilePath))
            {
                var line = rawLine.Trim();

                if (line.StartsWith("#")) 
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]")) 
                {
                    currentSection = line.Substring(1, line.Length - 2);
                    continue;
                }

                var split = line.Split(new[] { '=' }, 2); 
                if (split.Length != 2)
                    continue; 

                var currentKey = split[0].Trim();
                var currentValue = split[1].Trim();

                var definition = new ConfigDefinition(currentSection, currentKey);

                Entries.TryGetValue(definition, out var entry);

                if (entry != null)
                    entry.SetSerializedValue(currentValue);
                else
                    OrphanedEntries[definition] = currentValue;
            }
        }

        OnConfigReloaded();
    }

    
    
    
    public void Save()
    {
        lock (_ioLock)
        {
            var directoryName = Path.GetDirectoryName(ConfigFilePath);
            if (directoryName != null) Directory.CreateDirectory(directoryName);

            using (var writer = new StreamWriter(ConfigFilePath, false, Utility.UTF8NoBom))
            {
                if (_ownerMetadata != null)
                {
                    writer.WriteLine($"## Settings file was created by plugin {_ownerMetadata.Name} v{_ownerMetadata.Version}");
                    writer.WriteLine($"## Plugin GUID: {_ownerMetadata.GUID}");
                    writer.WriteLine();
                }

                var allConfigEntries = Entries
                                       .Select(x => new
                                       {
                                           x.Key, entry = x.Value, value = x.Value.GetSerializedValue()
                                       })
                                       .Concat(OrphanedEntries.Select(x => new
                                       {
                                           x.Key, entry = (ConfigEntryBase) null, value = x.Value
                                       }));

                foreach (var sectionKv in allConfigEntries.GroupBy(x => x.Key.Section).OrderBy(x => x.Key))
                {
                    
                    writer.WriteLine($"[{sectionKv.Key}]");

                    foreach (var configEntry in sectionKv)
                    {
                        if (GenerateSettingDescriptions)
                        {
                            writer.WriteLine();
                            configEntry.entry?.WriteDescription(writer);
                        }

                        writer.WriteLine($"{configEntry.Key.Key} = {configEntry.value}");
                    }

                    writer.WriteLine();
                }
            }
        }
    }

    
    
    
    
    
    private void TrySave()
    {
        try
        {
            Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Logger.Log(LogLevel.Warning,
                $"Could not write config file {ConfigFilePath}: {e.Message}. Continuing with the in-memory configuration.");
        }
    }

    #endregion

    #region Wraps

    
    
    
    
    
    
    
    [Obsolete("Use ConfigFile[key] or TryGetEntry instead")]
    public ConfigEntry<T> GetSetting<T>(ConfigDefinition configDefinition) =>
        TryGetEntry<T>(configDefinition, out var entry)
            ? entry
            : null;

    
    
    
    
    
    
    
    
    [Obsolete("Use ConfigFile[key] or TryGetEntry instead")]
    public ConfigEntry<T> GetSetting<T>(string section, string key) =>
        TryGetEntry<T>(section, key, out var entry)
            ? entry
            : null;

    
    
    
    
    
    
    
    
    
    public bool TryGetEntry<T>(ConfigDefinition configDefinition, out ConfigEntry<T> entry)
    {
        lock (_ioLock)
        {
            if (Entries.TryGetValue(configDefinition, out var rawEntry))
            {
                entry = (ConfigEntry<T>) rawEntry;
                return true;
            }

            entry = null;
            return false;
        }
    }

    
    
    
    
    
    
    
    
    
    
    public bool TryGetEntry<T>(string section, string key, out ConfigEntry<T> entry) =>
        TryGetEntry(new ConfigDefinition(section, key), out entry);

    
    
    
    
    
    
    
    
    public ConfigEntry<T> Bind<T>(ConfigDefinition configDefinition,
                                  T defaultValue,
                                  ConfigDescription configDescription = null)
    {
        if (!TomlTypeConverter.CanConvert(typeof(T)))
            throw new
                ArgumentException($"Type {typeof(T)} is not supported by the config system. Supported types: {string.Join(", ", TomlTypeConverter.GetSupportedTypes().Select(x => x.Name).ToArray())}");

        lock (_ioLock)
        {
            if (Entries.TryGetValue(configDefinition, out var rawEntry))
                return (ConfigEntry<T>) rawEntry;

            var entry = new ConfigEntry<T>(this, configDefinition, defaultValue, configDescription);

            Entries[configDefinition] = entry;

            if (OrphanedEntries.TryGetValue(configDefinition, out var homelessValue))
            {
                entry.SetSerializedValue(homelessValue);
                OrphanedEntries.Remove(configDefinition);
            }

            if (SaveOnConfigSet)
                TrySave();

            return entry;
        }
    }

    
    
    
    
    
    
    
    
    
    
    public ConfigEntry<T> Bind<T>(string section,
                                  string key,
                                  T defaultValue,
                                  ConfigDescription configDescription = null) =>
        Bind(new ConfigDefinition(section, key), defaultValue, configDescription);

    
    
    
    
    
    
    
    
    
    
    public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description) =>
        Bind(new ConfigDefinition(section, key), defaultValue, new ConfigDescription(description));

    
    
    
    
    
    
    
    
    [Obsolete("Use Bind instead")]
    public ConfigEntry<T> AddSetting<T>(ConfigDefinition configDefinition,
                                        T defaultValue,
                                        ConfigDescription configDescription = null) =>
        Bind(configDefinition, defaultValue, configDescription);

    
    
    
    
    
    
    
    
    
    
    [Obsolete("Use Bind instead")]
    public ConfigEntry<T> AddSetting<T>(string section,
                                        string key,
                                        T defaultValue,
                                        ConfigDescription configDescription = null) =>
        Bind(new ConfigDefinition(section, key), defaultValue, configDescription);

    
    
    
    
    
    
    
    
    
    
    [Obsolete("Use Bind instead")]
    public ConfigEntry<T> AddSetting<T>(string section, string key, T defaultValue, string description) =>
        Bind(new ConfigDefinition(section, key), defaultValue, new ConfigDescription(description));

    
    
    
    [Obsolete("Use Bind instead")]
    public ConfigWrapper<T> Wrap<T>(string section, string key, string description = null, T defaultValue = default)
    {
        lock (_ioLock)
        {
            var definition = new ConfigDefinition(section, key, description);
            var setting = Bind(definition, defaultValue,
                               string.IsNullOrEmpty(description) ? null : new ConfigDescription(description));
            return new ConfigWrapper<T>(setting);
        }
    }

    
    
    
    [Obsolete("Use Bind instead")]
    public ConfigWrapper<T> Wrap<T>(ConfigDefinition configDefinition, T defaultValue = default) =>
        Wrap(configDefinition.Section, configDefinition.Key, null, defaultValue);

    #endregion

    #region Events

    
    
    
    public event EventHandler ConfigReloaded;

    
    
    
    public event EventHandler<SettingChangedEventArgs> SettingChanged;

    internal void OnSettingChanged(object sender, ConfigEntryBase changedEntryBase)
    {
        if (changedEntryBase == null) throw new ArgumentNullException(nameof(changedEntryBase));

        if (SaveOnConfigSet)
            Save();

        var settingChanged = SettingChanged;
        if (settingChanged == null) return;

        var args = new SettingChangedEventArgs(changedEntryBase);
        foreach (EventHandler<SettingChangedEventArgs> callback in settingChanged.GetInvocationList())
            try
            {
                callback(sender, args);
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e);
            }
    }

    private void OnConfigReloaded()
    {
        var configReloaded = ConfigReloaded;
        if (configReloaded == null) return;

        foreach (EventHandler callback in configReloaded.GetInvocationList())
            try
            {
                callback(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e);
            }
    }

    #endregion
}
