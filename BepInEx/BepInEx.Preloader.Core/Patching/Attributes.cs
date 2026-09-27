using System;
using System.Linq;
using Mono.Cecil;
using Version = SemanticVersioning.Version;

namespace BepInEx.Preloader.Core.Patching;




[AttributeUsage(AttributeTargets.Class)]
public class PatcherPluginInfoAttribute : Attribute
{
    
    
    
    public PatcherPluginInfoAttribute(string GUID, string Name, string Version)
    {
        this.GUID = GUID;
        this.Name = Name;
        this.Version = TryParseLongVersion(Version);
    }

    
    
    
    public string GUID { get; protected set; }


    
    
    
    public string Name { get; protected set; }


    
    
    
    public Version Version { get; protected set; }

    private static Version TryParseLongVersion(string version)
    {
        if (Version.TryParse(version, out var v))
            return v;

        
        try
        {
            var longVersion = new System.Version(version);

            return new Version(longVersion.Major, longVersion.Minor,
                               longVersion.Build != -1 ? longVersion.Build : 0);
        }
        catch { }

        return null;
    }

    internal static PatcherPluginInfoAttribute FromCecilType(TypeDefinition td)
    {
        var attr = MetadataHelper.GetCustomAttributes<PatcherPluginInfoAttribute>(td, false).FirstOrDefault();

        if (attr == null)
            return null;

        return new PatcherPluginInfoAttribute((string) attr.ConstructorArguments[0].Value,
                                              (string) attr.ConstructorArguments[1].Value,
                                              (string) attr.ConstructorArguments[2].Value);
    }

    internal static PatcherPluginInfoAttribute FromType(Type type)
    {
        var attributes = type.GetCustomAttributes(typeof(PatcherPluginInfoAttribute), false);

        if (attributes.Length == 0)
            return null;

        return (PatcherPluginInfoAttribute) attributes[0];
    }
}




[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class TargetAssemblyAttribute : Attribute
{
    
    
    
    public const string AllAssemblies = "_all";

    
    
    
    
    public TargetAssemblyAttribute(string targetAssembly)
    {
        TargetAssembly = targetAssembly;
    }

    
    
    
    public string TargetAssembly { get; }
}




[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class TargetTypeAttribute : Attribute
{
    
    
    public TargetTypeAttribute(string targetAssembly, string targetType)
    {
        TargetAssembly = targetAssembly;
        TargetType = targetType;
    }

    
    
    
    public string TargetAssembly { get; }

    
    
    
    public string TargetType { get; }
}
