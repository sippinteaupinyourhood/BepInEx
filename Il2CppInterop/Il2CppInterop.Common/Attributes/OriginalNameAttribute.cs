namespace Il2CppInterop.Common.Attributes;




[AttributeUsage(AttributeTargets.Enum, Inherited = false)]
public class OriginalNameAttribute : Attribute
{
    public readonly string AssemblyName;
    public readonly string Namespace;
    public readonly string Name;

    public OriginalNameAttribute(string assemblyName, string @namespace, string name)
    {
        AssemblyName = assemblyName;
        Namespace = @namespace;
        Name = name;
    }
}
