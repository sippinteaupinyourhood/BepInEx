using System.Collections.Generic;
using System.Diagnostics;

namespace LibCpp2IL.Metadata;

public class Il2CppGenericContainer : ReadableClass
{
    
    public int ownerIndex; 

    
    public int genericParameterCount; 

    
    public bool isGenericMethod; 

    
    public Il2CppVariableWidthIndex<Il2CppGenericParameter> genericParameterStart;

    public IEnumerable<Il2CppGenericParameter> GenericParameters
    {
        get
        {
            if (genericParameterCount == 0)
                yield break;

            for (var i = 0; i < genericParameterCount; i++)
            {
                var index = Il2CppVariableWidthIndex<Il2CppGenericParameter>.MakeTemporaryForFixedWidthUsage(genericParameterStart.Value + i); 
                var p = LibCpp2IlMain.TheMetadata!.GetGenericParameterFromIndex(index);
                p.Index = index;
                Debug.Assert(p.genericParameterIndexInOwner == i);
                yield return p;
            }
        }
    }

    
    public Il2CppTypeDefinition? TypeOwner => isGenericMethod ? null : LibCpp2IlMain.TheMetadata!.GetTypeDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppTypeDefinition>.MakeTemporaryForFixedWidthUsage(ownerIndex));

    
    public Il2CppMethodDefinition? MethodOwner => isGenericMethod ? LibCpp2IlMain.TheMetadata!.GetMethodDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppMethodDefinition>.MakeTemporaryForFixedWidthUsage(ownerIndex)) : null;

    public Il2CppTypeDefinition? DeclaringType => TypeOwner ?? MethodOwner?.DeclaringType;

    public override void Read(ClassReadingBinaryReader reader)
    {
        ownerIndex = reader.ReadInt32();

        if (IsAtLeast(106f))
        {
            genericParameterCount = reader.ReadUInt16();
            isGenericMethod = reader.ReadByte() != 0;
        }
        else
        {
            genericParameterCount = reader.ReadInt32();
            isGenericMethod = reader.ReadInt32() != 0;
        }

        genericParameterStart = Il2CppVariableWidthIndex<Il2CppGenericParameter>.Read(reader);
    }
}
