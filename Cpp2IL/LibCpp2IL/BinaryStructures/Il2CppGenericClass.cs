using LibCpp2IL.Metadata;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppGenericClass : ReadableClass
{
    [Version(Max = 24.5f)] public long TypeDefinitionIndex; 

    [Version(Min = 27.0f)] public ulong V27TypePointer;
    
    public Il2CppGenericContext Context = null!; 
    public ulong CachedClass; 

    public Il2CppTypeDefinition TypeDefinition => LibCpp2IlMain.MetadataVersion < 27f
        ? LibCpp2IlMain.TheMetadata!.GetTypeDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppTypeDefinition>.MakeTemporaryForFixedWidthUsage((int)TypeDefinitionIndex)) 
        : V27BaseType!.AsClass();

    public Il2CppType? V27BaseType => LibCpp2IlMain.MetadataVersion < 27f ? null : LibCpp2IlMain.Binary!.ReadReadableAtVirtualAddress<Il2CppType>(V27TypePointer);

    public override void Read(ClassReadingBinaryReader reader)
    {
        if (IsAtLeast(27f))
            V27TypePointer = reader.ReadNUint();
        else
            TypeDefinitionIndex = reader.ReadNInt();
        
        Context = reader.ReadReadableHereNoLock<Il2CppGenericContext>();
        CachedClass = reader.ReadNUint();
    }
}
