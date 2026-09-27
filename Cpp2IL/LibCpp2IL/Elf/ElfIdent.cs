namespace LibCpp2IL.Elf;

public class ElfFileIdent : ReadableClass
{
    public int Magic;
    public byte Architecture; 
    public byte Endianness; 
    public byte Version; 
    public byte OSAbi; 
    public byte AbiVersion;
    

    public override void Read(ClassReadingBinaryReader reader)
    {
        Magic = reader.ReadInt32();
        Architecture = reader.ReadByte();
        Endianness = reader.ReadByte();
        Version = reader.ReadByte();
        OSAbi = reader.ReadByte();
        AbiVersion = reader.ReadByte();
        reader.ReadBytes(7);
    }
}
