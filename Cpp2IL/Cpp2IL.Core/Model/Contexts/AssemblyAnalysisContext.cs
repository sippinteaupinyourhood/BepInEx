using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;




public class AssemblyAnalysisContext : HasCustomAttributesAndName
{
    
    
    
    public Il2CppAssemblyDefinition? Definition { get; set; }

    
    
    
    public List<TypeAnalysisContext> Types = [];

    
    
    
    public IEnumerable<TypeAnalysisContext> TopLevelTypes => Types.Where(t => t.DeclaringType == null);

    
    
    
    
    
    public Il2CppCodeGenModule? CodeGenModule;

    public virtual Version DefaultVersion
    {
        get
        {
            
            return Definition is null || Definition.AssemblyName.build < 0
                ? new(0,0,0,0)
                : new(Definition.AssemblyName.major, Definition.AssemblyName.minor, Definition.AssemblyName.build, Definition.AssemblyName.revision);
        }
    }
    public Version? OverrideVersion { get; set; }
    public Version Version
    {
        get => OverrideVersion ?? DefaultVersion;
        set => OverrideVersion = value;
    }

    public virtual uint DefaultHashAlgorithm => Definition?.AssemblyName.hash_alg ?? default;
    public uint? OverrideHashAlgorithm { get; set; }
    public uint HashAlgorithm
    {
        get => OverrideHashAlgorithm ?? DefaultHashAlgorithm;
        set => OverrideHashAlgorithm = value;
    }

    public virtual uint DefaultFlags => Definition?.AssemblyName.flags ?? default;
    public uint? OverrideFlags { get; set; }
    public uint Flags
    {
        get => OverrideFlags ?? DefaultFlags;
        set => OverrideFlags = value;
    }

    public virtual string? DefaultCulture => Definition?.AssemblyName.Culture;
    
    
    
    
    
    
    public string? OverrideCulture { get; set; }
    
    
    
    
    
    
    public string? Culture
    {
        get
        {
            var culture = OverrideCulture ?? DefaultCulture;
            return string.IsNullOrEmpty(culture) ? null : culture;
        }
        set
        {
            OverrideCulture = value is null ? "" : value;
        }
    }

    public virtual byte[]? DefaultPublicKeyToken => Definition?.AssemblyName.PublicKeyToken;
    
    
    
    
    
    
    public byte[]? OverridePublicKeyToken { get; set; }
    
    
    
    
    
    
    public byte[]? PublicKeyToken
    {
        get
        {
            var data = OverridePublicKeyToken ?? DefaultPublicKeyToken;
            return data is null || data.Length == 0 ? null : data;
        }
        set
        {
            OverridePublicKeyToken = value is null ? [] : value;
        }
    }

    public virtual byte[]? DefaultPublicKey => Definition?.AssemblyName.PublicKey;
    
    
    
    
    
    
    public byte[]? OverridePublicKey { get; set; }
    
    
    
    
    
    
    public byte[]? PublicKey
    {
        get
        {
            var data = OverridePublicKey ?? DefaultPublicKey;
            return data is null || data.Length == 0 ? null : data;
        }
        set
        {
            OverridePublicKey = value is null ? [] : value;
        }
    }

    protected override int CustomAttributeIndex => Definition?.CustomAttributeIndex ?? -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => this;

    private readonly Dictionary<string, TypeAnalysisContext> TypesByName = new();

    private readonly Dictionary<Il2CppTypeDefinition, TypeAnalysisContext> TypesByDefinition = new();

    
    
    
    internal readonly ConcurrentDictionary<Il2CppType, GenericInstanceTypeAnalysisContext> GenericInstanceTypesByIl2CppType = new();

    public override string DefaultName => Definition?.AssemblyName.Name ?? throw new($"Injected assemblies should override {nameof(DefaultName)}");

    protected override bool IsInjected => Definition is null;

    
    
    
    public string CleanAssemblyName => MiscUtils.CleanPathElement(Name);

    public AssemblyAnalysisContext(Il2CppAssemblyDefinition? assemblyDefinition, ApplicationAnalysisContext appContext) : base(assemblyDefinition?.Token ?? 0, appContext)
    {
        if (assemblyDefinition is null)
            return;

        Definition = assemblyDefinition;

        if (AppContext.MetadataVersion >= 24.2f)
            CodeGenModule = AppContext.Binary.GetCodegenModuleByName(Definition.Image.Name!);

        InitCustomAttributeData();

        foreach (var il2CppTypeDefinition in Definition.Image.Types!)
        {
            var typeContext = new TypeAnalysisContext(il2CppTypeDefinition, this);
            Types.Add(typeContext);
            TypesByName[il2CppTypeDefinition.FullName!] = typeContext;
            TypesByDefinition[il2CppTypeDefinition] = typeContext;
        }

        foreach (var type in Types)
        {
            if (type.Definition!.NestedTypeCount < 1)
                continue;

            type.NestedTypes = type.Definition.NestedTypes!
                .Select(n => TypesByDefinition.TryGetValue(n, out var nestedType) ? nestedType : throw new("Unable to resolve nested type definition."))
                .Peek(t => t.DeclaringType = type)
                .ToList();
        }
    }

    public InjectedTypeAnalysisContext InjectType(string ns, string name, TypeAnalysisContext? baseType, TypeAttributes typeAttributes)
    {
        var ret = new InjectedTypeAnalysisContext(this, ns, name, baseType, typeAttributes);
        InjectType(ret);
        return ret;
    }

    internal void InjectType(InjectedTypeAnalysisContext ret)
    {
        Types.Add(ret);
        TypesByName[ret.FullName] = ret;
    }

    public TypeAnalysisContext? GetTypeByFullName(string fullName) => TypesByName.TryGetValue(fullName, out var typeContext) ? typeContext : null;

    public TypeAnalysisContext? GetTypeByDefinition(Il2CppTypeDefinition typeDefinition) => TypesByDefinition.TryGetValue(typeDefinition, out var typeContext) ? typeContext : null;

    public override string ToString() => "Assembly: " + Name;
}
