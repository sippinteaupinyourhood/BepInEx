namespace LibCpp2IL.Metadata;

public class Il2CppCustomAttributeDataRange : ReadableClass, IIl2CppTokenProvider
{
    
    public uint token;
    public uint startOffset;

    public uint Token => token;

    public override void Read(ClassReadingBinaryReader reader)
    {
        token = reader.ReadUInt32();
        startOffset = reader.ReadUInt32();
    }
}
