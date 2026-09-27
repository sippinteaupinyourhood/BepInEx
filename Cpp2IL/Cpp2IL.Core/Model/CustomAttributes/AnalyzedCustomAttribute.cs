using System.Collections.Generic;
using System.Text;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;




public class AnalyzedCustomAttribute(MethodAnalysisContext constructor)
{
    
    
    
    public readonly MethodAnalysisContext Constructor = constructor;

    
    
    
    public readonly List<BaseCustomAttributeParameter> ConstructorParameters = [];

    
    
    
    public readonly List<CustomAttributeField> Fields = [];

    
    
    
    public readonly List<CustomAttributeProperty> Properties = [];

    
    
    
    public bool HasAnyParameters => Constructor.Parameters.Count > 0;

    
    
    
    public bool IsSuitableForEmission => !HasAnyParameters || ConstructorParameters.Count == Constructor.Parameters.Count;

    public bool AnyFieldsOrPropsSet => Fields.Count + Properties.Count > 0;

    public override string ToString()
    {
        var sb = new StringBuilder("[");

        var attributeTypeName = Constructor.DeclaringType!.Name!;

        const string suffix = "Attribute";
        if (attributeTypeName.EndsWith(suffix))
            attributeTypeName = attributeTypeName[..^suffix.Length];

        sb.Append(attributeTypeName);

        if (HasAnyParameters || AnyFieldsOrPropsSet)
            sb.Append('(');

        if (!IsSuitableForEmission)
            sb.Append("/*Cpp2IL Warning: missing at least one required parameter*/");

        if (ConstructorParameters.Count + Fields.Count + Properties.Count > 0)
        {
            var needComma = false;

            foreach (var param in ConstructorParameters)
            {
                if (needComma)
                    sb.Append(", ");

                sb.Append(param);
                needComma = true;
            }

            foreach (var field in Fields)
            {
                if (needComma)
                    sb.Append(", ");

                sb.Append(field);
                needComma = true;
            }

            foreach (var prop in Properties)
            {
                if (needComma)
                    sb.Append(", ");

                sb.Append(prop);
                needComma = true;
            }
        }

        if (HasAnyParameters || AnyFieldsOrPropsSet)
            sb.Append(')');

        sb.Append(']');
        return sb.ToString();
    }
}
