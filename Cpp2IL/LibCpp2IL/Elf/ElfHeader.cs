namespace LibCpp2IL.Elf;

public class ElfFileHeader : ReadableClass
{
    public ElfFileType Type;
    public short Machine; 
    public int Version; 
    public long pEntryPoint; 
    public long pProgramHeader; 
    public long pSectionHeader; 
    public int Flags;
    public short HeaderSize; 

    public short ProgramHeaderEntrySize;
    public short ProgramHeaderEntryCount;

    public short SectionHeaderEntrySize;
    public short SectionHeaderEntryCount;

    public short SectionNameSectionOffset; 

    public override void Read(ClassReadingBinaryReader reader)
    {
        Type = (ElfFileType)reader.ReadInt16();
        Machine = reader.ReadInt16();
        Version = reader.ReadInt32();
        pEntryPoint = reader.ReadNInt();
        pProgramHeader = reader.ReadNInt();
        pSectionHeader = reader.ReadNInt();
        Flags = reader.ReadInt32();
        HeaderSize = reader.ReadInt16();
        ProgramHeaderEntrySize = reader.ReadInt16();
        ProgramHeaderEntryCount = reader.ReadInt16();
        SectionHeaderEntrySize = reader.ReadInt16();
        SectionHeaderEntryCount = reader.ReadInt16();
        SectionNameSectionOffset = reader.ReadInt16();
    }
}
