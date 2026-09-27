using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using Mono.Cecil;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace BepInEx;

#region BaseUnityPlugin




[AttributeUsage(AttributeTargets.Class)]
public class BepInPlugin : Attribute
{
    
    
    
    public BepInPlugin(string GUID, string Name, string Version)
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

    internal static BepInPlugin FromCecilType(TypeDefinition td)
    {
        var attr = MetadataHelper.GetCustomAttributes<BepInPlugin>(td, false).FirstOrDefault();

        if (attr == null)
            return null;

        return new BepInPlugin((string) attr.ConstructorArguments[0].Value,
                               (string) attr.ConstructorArguments[1].Value,
                               (string) attr.ConstructorArguments[2].Value);
    }
}




[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class BepInDependency : Attribute, ICacheable
{
    
    
    
    [Flags]
    public enum DependencyFlags
    {
        
        
        
        HardDependency = 1,

        
        
        
        SoftDependency = 2
    }

    
    
    
    
    
    
    
    public BepInDependency(string DependencyGUID, DependencyFlags Flags = DependencyFlags.HardDependency)
    {
        this.DependencyGUID = DependencyGUID;
        this.Flags = Flags;
        VersionRange = null;
    }

    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    public BepInDependency(string guid, string version) : this(guid)
    {
        VersionRange = Range.Parse(version);
    }

    
    
    
    public string DependencyGUID { get; protected set; }

    
    
    
    public DependencyFlags Flags { get; protected set; }

    
    
    
    public Range VersionRange { get; protected set; }

    void ICacheable.Save(BinaryWriter bw)
    {
        bw.Write(DependencyGUID);
        bw.Write((int) Flags);
        bw.Write(VersionRange?.ToString() ?? string.Empty);
    }

    void ICacheable.Load(BinaryReader br)
    {
        DependencyGUID = br.ReadString();
        Flags = (DependencyFlags) br.ReadInt32();

        var versionRange = br.ReadString();
        VersionRange = versionRange == string.Empty ? null : Range.Parse(versionRange);
    }

    internal static IEnumerable<BepInDependency> FromCecilType(TypeDefinition td)
    {
        var attrs = MetadataHelper.GetCustomAttributes<BepInDependency>(td, true);
        return attrs.Select(customAttribute =>
        {
            var dependencyGuid = (string) customAttribute.ConstructorArguments[0].Value;
            var secondArg = customAttribute.ConstructorArguments[1].Value;
            if (secondArg is string minVersion) return new BepInDependency(dependencyGuid, minVersion);
            return new BepInDependency(dependencyGuid, (DependencyFlags) secondArg);
        }).ToList();
    }
}




[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class BepInIncompatibility : Attribute, ICacheable
{
    
    
    
    
    
    public BepInIncompatibility(string IncompatibilityGUID)
    {
        this.IncompatibilityGUID = IncompatibilityGUID;
    }

    
    
    
    public string IncompatibilityGUID { get; protected set; }

    void ICacheable.Save(BinaryWriter bw) => bw.Write(IncompatibilityGUID);

    void ICacheable.Load(BinaryReader br) => IncompatibilityGUID = br.ReadString();

    internal static IEnumerable<BepInIncompatibility> FromCecilType(TypeDefinition td)
    {
        var attrs = MetadataHelper.GetCustomAttributes<BepInIncompatibility>(td, true);
        return attrs.Select(customAttribute =>
        {
            var dependencyGuid = (string) customAttribute.ConstructorArguments[0].Value;
            return new BepInIncompatibility(dependencyGuid);
        }).ToList();
    }
}





[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class BepInProcess : Attribute
{
    
    public BepInProcess(string ProcessName)
    {
        this.ProcessName = ProcessName;
    }

    
    
    
    public string ProcessName { get; protected set; }

    internal static List<BepInProcess> FromCecilType(TypeDefinition td)
    {
        var attrs = MetadataHelper.GetCustomAttributes<BepInProcess>(td, true);
        return attrs.Select(customAttribute =>
                                new BepInProcess((string) customAttribute.ConstructorArguments[0].Value)).ToList();
    }
}

#endregion

#region MetadataHelper




public static class MetadataHelper
{
    internal static IEnumerable<CustomAttribute> GetCustomAttributes<T>(TypeDefinition td, bool inherit)
        where T : Attribute
    {
        var result = new List<CustomAttribute>();
        var type = typeof(T);
        var currentType = td;

        do
        {
            result.AddRange(currentType.CustomAttributes.Where(ca => ca.AttributeType.FullName == type.FullName));
            currentType = currentType.BaseType?.Resolve();
        } while (inherit && currentType?.FullName != "System.Object");


        return result;
    }

    
    
    
    
    
    public static BepInPlugin GetMetadata(Type pluginType)
    {
        var attributes = pluginType.GetCustomAttributes(typeof(BepInPlugin), false);

        if (attributes.Length == 0)
            return null;

        return (BepInPlugin) attributes[0];
    }

    
    
    
    
    
    public static BepInPlugin GetMetadata(object plugin) => GetMetadata(plugin.GetType());

    
    
    
    
    
    
    public static T[] GetAttributes<T>(Type pluginType) where T : Attribute =>
        (T[]) pluginType.GetCustomAttributes(typeof(T), true);

    
    
    
    
    
    
    public static T[] GetAttributes<T>(Assembly assembly) where T : Attribute =>
        (T[]) assembly.GetCustomAttributes(typeof(T), true);

    
    
    
    
    
    
    public static IEnumerable<T> GetAttributes<T>(object plugin) where T : Attribute =>
        GetAttributes<T>(plugin.GetType());

    
    
    
    
    
    
    public static T[] GetAttributes<T>(MemberInfo member) where T : Attribute =>
        (T[]) member.GetCustomAttributes(typeof(T), true);

    
    
    
    
    
    public static IEnumerable<BepInDependency> GetDependencies(Type plugin) =>
        plugin.GetCustomAttributes(typeof(BepInDependency), true).Cast<BepInDependency>();
}

#endregion
