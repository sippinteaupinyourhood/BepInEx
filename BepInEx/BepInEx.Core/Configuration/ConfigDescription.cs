using System;

namespace BepInEx.Configuration;




public class ConfigDescription
{
    
    
    
    
    
    
    
    
    
    public ConfigDescription(string description, AcceptableValueBase acceptableValues = null, params object[] tags)
    {
        AcceptableValues = acceptableValues;
        Tags = tags;
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    
    
    
    public string Description { get; }

    
    
    
    public AcceptableValueBase AcceptableValues { get; }

    
    
    
    public object[] Tags { get; }

    
    
    
    public static ConfigDescription Empty { get; } = new("");
}
