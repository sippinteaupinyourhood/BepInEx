namespace LibCpp2IL.MachO;

public class MachOHeader : ReadableClass
{
    public const uint MAGIC_32_BIT = 0xFEEDFACE;
    public const uint MAGIC_64_BIT = 0xFEEDFACF;

    public uint Magic; 
    public MachOCpuType CpuType; 
    public MachOCpuSubtype CpuSubtype; 
    public MachOFileType FileType; 
    public uint NumLoadCommands; 
    public uint SizeOfLoadCommands; 
    public MachOHeaderFlags Flags; 

    public uint Reserved; 

    public override void Read(ClassReadingBinaryReader reader)
    {
        Magic = reader.ReadUInt32();
        CpuType = (MachOCpuType)reader.ReadUInt32();
        CpuSubtype = (MachOCpuSubtype)reader.ReadUInt32();
        FileType = (MachOFileType)reader.ReadUInt32();
        NumLoadCommands = reader.ReadUInt32();
        SizeOfLoadCommands = reader.ReadUInt32();
        Flags = (MachOHeaderFlags)reader.ReadUInt32();

        if (Magic == MAGIC_64_BIT)
            Reserved = reader.ReadUInt32();
    }
}
