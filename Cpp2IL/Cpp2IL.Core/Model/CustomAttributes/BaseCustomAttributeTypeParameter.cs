using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;




public abstract class BaseCustomAttributeTypeParameter(
    AnalyzedCustomAttribute owner,
    CustomAttributeParameterKind kind,
    int index)
    : BaseCustomAttributeParameter(owner, kind, index)
{
    public abstract TypeAnalysisContext? TypeContext { get; }
}
