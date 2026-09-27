using System.Text;
using LibCpp2IL.Metadata;

#pragma warning disable 8618
namespace LibCpp2IL.Reflection;











public class Il2CppTypeReflectionData
{
    public Il2CppTypeDefinition? baseType;
    public Il2CppTypeReflectionData[] genericParams;
    public bool isType;
    public bool isGenericType;
    public bool isArray;
    public Il2CppTypeReflectionData? arrayType;
    public byte arrayRank;
    public string variableGenericParamName;
    public Il2CppVariableWidthIndex<Il2CppGenericParameter> variableGenericParamIndex;
    public bool isPointer;
#pragma warning restore 8618

    public Il2CppGenericParameter? GenericParameter => isArray || isType ? null : LibCpp2IlMain.TheMetadata?.GetGenericParameterFromIndex(variableGenericParamIndex);

    private string GetPtrSuffix()
    {
        return isPointer ? "*" : "";
    }

    public override string ToString()
    {
        if (isArray)
            return arrayType + "[]".Repeat(arrayRank) + GetPtrSuffix();

        if (!isType)
            return variableGenericParamName + GetPtrSuffix();

        if (!isGenericType)
            return baseType!.FullName! + GetPtrSuffix();

        var builder = new StringBuilder(baseType!.FullName + "<");
        foreach (var genericParam in genericParams)
        {
            builder.Append(genericParam).Append(", ");
        }

        builder.Remove(builder.Length - 2, 2);
        builder.Append(">");
        return builder + GetPtrSuffix();
    }
}
