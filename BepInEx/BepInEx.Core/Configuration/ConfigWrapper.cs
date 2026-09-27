using System;

namespace BepInEx.Configuration;





[Obsolete("Use ConfigFile from new Bind overloads instead")]
public sealed class ConfigWrapper<T>
{
    internal ConfigWrapper(ConfigEntry<T> configEntry)
    {
        ConfigEntry = configEntry ?? throw new ArgumentNullException(nameof(configEntry));

        configEntry.ConfigFile.SettingChanged += (sender, args) =>
        {
            if (args.ChangedSetting == configEntry) SettingChanged?.Invoke(sender, args);
        };
    }

    
    
    
    public ConfigEntry<T> ConfigEntry { get; }

    
    
    
    public ConfigDefinition Definition => ConfigEntry.Definition;

    
    
    
    public ConfigFile ConfigFile => ConfigEntry.ConfigFile;

    
    
    
    public T Value
    {
        get => ConfigEntry.Value;
        set => ConfigEntry.Value = value;
    }

    
    
    
    public event EventHandler SettingChanged;
}
