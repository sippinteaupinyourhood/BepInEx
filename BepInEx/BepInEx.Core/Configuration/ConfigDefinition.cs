using System;
using System.Linq;

namespace BepInEx.Configuration;







public class ConfigDefinition : IEquatable<ConfigDefinition>
{
    private static readonly char[] _invalidConfigChars = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

    
    
    
    
    
    public ConfigDefinition(string section, string key)
    {
        CheckInvalidConfigChars(section, nameof(section));
        CheckInvalidConfigChars(key, nameof(key));
        Key = key;
        Section = section;
    }

    
    [Obsolete("description argument is no longer used, put it in a ConfigDescription instead")]
    public ConfigDefinition(string section, string key, string description)
    {
        Key = key ?? "";
        Section = section ?? "";
    }

    
    
    
    public string Section { get; }

    
    
    
    public string Key { get; }

    
    
    
    
    public bool Equals(ConfigDefinition other)
    {
        if (other == null) return false;
        return string.Equals(Key, other.Key)
            && string.Equals(Section, other.Section);
    }

    private static void CheckInvalidConfigChars(string val, string name)
    {
        if (val == null) throw new ArgumentNullException(name);
        if (val != val.Trim())
            throw new ArgumentException("Cannot use whitespace characters at start or end of section and key names",
                                        name);
        if (val.Any(c => _invalidConfigChars.Contains(c)))
            throw new
                ArgumentException(@"Cannot use any of the following characters in section and key names: = \n \t \ "" ' [ ]",
                                  name);
    }

    
    
    
    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
            return false;
        if (ReferenceEquals(this, obj))
            return true;

        return Equals(obj as ConfigDefinition);
    }

    
    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = Key != null ? Key.GetHashCode() : 0;
            hashCode = (hashCode * 397) ^ (Section != null ? Section.GetHashCode() : 0);
            return hashCode;
        }
    }

    
    
    
    public static bool operator ==(ConfigDefinition left, ConfigDefinition right) => Equals(left, right);

    
    
    
    public static bool operator !=(ConfigDefinition left, ConfigDefinition right) => !Equals(left, right);

    
    public override string ToString() => Section + "." + Key;
}
