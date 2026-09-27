namespace LibCpp2IL.BinaryStructures;

public class Il2CppGenericContext : ReadableClass
{
    
    public ulong class_inst;

    
    public ulong method_inst;

    public Il2CppGenericInst ClassInst => LibCpp2IlMain.Binary!.ReadReadableAtVirtualAddress<Il2CppGenericInst>(class_inst);
    public Il2CppGenericInst MethodInst => LibCpp2IlMain.Binary!.ReadReadableAtVirtualAddress<Il2CppGenericInst>(method_inst);

    public override void Read(ClassReadingBinaryReader reader)
    {
        class_inst = reader.ReadNUint();
        method_inst = reader.ReadNUint();
    }
}
