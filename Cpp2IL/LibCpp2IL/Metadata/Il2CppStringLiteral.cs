namespace LibCpp2IL.Metadata;

public class Il2CppStringLiteral : ReadableClass
{
    [Version(Max = 34)] 
    public uint length;
    public int dataIndex;

    public override void Read(ClassReadingBinaryReader reader)
    {
        if(IsLessThan(35))
            length = reader.ReadUInt32();
        dataIndex = reader.ReadInt32();
    }
}
