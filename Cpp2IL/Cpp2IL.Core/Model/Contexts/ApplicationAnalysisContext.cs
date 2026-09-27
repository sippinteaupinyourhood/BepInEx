using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using AssetRipper.Primitives;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;




public class ApplicationAnalysisContext : ContextWithDataStorage
{
    
    
    
    public Il2CppBinary Binary;

    
    
    
    public Il2CppMetadata Metadata;

    
    
    
    public float MetadataVersion => Metadata.MetadataVersion;
    
    
    
    
    public UnityVersion UnityVersion => Metadata.UnityVersion;

    
    
    
    public Cpp2IlInstructionSet InstructionSet;

    
    
    
    public SystemTypesContext SystemTypes;

    
    
    
    public readonly List<AssemblyAnalysisContext> Assemblies = [];

    
    
    
    public readonly Dictionary<string, AssemblyAnalysisContext> AssembliesByName = new();

    
    
    
    public readonly Dictionary<ulong, List<MethodAnalysisContext>> MethodsByAddress = new();

    
    
    
    public readonly Dictionary<Cpp2IlMethodRef, ConcreteGenericMethodAnalysisContext> ConcreteGenericMethodsByRef = new();

    
    
    
    private BaseKeyFunctionAddresses? _keyFunctionAddresses;

    
    
    
    public bool HasFinishedInitializing { get; private set; }

    private readonly Dictionary<Il2CppImageDefinition, AssemblyAnalysisContext> AssembliesByImageDefinition = new();

    public ApplicationAnalysisContext(Il2CppBinary binary, Il2CppMetadata metadata)
    {
        Binary = binary;
        Metadata = metadata;

        try
        {
            InstructionSet = InstructionSetRegistry.GetInstructionSet(binary.InstructionSetId);
        }
        catch (Exception)
        {
            throw new InstructionSetHandlerNotRegisteredException(binary.InstructionSetId);
        }

        Logger.VerboseNewline("\tUsing instruction set handler: " + InstructionSet.GetType().FullName);

        foreach (var assemblyDefinition in Metadata.AssemblyDefinitions)
        {
            Logger.VerboseNewline($"\tProcessing assembly: {assemblyDefinition.AssemblyName.Name}...");
            var aac = new AssemblyAnalysisContext(assemblyDefinition, this);
            Assemblies.Add(aac);
            AssembliesByName[assemblyDefinition.AssemblyName.Name] = aac;
            AssembliesByImageDefinition[assemblyDefinition.Image] = aac;
        }

        SystemTypes = new(this);
        
        MiscUtils.InitFunctionStarts(this);

        PopulateMethodsByAddressTable();

        HasFinishedInitializing = true;
    }

    
    
    
    private void PopulateMethodsByAddressTable()
    {
        Assemblies.SelectMany(a => a.Types).SelectMany(t => t.Methods).ToList().ForEach(m =>
        {
            m.EnsureRawBytes();
            var ptr = InstructionSet.GetPointerForMethod(m);

            if (!MethodsByAddress.ContainsKey(ptr))
                MethodsByAddress.Add(ptr, []);

            MethodsByAddress[ptr].Add(m);
        });

        Logger.VerboseNewline("\tProcessing concrete generic methods...");
        foreach (var methodRef in Binary.ConcreteGenericMethods.Values.SelectMany(v => v))
        {
#if !DEBUG
            try
            {
#endif
            var gm = new ConcreteGenericMethodAnalysisContext(methodRef, this);

            var ptr = InstructionSet.GetPointerForMethod(gm);

            if (!MethodsByAddress.ContainsKey(ptr))
                MethodsByAddress[ptr] = [];

            MethodsByAddress[ptr].Add(gm);
            ConcreteGenericMethodsByRef[methodRef] = gm;
#if !DEBUG
            }
            catch (Exception e)
            {
                throw new("Failed to process concrete generic method.", e);
            }
#endif
        }
    }

    
    
    
    
    
    public AssemblyAnalysisContext? GetAssemblyByName(string name)
    {
        if (name.Length >= 4 && name[^4] == '.' && name[^3] == 'd')
            
            name = name[..^4];
        else if (name.Length >= 6 && name[^6] == '.' && name[^5] == 'w')
            
            name = name[..^6];

        return AssembliesByName[name];
    }

    public AssemblyAnalysisContext? ResolveContextForAssembly(Il2CppImageDefinition? imageDefinition)
    {
        return imageDefinition is not null
            ? AssembliesByImageDefinition[imageDefinition]
            : null;
    }

    public AssemblyAnalysisContext? ResolveContextForAssembly(Il2CppAssemblyDefinition? assemblyDefinition)
    {
        return ResolveContextForAssembly(assemblyDefinition?.Image);
    }

    public TypeAnalysisContext? ResolveContextForType(Il2CppTypeDefinition? typeDefinition)
    {
        return typeDefinition is not null
            ? AssembliesByImageDefinition[typeDefinition.DeclaringAssembly!].GetTypeByDefinition(typeDefinition)
            : null;
    }

    public MethodAnalysisContext? ResolveContextForMethod(Il2CppMethodDefinition? methodDefinition)
    {
        return ResolveContextForType(methodDefinition?.DeclaringType)?.Methods.FirstOrDefault(m => m.Definition == methodDefinition);
    }

    [return: NotNullIfNotNull(nameof(methodReference))]
    public ConcreteGenericMethodAnalysisContext? ResolveContextForMethod(Cpp2IlMethodRef? methodReference)
    {
        if(methodReference == null)
            return null;
            
        return ConcreteGenericMethodsByRef.TryGetValue(methodReference, out var context) ? context : new(methodReference, this);
    }

    public FieldAnalysisContext? ResolveContextForField(Il2CppFieldDefinition? field)
    {
        return ResolveContextForType(field?.DeclaringType)?.Fields.FirstOrDefault(f => f.BackingData?.Field == field);
    }

    public EventAnalysisContext? ResolveContextForEvent(Il2CppEventDefinition? eventDefinition)
    {
        return ResolveContextForType(eventDefinition?.DeclaringType)?.Events.FirstOrDefault(e => e.Definition == eventDefinition);
    }

    public PropertyAnalysisContext? ResolveContextForProperty(Il2CppPropertyDefinition? propertyDefinition)
    {
        return ResolveContextForType(propertyDefinition?.DeclaringType)?.Properties.FirstOrDefault(p => p.Definition == propertyDefinition);
    }

    public GenericParameterTypeAnalysisContext? ResolveContextForGenericParameter(Il2CppGenericParameter? genericParameter)
    {
        if (genericParameter is null)
            return null;

        if (genericParameter.Owner.TypeOwner is { } typeOwner)
        {
            return ResolveContextForType(typeOwner)?.GenericParameters.ElementAtOrDefault(genericParameter.genericParameterIndexInOwner);
        }
        else
        {
            Debug.Assert(genericParameter.Owner.MethodOwner is not null);
            return ResolveContextForMethod(genericParameter.Owner.MethodOwner)?.GenericParameters.ElementAtOrDefault(genericParameter.genericParameterIndexInOwner);
        }
    }

    public BaseKeyFunctionAddresses GetOrCreateKeyFunctionAddresses()
    {
        lock (InstructionSet)
        {
            if (_keyFunctionAddresses == null)
                (_keyFunctionAddresses = InstructionSet.CreateKeyFunctionAddressesInstance()).Find(this);

            return _keyFunctionAddresses;
        }
    }

    public MultiAssemblyInjectedType InjectTypeIntoAllAssemblies(string ns, string name, TypeAnalysisContext? baseType, TypeAttributes typeAttributes = TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed)
    {
        var types = Assemblies.Select(a => (InjectedTypeAnalysisContext)a.InjectType(ns, name, baseType, typeAttributes)).ToArray();

        return new(types);
    }

    public InjectedAssemblyAnalysisContext InjectAssembly(
        string name,
        Version? version = null,
        uint hashAlgorithm = 0,
        uint flags = 0,
        string? culture = null,
        byte[]? publicKeyToken = null,
        byte[]? publicKey = null)
    {
        var assembly = new InjectedAssemblyAnalysisContext(name, this, version, hashAlgorithm, flags, culture, publicKeyToken, publicKey);
        Assemblies.Add(assembly);
        AssembliesByName.Add(name, assembly);
        return assembly;
    }

    public IEnumerable<TypeAnalysisContext> AllTypes => Assemblies.SelectMany(a => a.Types);
}
