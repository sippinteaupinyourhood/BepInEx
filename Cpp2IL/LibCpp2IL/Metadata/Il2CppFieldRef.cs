using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;




public class Il2CppFieldRef : ReadableClass
{
    public Il2CppVariableWidthIndex<Il2CppType> typeIndex;
    public Il2CppVariableWidthIndex<Il2CppFieldDefinition> fieldIndex; 

    public Il2CppType? DeclaringType => LibCpp2IlMain.Binary?.GetType(typeIndex);

    public Il2CppTypeDefinition? DeclaringTypeDefinition => LibCpp2IlMain.TheMetadata?.GetTypeDefinitionFromIndex(DeclaringType!.Data.ClassIndex);

    public Il2CppFieldDefinition? FieldDefinition => LibCpp2IlMain.TheMetadata?.GetFieldDefinitionFromIndex(DeclaringTypeDefinition!.FirstFieldIdx + fieldIndex);

    public override void Read(ClassReadingBinaryReader reader)
    {
        typeIndex = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);
        fieldIndex = Il2CppVariableWidthIndex<Il2CppFieldDefinition>.Read(reader);
    }
}
