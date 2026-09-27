using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;

namespace BepInEx.Preloader.Core.Patching;




public class PatchDefinition
{
    public PatchDefinition(TargetAssemblyAttribute targetAssembly, BasePatcher instance, MethodInfo methodInfo)
    {
        TargetAssembly = targetAssembly;
        Instance = instance;
        MethodInfo = methodInfo;

        FullName = $"{MethodInfo.DeclaringType.FullName}/{MethodInfo.Name} -> {TargetAssembly.TargetAssembly}";
    }

    public PatchDefinition(TargetTypeAttribute targetType, BasePatcher instance, MethodInfo methodInfo)
    {
        TargetType = targetType;
        Instance = instance;
        MethodInfo = methodInfo;

        FullName =
            $"{MethodInfo.DeclaringType.FullName}/{MethodInfo.Name} -> {TargetType.TargetAssembly}/{TargetType.TargetType}";
    }

    
    
    
    public TargetAssemblyAttribute TargetAssembly { get; }

    
    
    
    public TargetTypeAttribute TargetType { get; }

    
    
    
    public BasePatcher Instance { get; }

    
    
    
    public MethodInfo MethodInfo { get; }

    
    
    
    public string FullName { get; }
}




public class PatcherContext
{
    
    
    
    
    
    
    
    
    
    public Dictionary<string, AssemblyDefinition> AvailableAssemblies { get; } = new();

    
    
    
    public Dictionary<string, string> AvailableAssembliesPaths { get; } = new();

    
    
    
    
    
    
    
    public Dictionary<string, Assembly> LoadedAssemblies { get; } = new();

    
    
    
    public List<BasePatcher> PatcherPlugins { get; } = new();

    
    
    
    
    public List<PatchDefinition> PatchDefinitions { get; } = new();

    
    
    
    
    public string DumpedAssembliesPath { get; internal set; }
}
