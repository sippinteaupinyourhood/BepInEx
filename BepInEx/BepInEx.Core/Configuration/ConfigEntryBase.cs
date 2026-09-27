using System;
using System.IO;
using System.Linq;
using BepInEx.Logging;

namespace BepInEx.Configuration;





public sealed class ConfigEntry<T> : ConfigEntryBase
{
    private T _typedValue;

    internal ConfigEntry(ConfigFile configFile,
                         ConfigDefinition definition,
                         T defaultValue,
                         ConfigDescription configDescription) : base(configFile, definition, typeof(T),
                                                                     defaultValue, configDescription)
    {
        configFile.SettingChanged += (sender, args) =>
        {
            if (args.ChangedSetting == this) SettingChanged?.Invoke(sender, args);
        };
    }

    
    
    
    public T Value
    {
        get => _typedValue;
        set
        {
            value = ClampValue(value);
            if (Equals(_typedValue, value))
                return;

            _typedValue = value;
            OnSettingChanged(this);
        }
    }

    
    public override object BoxedValue
    {
        get => Value;
        set => Value = (T) value;
    }

    
    
    
    public event EventHandler SettingChanged;
}





public abstract class ConfigEntryBase
{
    
    
    
    internal protected ConfigEntryBase(ConfigFile configFile,
                             ConfigDefinition definition,
                             Type settingType,
                             object defaultValue,
                             ConfigDescription configDescription)
    {
        ConfigFile = configFile ?? throw new ArgumentNullException(nameof(configFile));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        SettingType = settingType ?? throw new ArgumentNullException(nameof(settingType));

        Description = configDescription ?? ConfigDescription.Empty;
        if (Description.AcceptableValues != null &&
            !SettingType.IsAssignableFrom(Description.AcceptableValues.ValueType))
            throw new
                ArgumentException("configDescription.AcceptableValues is for a different type than the type of this setting");

        DefaultValue = defaultValue;

        
        BoxedValue = defaultValue;
    }

    
    
    
    public ConfigFile ConfigFile { get; }

    
    
    
    
    public ConfigDefinition Definition { get; }

    
    
    
    public ConfigDescription Description { get; }

    
    
    
    public Type SettingType { get; }

    
    
    
    public object DefaultValue { get; }

    
    
    
    public abstract object BoxedValue { get; set; }

    
    
    
    public string GetSerializedValue() => TomlTypeConverter.ConvertToString(BoxedValue, SettingType);

    
    
    
    public void SetSerializedValue(string value)
    {
        try
        {
            var newValue = TomlTypeConverter.ConvertToValue(value, SettingType);
            BoxedValue = newValue;
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Warning,
                       $"Config value of setting \"{Definition}\" could not be parsed and will be ignored. Reason: {e.Message}; Value: {value}");
        }
    }

    
    
    
    protected T ClampValue<T>(T value)
    {
        if (Description.AcceptableValues != null)
            return (T) Description.AcceptableValues.Clamp(value);
        return value;
    }

    
    
    
    protected void OnSettingChanged(object sender) => ConfigFile.OnSettingChanged(sender, this);

    
    
    
    public void WriteDescription(StreamWriter writer)
    {
        if (!string.IsNullOrEmpty(Description.Description))
            writer.WriteLine($"## {Description.Description.Replace("\n", "\n## ")}");

        writer.WriteLine($"# Setting type: {SettingType.Name}");

        writer.WriteLine($"# Default value: {TomlTypeConverter.ConvertToString(DefaultValue, SettingType)}");

        if (Description.AcceptableValues != null)
        {
            writer.WriteLine(Description.AcceptableValues.ToDescriptionString());
        }
        else if (SettingType.IsEnum)
        {
            writer.WriteLine($"# Acceptable values: {string.Join(", ", Enum.GetNames(SettingType))}");

            if (SettingType.GetCustomAttributes(typeof(FlagsAttribute), true).Any())
                writer.WriteLine("# Multiple values can be set at the same time by separating them with , (e.g. Debug, Warning)");
        }
    }
}
