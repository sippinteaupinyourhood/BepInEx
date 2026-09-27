using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.CustomAttributes;












public class CustomAttributeArrayParameter(AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index)
    : BaseCustomAttributeParameter(owner, kind, index)
{
    public bool IsNullArray;
    public Il2CppType? EnumType;

    public Il2CppTypeEnum ArrType;

    public List<BaseCustomAttributeParameter> ArrayElements = [];

    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context)
    {
        var arrLength = reader.BaseStream.ReadUnityCompressedInt();
        if (arrLength == -1)
        {
            
            IsNullArray = true;
            return;
        }

        ArrType = (Il2CppTypeEnum)reader.ReadByte();
        if (ArrType == Il2CppTypeEnum.IL2CPP_TYPE_ENUM)
        {
            var enumTypeIndex = reader.BaseStream.ReadUnityCompressedInt();

            
            EnumType = context.Binary.GetType(Il2CppVariableWidthIndex<Il2CppType>.MakeTemporaryForFixedWidthUsage(enumTypeIndex)); 

            
            var enumClass = EnumType.AsClass();
            ArrType = enumClass.EnumUnderlyingType.Type;
        }

        var arrayElementsAreTypePrefixed = reader.ReadBoolean();

        if (arrayElementsAreTypePrefixed && ArrType != Il2CppTypeEnum.IL2CPP_TYPE_OBJECT)
            throw new("Array elements are type-prefixed, but the array type is not object");

        for (var i = 0; i < arrLength; i++)
        {
            var thisType = ArrType;

            if (arrayElementsAreTypePrefixed)
                thisType = (Il2CppTypeEnum)reader.ReadByte();

            
            var arrayElement = V29AttributeUtils.ConstructParameterForType(reader, context, thisType, Owner, CustomAttributeParameterKind.ArrayElement, i);

            arrayElement.ReadFromV29Blob(reader, context);

            ArrayElements.Add(arrayElement);
        }
    }

    public override string ToString()
    {
        if (IsNullArray)
            return "(array) null";

        var arrType = ArrType.ToString();
        if (EnumType != null)
            arrType = EnumType.AsClass().ToString();

        return $"new {arrType}[] {{{string.Join(", ", ArrayElements.Select(x => x.ToString()))}}}";
    }
}
