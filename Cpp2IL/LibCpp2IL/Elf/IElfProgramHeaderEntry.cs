using LibCpp2IL.PE;

namespace LibCpp2IL.Elf;

public interface IElfProgramHeaderEntry
{
    
    public ElfProgramHeaderFlags Flags { get; }

    public ElfProgramEntryType Type { get; }
    public ulong RawAddress { get; }
    public ulong VirtualAddress { get; }
    public ulong PhysicalAddr { get; }
    public ulong RawSize { get; }
    public ulong VirtualSize { get; }
    public long Align { get; }
}
