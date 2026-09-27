using System.Reflection;
using System.Text;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

public class ParameterAnalysisContext : HasCustomAttributesAndName, IParameterInfoProvider
{
    
    
    
    public Il2CppParameterDefinition? Definition { get; }

    
    
    
    public int ParameterIndex { get; }

    
    
    
    public MethodAnalysisContext DeclaringMethod { get; }

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of ParameterAnalysisContext must provide a customAttributeIndex");
    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringMethod.DeclaringType!.DeclaringAssembly;
    public override string DefaultName => Definition?.Name ?? throw new("Subclasses of ParameterAnalysisContext must provide a default name");

    
    
    
    public string ReadableTypeName => ParameterType.FullName;

    
    
    
    public string HumanReadableSignature => $"{ReadableTypeName} {Name}";

    public virtual ParameterAttributes DefaultAttributes => (ParameterAttributes?)Definition?.RawType?.Attrs ?? throw new("Subclasses of ParameterAnalysisContext must provide parameter attributes");

    public virtual ParameterAttributes? OverrideAttributes { get; set; }

    
    
    
    public ParameterAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    
    
    
    public bool IsRef => ParameterType is ByRefTypeAnalysisContext || Attributes.HasFlag(ParameterAttributes.Out);

    
    
    
    public Il2CppParameterDefaultValue? DefaultValue { get; }

    public virtual TypeAnalysisContext DefaultParameterType => DeclaringMethod.DeclaringType!.DeclaringAssembly.ResolveIl2CppType(Definition?.RawType) ?? throw new("Subclasses of ParameterAnalysisContext must provide a parameter type");

    public TypeAnalysisContext? OverrideParameterType { get; set; }

    public TypeAnalysisContext ParameterType
    {
        get => OverrideParameterType ?? DefaultParameterType;
        set => OverrideParameterType = value;
    }

    public ParameterAnalysisContext(Il2CppParameterDefinition? definition, int parameterIndex, MethodAnalysisContext declaringMethod) : base(definition?.token ?? 0, declaringMethod.AppContext)
    {
        Definition = definition;
        ParameterIndex = parameterIndex;
        DeclaringMethod = declaringMethod;

        if (Definition != null)
        {
            InitCustomAttributeData();

            if (Attributes.HasFlag(ParameterAttributes.HasDefault))
            {
                DefaultValue = AppContext.Metadata.GetParameterDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppParameterDefinition>.MakeTemporaryForFixedWidthUsage(declaringMethod.Definition!.parameterStart.Value + parameterIndex))!;
            }
        }
    }

    public override string ToString()
    {
        if (!AppContext.HasFinishedInitializing)
            
            return $"Parameter {Name} (ordinal {ParameterIndex}) of {DeclaringMethod}";

        if (Definition?.RawType == null)
            return $"object unnamed_param_{ParameterIndex}";

        var result = new StringBuilder();

        if (Attributes.HasFlag(ParameterAttributes.Out))
            result.Append("out ");
        else if (Attributes.HasFlag(ParameterAttributes.In))
            result.Append("in ");
        else if (ParameterType is ByRefTypeAnalysisContext)
            result.Append("ref ");

        result.Append(CsFileUtils.GetTypeName(ParameterType.Name)).Append(' ');

        if (string.IsNullOrEmpty(ParameterName))
            result.Append("unnamed_param_").Append(ParameterIndex);
        else
            result.Append(ParameterName);

        if (Attributes.HasFlag(ParameterAttributes.HasDefault) && DefaultValue != null)
        {
            var defaultValue = DefaultValue!.ContainedDefaultValue;
            if (defaultValue is string stringDefaultValue)
                defaultValue = $"\"{stringDefaultValue}\"";
            else if (defaultValue is bool boolDefaultValue)
                defaultValue = boolDefaultValue.ToString().ToLowerInvariant();
            else if (defaultValue is null)
                defaultValue = "null";

            result.Append(" = ").Append(defaultValue);
        }

        return result.ToString();
    }

    #region StableNameDotNet implementation

    public ITypeInfoProvider ParameterTypeInfoProvider
        => Definition!.RawType!.ThisOrElementIsGenericParam()
            ? new GenericParameterTypeInfoProviderWrapper(Definition.RawType!.GetGenericParamName())
            : TypeAnalysisContext.GetSndnProviderForType(AppContext, Definition.RawType);

    public string ParameterName => Name;

    #endregion
}
