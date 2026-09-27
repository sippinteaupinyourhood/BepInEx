namespace LibCpp2IL.MachO;

public class MachOUniversalFileEntry : ReadableClass
{
    public MachOCpuType CpuType; 
    public MachOCpuSubtype CpuSubtype; 
    public ulong FileOffset; 
    public ulong FileSize; 
    public uint FileAlignment; 

    public override void Read(ClassReadingBinaryReader reader)
    {
        CpuType = (MachOCpuType)reader.ReadUInt32WithReversedBits();
        CpuSubtype = (MachOCpuSubtype)reader.ReadUInt32WithReversedBits();
        FileOffset = reader.ReadUInt32WithReversedBits();
        FileSize = reader.ReadUInt32WithReversedBits();
        FileAlignment = reader.ReadUInt32WithReversedBits();
    }
}
