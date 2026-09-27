namespace LibCpp2IL.Metadata;




public class Il2CppNestedTypeIndex : ReadableClass
{
    public int Value { get; private set; }
    
    public override void Read(ClassReadingBinaryReader reader)
    {
        Value = reader.ReadInt32();
    }
}
