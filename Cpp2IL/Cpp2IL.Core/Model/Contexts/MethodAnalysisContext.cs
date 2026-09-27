using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Graphs.Processors;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;




public class MethodAnalysisContext : HasGenericParameters, IMethodInfoProvider
{
    
    
    
    
    
    public readonly Il2CppMethodDefinition? Definition;

    
    
    
    public readonly TypeAnalysisContext? DeclaringType;

    
    
    
    public virtual ulong UnderlyingPointer => Definition?.MethodPointer ?? throw new("Subclasses of MethodAnalysisContext should override UnderlyingPointer");

    public ulong Rva => UnderlyingPointer == 0 || LibCpp2IlMain.Binary == null ? 0 : LibCpp2IlMain.Binary.GetRva(UnderlyingPointer);

    
    
    
    public Memory<byte> RawBytes => rawMethodBody ??= InitRawBytes();

    
    
    
    public List<InstructionSetIndependentInstruction>? ConvertedIsil;

    
    
    
    public ISILControlFlowGraph? ControlFlowGraph;

    public List<ParameterAnalysisContext> Parameters = [];

    
    
    
    public bool IsVoid => ReturnType == AppContext.SystemTypes.SystemVoidType;

    public bool IsStatic => (Attributes & MethodAttributes.Static) != 0;

    public bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;

    public bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;

    public bool IsNewSlot => (Attributes & MethodAttributes.NewSlot) != 0;

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeIndex if they have custom attributes");

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType?.DeclaringAssembly ?? throw new("Subclasses of MethodAnalysisContext should override CustomAttributeAssembly if they have custom attributes");

    public override string DefaultName => Definition?.Name ?? throw new("Subclasses of MethodAnalysisContext should override DefaultName");

    public string FullName => DeclaringType == null ? Name : $"{DeclaringType.FullName}::{Name}";

    public string FullNameWithSignature => $"{ReturnType.FullName} {FullName}({string.Join(", ", Parameters.Select(p => p.HumanReadableSignature))})";

    public virtual MethodAttributes DefaultAttributes => Definition?.Attributes ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultAttributes)}");

    public virtual MethodAttributes? OverrideAttributes { get; set; }

    public MethodAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    public virtual MethodImplAttributes DefaultImplAttributes => Definition?.MethodImplAttributes ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultImplAttributes)}");

    public virtual MethodImplAttributes? OverrideImplAttributes { get; set; }

    public MethodImplAttributes ImplAttributes
    {
        get => OverrideImplAttributes ?? DefaultImplAttributes;
        set => OverrideImplAttributes = value;
    }

    public MethodAttributes Visibility
    {
        get
        {
            return Attributes & MethodAttributes.MemberAccessMask;
        }
        set
        {
            Attributes = (Attributes & ~MethodAttributes.MemberAccessMask) | (value & MethodAttributes.MemberAccessMask);
        }
    }

    private List<GenericParameterTypeAnalysisContext>? _genericParameters;
    public override List<GenericParameterTypeAnalysisContext> GenericParameters
    {
        get
        {
            
            _genericParameters ??= Definition?.GenericContainer?.GenericParameters.Select(p => new GenericParameterTypeAnalysisContext(p, this)).ToList() ?? [];
            return _genericParameters;
        }
    }

    private ushort Slot => Definition?.slot ?? ushort.MaxValue;

    public virtual TypeAnalysisContext DefaultReturnType => DeclaringType?.DeclaringAssembly.ResolveIl2CppType(Definition?.RawReturnType) ?? throw new($"Subclasses of MethodAnalysisContext should override {nameof(DefaultReturnType)}");

    public TypeAnalysisContext? OverrideReturnType { get; set; }

    
    public TypeAnalysisContext ReturnType
    {
        get => OverrideReturnType ?? DefaultReturnType;
        set => OverrideReturnType = value;
    }
    
    protected Memory<byte>? rawMethodBody;

    public MethodAnalysisContext? BaseMethod
    {
        get
        {
            if (Definition == null)
                return null;

            var vtable = DeclaringType?.Definition?.VTable;
            if (vtable == null)
                return null;

            for (var i = 0; i < vtable.Length; ++i)
            {
                var vtableEntry = vtable[i];
                if (vtableEntry is null or { Type: not MetadataUsageType.MethodDef } || vtableEntry.AsMethod() != Definition)
                    continue;

                var baseType = DeclaringType?.DefaultBaseType;
                while (baseType is not null)
                {
                    if (TryGetMethodForSlot(baseType, i, out var method))
                    {
                        return method;
                    }
                    baseType = baseType.DefaultBaseType;
                }
            }
            return null;
        }
    }

    private List<MethodAnalysisContext>? _overrides;

    
    
    
    public List<MethodAnalysisContext> Overrides
    {
        get
        {
            
            return _overrides ??= GetOverrides().ToList();
        }
    }

    private IEnumerable<MethodAnalysisContext> GetOverrides()
    {
        if (Definition == null)
            return [];

        var declaringTypeDefinition = DeclaringType?.Definition;
        if (declaringTypeDefinition == null)
            return [];

        var vtable = declaringTypeDefinition.VTable;
        if (vtable == null)
            return [];

        return GetOverriddenMethods(declaringTypeDefinition, vtable);

        IEnumerable<MethodAnalysisContext> GetOverriddenMethods(Il2CppTypeDefinition declaringTypeDefinition, MetadataUsage?[] vtable)
        {
            for (var i = 0; i < vtable.Length; ++i)
            {
                var vtableEntry = vtable[i];
                if (vtableEntry is null or { Type: not MetadataUsageType.MethodDef })
                    continue;

                if (vtableEntry.AsMethod() != Definition)
                    continue;

                
                foreach (var interfaceOffset in declaringTypeDefinition.InterfaceOffsets)
                {
                    if (i >= interfaceOffset.offset)
                    {
                        var interfaceTypeContext = interfaceOffset.Type.ToContext(CustomAttributeAssembly);
                        if (interfaceTypeContext != null && TryGetMethodForSlot(interfaceTypeContext, i - interfaceOffset.offset, out var method))
                        {
                            yield return method;
                        }
                    }
                }
            }
        }
    }

    private static bool TryGetMethodForSlot(TypeAnalysisContext declaringType, int slot, [NotNullWhen(true)] out MethodAnalysisContext? method)
    {
        if (declaringType is GenericInstanceTypeAnalysisContext genericInstanceType)
        {
            var genericMethod = genericInstanceType.GenericType.Methods.FirstOrDefault(m => m.Slot == slot);
            if (genericMethod is not null)
            {
                method = new ConcreteGenericMethodAnalysisContext(genericMethod, genericInstanceType.GenericArguments, []);
                return true;
            }
        }
        else
        {
            var baseMethod = declaringType.Methods.FirstOrDefault(m => m.Slot == slot);
            if (baseMethod is not null)
            {
                method = baseMethod;
                return true;
            }
        }

        method = null;
        return false;
    }

    private static readonly List<IBlockProcessor> blockProcessors =
    [
        new MetadataProcessor(),
        new CallProcessor()
    ];

    public MethodAnalysisContext(Il2CppMethodDefinition? definition, TypeAnalysisContext parent) : base(definition?.token ?? 0, parent.AppContext)
    {
        DeclaringType = parent;
        Definition = definition;

        if (Definition != null)
        {
            InitCustomAttributeData();

            for (var i = 0; i < Definition.InternalParameterData!.Length; i++)
            {
                var parameterDefinition = Definition.InternalParameterData![i];
                Parameters.Add(new(parameterDefinition, i, this));
            }
        }
        else
            rawMethodBody = Array.Empty<byte>();
    }

    [MemberNotNull(nameof(rawMethodBody))]
    public void EnsureRawBytes()
    {
        rawMethodBody ??= InitRawBytes();
    }

    private Memory<byte> InitRawBytes()
    {
        
        
        if (Definition != null && Definition.MethodPointer != 0 && !Definition.Attributes.HasFlag(MethodAttributes.Abstract))
        {
            var ret = AppContext.InstructionSet.GetRawBytesForMethod(this, false);

            if (ret.Length == 0)
            {
                Logger.VerboseNewline("\t\t\tUnexpectedly got 0-byte method body for " + this + $". Pointer was 0x{Definition.MethodPointer:X}", "MAC");
            }

            return ret;
        }
        else
            return Array.Empty<byte>();
    }

    protected MethodAnalysisContext(ApplicationAnalysisContext context) : base(0, context)
    {
        rawMethodBody = Array.Empty<byte>();
    }

    [MemberNotNull(nameof(ConvertedIsil))]
    public void Analyze()
    {
        if (ConvertedIsil != null)
            return;

        if (UnderlyingPointer == 0)
        {
            ConvertedIsil = [];
            return;
        }

        ConvertedIsil = AppContext.InstructionSet.GetIsilFromMethod(this);

        if (ConvertedIsil.Count == 0)
            return; 

        ControlFlowGraph = new ISILControlFlowGraph();
        ControlFlowGraph.Build(ConvertedIsil);

        
        foreach (var block in ControlFlowGraph.Blocks)
        {
            foreach (var converter in blockProcessors)
            {
                converter.Process(this, block);
            }
        }
    }

    public void ReleaseAnalysisData()
    {
        ConvertedIsil = null;
        ControlFlowGraph = null;
    }

    public ConcreteGenericMethodAnalysisContext MakeGenericInstanceMethod(params IEnumerable<TypeAnalysisContext> methodGenericParameters)
    {
        if (this is ConcreteGenericMethodAnalysisContext methodOnGenericInstanceType)
        {
            return new ConcreteGenericMethodAnalysisContext(methodOnGenericInstanceType.BaseMethodContext, methodOnGenericInstanceType.TypeGenericParameters, methodGenericParameters);
        }
        else
        {
            return new ConcreteGenericMethodAnalysisContext(this, [], methodGenericParameters);
        }
    }

    public ConcreteGenericMethodAnalysisContext MakeConcreteGenericMethod(IEnumerable<TypeAnalysisContext> typeGenericParameters, IEnumerable<TypeAnalysisContext> methodGenericParameters)
    {
        if (this is ConcreteGenericMethodAnalysisContext)
        {
            throw new InvalidOperationException($"Attempted to make a {nameof(ConcreteGenericMethodAnalysisContext)} concrete: {this}");
        }
        else
        {
            return new ConcreteGenericMethodAnalysisContext(this, typeGenericParameters, methodGenericParameters);
        }
    }

    public override string ToString() => $"Method: {FullName}";

    #region StableNameDot implementation

    ITypeInfoProvider IMethodInfoProvider.ReturnType =>
        Definition!.RawReturnType!.ThisOrElementIsGenericParam()
            ? new GenericParameterTypeInfoProviderWrapper(Definition.RawReturnType!.GetGenericParamName())
            : TypeAnalysisContext.GetSndnProviderForType(AppContext, Definition!.RawReturnType);

    IEnumerable<IParameterInfoProvider> IMethodInfoProvider.ParameterInfoProviders => Parameters;

    string IMethodInfoProvider.MethodName => Name;

    MethodAttributes IMethodInfoProvider.MethodAttributes => Attributes;

    MethodSemantics IMethodInfoProvider.MethodSemantics
    {
        get
        {
            if (DeclaringType != null)
            {
                
                foreach (var prop in DeclaringType.Properties)
                {
                    if (prop.Getter == this)
                        return MethodSemantics.Getter;
                    if (prop.Setter == this)
                        return MethodSemantics.Setter;
                }

                foreach (var evt in DeclaringType.Events)
                {
                    if (evt.Adder == this)
                        return MethodSemantics.AddOn;
                    if (evt.Remover == this)
                        return MethodSemantics.RemoveOn;
                    if (evt.Invoker == this)
                        return MethodSemantics.Fire;
                }
            }

            return 0;
        }
    }

    #endregion
}
