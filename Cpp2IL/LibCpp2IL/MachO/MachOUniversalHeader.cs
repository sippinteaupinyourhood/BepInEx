namespace LibCpp2IL.MachO;

public class MachOUniversalHeader : ReadableClass
{
    public uint Magic; 
    public uint NumberFileEntries; 

    public override void Read(ClassReadingBinaryReader reader)
    {
        Magic = reader.ReadUInt32WithReversedBits();
        NumberFileEntries = reader.ReadUInt32WithReversedBits();
    }
}
