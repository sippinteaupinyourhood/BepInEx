using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using LibCpp2IL.PE;

namespace LibCpp2IL.Elf;

public sealed class ElfFile : Il2CppBinary
{
    private byte[] _raw;
    private List<IElfProgramHeaderEntry> _elfProgramHeaderEntries;
    private readonly List<ElfSectionHeaderEntry> _elfSectionHeaderEntries;
    private ElfFileIdent? _elfFileIdent;
    private ElfFileHeader? _elfHeader;
    private readonly List<ElfDynamicEntry> _dynamicSection = [];
    private readonly List<ElfSymbolTableEntry> _symbolTable = [];
    private readonly Dictionary<string, ElfSymbolTableEntry> _exportNameTable = new();
    private readonly Dictionary<ulong, ElfSymbolTableEntry> _exportAddressTable = new();
    private List<long>? _initializerPointers;

    private readonly List<(ulong start, ulong end)> relocationBlocks = [];

    private long _globalOffset;

    public ElfFile(MemoryStream input) : base(input)
    {
        _raw = input.GetBuffer();

        LibLogger.Verbose("\tReading Elf File Ident...");
        var start = DateTime.Now;

        ReadAndValidateIdent();

        var isBigEndian = _elfFileIdent!.Endianness == 2;

        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        LibLogger.VerboseNewline($"\tBinary is {(is32Bit ? "32-bit" : "64-bit")} and {(isBigEndian ? "big-endian" : "little-endian")}.");

        if (isBigEndian)
            SetBigEndian();

        LibLogger.Verbose("\tReading and validating full ELF header...");
        start = DateTime.Now;

        ReadHeader();

        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        LibLogger.VerboseNewline($"\tElf File contains instructions of type {InstructionSetId}");

        LibLogger.Verbose("\tReading ELF program header table...");
        start = DateTime.Now;

        ReadProgramHeaderTable();

        LibLogger.VerboseNewline($"Read {_elfProgramHeaderEntries!.Count} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.VerboseNewline("\tReading ELF section header table and names...");
        start = DateTime.Now;

        
        try
        {
            _elfSectionHeaderEntries = ReadReadableArrayAtRawAddr<ElfSectionHeaderEntry>(_elfHeader!.pSectionHeader, _elfHeader.SectionHeaderEntryCount).ToList();
        }
        catch (Exception)
        {
            _elfSectionHeaderEntries = [];
        }

        if (_elfHeader!.SectionNameSectionOffset >= 0 && _elfHeader.SectionNameSectionOffset < _elfSectionHeaderEntries.Count)
        {
            var pSectionHeaderStringTable = _elfSectionHeaderEntries[_elfHeader.SectionNameSectionOffset].RawAddress;

            foreach (var section in _elfSectionHeaderEntries)
            {
                section.Name = ReadStringToNull(pSectionHeaderStringTable + section.NameOffset);
                LibLogger.VerboseNewline($"\t\t-Name for section at 0x{section.RawAddress:X} is {section.Name}");
            }
        }

        LibLogger.VerboseNewline($"\tRead {_elfSectionHeaderEntries.Count} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        if (_elfSectionHeaderEntries.FirstOrDefault(s => s.Name == ".text") is { } textSection)
        {
            _globalOffset = (long)textSection.VirtualAddress - (long)textSection.RawAddress;
        }
        else
        {
            var execSegment = _elfProgramHeaderEntries!.First(p => (p.Flags & ElfProgramHeaderFlags.PF_X) != 0);
            _globalOffset = (long)execSegment.VirtualAddress - (long)execSegment.RawAddress;
        }

        LibLogger.VerboseNewline($"\tELF global offset is 0x{_globalOffset:X}");

        
        if (GetProgramHeaderOfType(ElfProgramEntryType.PT_DYNAMIC) is { } dynamicSegment)
        {
            
            
            
            _dynamicSection = ReadReadableArrayAtVirtualAddress<ElfDynamicEntry>(dynamicSegment.VirtualAddress, (int)dynamicSegment.RawSize / (is32Bit ? 8 : 16)).ToList();
        }

        LibLogger.VerboseNewline("\tFinding Relocations...");
        start = DateTime.Now;

        ProcessRelocations();

        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.VerboseNewline("\tProcessing Symbols...");
        start = DateTime.Now;

        ProcessSymbols();

        LibLogger.VerboseNewline($"\tOK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tProcessing Initializers...");
        start = DateTime.Now;

        ProcessInitializers();

        LibLogger.VerboseNewline($"Got {_initializerPointers!.Count} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
    }

    private void ReadAndValidateIdent()
    {
        _elfFileIdent = ReadReadable<ElfFileIdent>(0);

        if (_elfFileIdent.Magic != 0x464c457f) 
            throw new FormatException("ERROR: Magic number mismatch.");

        if (_elfFileIdent.Architecture == 1)
            is32Bit = true;
        else if (_elfFileIdent.Architecture != 2)
            throw new FormatException($"Invalid arch number (expecting 1 or 2): {_elfFileIdent.Architecture}");

        if (_elfFileIdent.Version != 1)
            throw new FormatException($"ELF Version is not 1? File header has version {_elfFileIdent.Version}");
    }

    private void ReadHeader()
    {
        _elfHeader = ReadReadable<ElfFileHeader>(0x10);

        InstructionSetId = _elfHeader.Machine switch
        {
            0x03 => DefaultInstructionSets.X86_32,
            0x3E => DefaultInstructionSets.X86_64,
            0x28 => DefaultInstructionSets.ARM_V7,
            0xB7 => DefaultInstructionSets.ARM_V8,
            _ => throw new NotImplementedException($"ELF Machine {_elfHeader.Machine} not implemented")
        };

        if (_elfHeader.Version != 1)
            throw new FormatException($"Full ELF header specifies version {_elfHeader.Version}, only supported version is 1.");
    }

    private void ReadProgramHeaderTable()
    {
        _elfProgramHeaderEntries = is32Bit
            ? ReadReadableArrayAtRawAddr<ElfProgramHeaderEntry32>(_elfHeader!.pProgramHeader, _elfHeader.ProgramHeaderEntryCount).Cast<IElfProgramHeaderEntry>().ToList()
            : ReadReadableArrayAtRawAddr<ElfProgramHeaderEntry64>(_elfHeader!.pProgramHeader, _elfHeader.ProgramHeaderEntryCount).Cast<IElfProgramHeaderEntry>().ToList();
    }

    private IElfProgramHeaderEntry? GetProgramHeaderOfType(ElfProgramEntryType type) => _elfProgramHeaderEntries.FirstOrDefault(p => p.Type == type);

    private IEnumerable<ElfSectionHeaderEntry> GetSections(ElfSectionEntryType type) => _elfSectionHeaderEntries.Where(s => s.Type == type);

    private ElfSectionHeaderEntry? GetSingleSection(ElfSectionEntryType type) => GetSections(type).FirstOrDefault();

    private ElfDynamicEntry? GetDynamicEntryOfType(ElfDynamicType type) => _dynamicSection.FirstOrDefault(d => d.Tag == type);

    private void ProcessRelocations()
    {
        try
        {
            var rels = new HashSet<ElfRelocation>();

            var relSectionStarts = new HashSet<ulong>();

            
            foreach (var section in GetSections(ElfSectionEntryType.SHT_REL))
            {
                
                var relatedTablePointer = _elfSectionHeaderEntries[section.LinkedSectionIndex].RawAddress;

                
                var table = ReadReadableArrayAtRawAddr<ElfRelEntry>((long)section.RawAddress, (long)(section.Size / (ulong)section.EntrySize));

                LibLogger.VerboseNewline($"\t\t-Got {table.Length} from REL section {section.Name}");

                relocationBlocks.Add((section.RawAddress, section.RawAddress + section.Size));
                relSectionStarts.Add(section.RawAddress);

                
                rels.UnionWith(table.Select(r => new ElfRelocation(this, r, relatedTablePointer)));
            }

            
            foreach (var section in GetSections(ElfSectionEntryType.SHT_RELA))
            {
                if (relSectionStarts.Contains(section.RawAddress))
                {
                    LibLogger.VerboseNewline($"\t\t-Ignoring RELA section starting at 0x{section.RawAddress} because it's already been processed.");
                    continue;
                }

                
                var relatedTablePointer = _elfSectionHeaderEntries[section.LinkedSectionIndex].RawAddress;

                
                var table = ReadReadableArrayAtRawAddr<ElfRelaEntry>((long)section.RawAddress, (long)(section.Size / (ulong)section.EntrySize));

                LibLogger.VerboseNewline($"\t\t-Got {table.Length} from RELA section {section.Name} at 0x{section.RawAddress}");

                relocationBlocks.Add((section.RawAddress, section.RawAddress + section.Size));
                relSectionStarts.Add(section.RawAddress);

                
                rels.UnionWith(table.Select(r => new ElfRelocation(this, r, relatedTablePointer)));
            }

            
            if (GetDynamicEntryOfType(ElfDynamicType.DT_REL) is { } dt_rel && (uint)MapVirtualAddressToRaw(dt_rel.Value) is { } dtRelStartAddr)
            {
                if (!relSectionStarts.Contains(dtRelStartAddr))
                {
                    
                    var relocationSectionSize = GetDynamicEntryOfType(ElfDynamicType.DT_RELSZ)!.Value;
                    var relCount = (int)(relocationSectionSize / GetDynamicEntryOfType(ElfDynamicType.DT_RELENT)!.Value);
                    var entries = ReadReadableArrayAtRawAddr<ElfRelEntry>(dtRelStartAddr, relCount);

                    LibLogger.VerboseNewline($"\t\t-Got {entries.Length} from dynamic REL section at 0x{dtRelStartAddr}");

                    
                    var pSymTab = GetDynamicEntryOfType(ElfDynamicType.DT_SYMTAB)!.Value;

                    relocationBlocks.Add((dtRelStartAddr, dtRelStartAddr + relocationSectionSize));

                    rels.UnionWith(entries.Select(r => new ElfRelocation(this, r, pSymTab)));
                }
                else
                {
                    LibLogger.VerboseNewline($"\t\t-Ignoring dynamic REL section starting at 0x{dtRelStartAddr} because it's already been processed.");
                }
            }

            
            if (GetDynamicEntryOfType(ElfDynamicType.DT_RELA) is { } dt_rela)
            {
                
                var relocationSectionSize = GetDynamicEntryOfType(ElfDynamicType.DT_RELASZ)!.Value;
                var relCount = (int)(relocationSectionSize / GetDynamicEntryOfType(ElfDynamicType.DT_RELAENT)!.Value);
                var startAddr = (uint)MapVirtualAddressToRaw(dt_rela.Value);

                if (!relSectionStarts.Contains(startAddr))
                {
                    var entries = ReadReadableArrayAtRawAddr<ElfRelaEntry>(startAddr, relCount);

                    LibLogger.VerboseNewline($"\t\t-Got {entries.Length} from dynamic RELA section at 0x{startAddr}");

                    
                    var pSymTab = GetDynamicEntryOfType(ElfDynamicType.DT_SYMTAB)!.Value;

                    relocationBlocks.Add((startAddr, startAddr + relocationSectionSize));

                    rels.UnionWith(entries.Select(r => new ElfRelocation(this, r, pSymTab)));
                }
                else
                {
                    LibLogger.VerboseNewline($"\t\t-Ignoring dynamic RELA section starting at 0x{startAddr} because it's already been processed.");
                }
            }

            var sizeOfRelocationStruct = (ulong)(is32Bit ? ElfDynamicSymbol32.StructSize : ElfDynamicSymbol64.StructSize);

            LibLogger.Verbose($"\t-Now Processing {rels.Count} relocations...");

            foreach (var rel in rels)
            {
                var pointer = rel.pRelatedSymbolTable + rel.IndexInSymbolTable * sizeOfRelocationStruct;
                ulong symValue;
                try
                {
                    symValue = ((IElfDynamicSymbol)(is32Bit ? ReadReadable<ElfDynamicSymbol32>((long)pointer) : ReadReadable<ElfDynamicSymbol64>((long)pointer))).Value;
                }
                catch
                {
                    LibLogger.ErrorNewline($"Exception reading dynamic symbol for rel of type {rel.Type} at pointer 0x{pointer:X} (length of file is 0x{RawLength:X}, pointer - length is 0x{pointer - (ulong)RawLength:X})");
                    throw;
                }

                long targetLocation;
                try
                {
                    targetLocation = MapVirtualAddressToRaw(rel.Offset);
                }
                catch (InvalidOperationException)
                {
                    continue; 
                }

                
                ulong addend;
                if (rel.Addend.HasValue)
                    addend = rel.Addend.Value;
                else
                {
                    Position = targetLocation;
                    addend = ReadUInt64();
                }

                

                ulong newValue;
                bool recognized;
                if (InstructionSetId == DefaultInstructionSets.ARM_V7)
                    (newValue, recognized) = rel.Type switch
                    {
                        ElfRelocationType.R_ARM_ABS32 => (symValue + addend, true), 
                        ElfRelocationType.R_ARM_REL32 => (symValue + rel.Offset - addend, true), 
                        ElfRelocationType.R_ARM_COPY => (symValue, true), 
                        _ => (0UL, false)
                    };
                else if (InstructionSetId == DefaultInstructionSets.ARM_V8)
                    (newValue, recognized) = rel.Type switch
                    {
                        ElfRelocationType.R_AARCH64_ABS64 => (symValue + addend, true), 
                        ElfRelocationType.R_AARCH64_PREL64 => (symValue + addend - rel.Offset, true), 
                        ElfRelocationType.R_AARCH64_GLOB_DAT => (symValue + addend, true), 
                        ElfRelocationType.R_AARCH64_JUMP_SLOT => (symValue + addend, true), 
                        ElfRelocationType.R_AARCH64_RELATIVE => (symValue + addend, true), 
                        _ => (0UL, false)
                    };
                else if (InstructionSetId == DefaultInstructionSets.X86_32)
                    (newValue, recognized) = rel.Type switch
                    {
                        ElfRelocationType.R_386_32 => (symValue + addend, true), 
                        ElfRelocationType.R_386_PC32 => (symValue + addend - rel.Offset, true), 
                        ElfRelocationType.R_386_GLOB_DAT => (symValue, true), 
                        ElfRelocationType.R_386_JMP_SLOT => (symValue, true), 
                        _ => (0UL, false)
                    };
                else if (InstructionSetId == DefaultInstructionSets.X86_64)
                    (newValue, recognized) = rel.Type switch
                    {
                        ElfRelocationType.R_AMD64_64 => (symValue + addend, true), 
                        ElfRelocationType.R_AMD64_RELATIVE => (addend, true), 

                        _ => (0UL, false)
                    };
                else
                    (newValue, recognized) = (0UL, false);

                if (recognized)
                {
                    WriteWord((int)targetLocation, newValue);
                }
            }
        }
        catch
        {
            LibLogger.Info("Exception during relocation mapping!");
            throw;
        }
    }

    private void ProcessSymbols()
    {
        var symbolTables = new List<(ulong offset, ulong count, ulong strings)>();

        
        if (GetSingleSection(ElfSectionEntryType.SHT_STRTAB) is { } strTab)
        {
            
            if (GetSingleSection(ElfSectionEntryType.SHT_SYMTAB) is { } symtab)
            {
                LibLogger.VerboseNewline($"\t\t-Found .symtab at 0x{symtab.RawAddress:X}");
                symbolTables.Add((symtab.RawAddress, symtab.Size / (ulong)symtab.EntrySize, strTab.RawAddress));
            }

            
            if (GetSingleSection(ElfSectionEntryType.SHT_DYNSYM) is { } dynsym)
            {
                LibLogger.VerboseNewline($"\t\t-Found .dynsym at 0x{dynsym.RawAddress:X}");
                symbolTables.Add((dynsym.RawAddress, dynsym.Size / (ulong)dynsym.EntrySize, strTab.RawAddress));
            }
        }

        
        if (GetDynamicEntryOfType(ElfDynamicType.DT_STRTAB) is { } dynamicStrTab)
        {
            if (GetDynamicEntryOfType(ElfDynamicType.DT_SYMTAB) is { } dynamicSymTab)
            {
                var end = _dynamicSection.Where(x => x.Value > dynamicSymTab.Value).OrderBy(x => x.Value).First().Value;
                var dynSymSize = is32Bit ? 18ul : 24ul;

                var address = (ulong)MapVirtualAddressToRaw(dynamicSymTab.Value);

                LibLogger.VerboseNewline($"\t\t-Found DT_SYMTAB at 0x{address:X}");

                symbolTables.Add((
                    address,
                    (end - dynamicSymTab.Value) / dynSymSize,
                    dynamicStrTab.Value
                ));
            }
        }

        _symbolTable.Clear();
        _exportNameTable.Clear();
        _exportAddressTable.Clear();

        
        foreach (var (offset, count, stringTable) in symbolTables)
        {
            var symbols = is32Bit
                ? ReadReadableArrayAtRawAddr<ElfDynamicSymbol32>((long)offset, (long)count).Cast<IElfDynamicSymbol>().ToList()
                : ReadReadableArrayAtRawAddr<ElfDynamicSymbol64>((long)offset, (long)count).Cast<IElfDynamicSymbol>().ToList();

            LibLogger.VerboseNewline($"\t\t-Found {symbols.Count} symbols in table at 0x{offset:X}");

            foreach (var symbol in symbols)
            {
                string name;
                try
                {
                    name = ReadStringToNull(stringTable + symbol.NameOffset);
                }
                catch (ArgumentOutOfRangeException)
                {
                    
                    continue;
                }

                var usefulType = symbol.Shndx == 0 ? ElfSymbolTableEntry.ElfSymbolEntryType.Import
                    : symbol.Type == ElfDynamicSymbolType.STT_FUNC ? ElfSymbolTableEntry.ElfSymbolEntryType.Function
                    : symbol.Type == ElfDynamicSymbolType.STT_OBJECT || symbol.Type == ElfDynamicSymbolType.STT_COMMON ? ElfSymbolTableEntry.ElfSymbolEntryType.Name
                    : ElfSymbolTableEntry.ElfSymbolEntryType.Unknown;

                var virtualAddress = symbol.Value;

                var entry = new ElfSymbolTableEntry { Name = name, Type = usefulType, VirtualAddress = virtualAddress };
                _symbolTable.Add(entry);

                if (symbol.Shndx != 0)
                {
                    _exportNameTable.TryAdd(name, entry);
                    _exportAddressTable.TryAdd(virtualAddress, entry);
                }
            }
        }
    }

    private void ProcessInitializers()
    {
        if (!(GetDynamicEntryOfType(ElfDynamicType.DT_INIT_ARRAY) is { } dtInitArray) || !(GetDynamicEntryOfType(ElfDynamicType.DT_INIT_ARRAYSZ) is { } dtInitArraySz))
        {
            _initializerPointers = [];
            return;
        }

        var pInitArray = MapVirtualAddressToRaw(dtInitArray.Value);
        var count = (int)dtInitArraySz.Value / (is32Bit ? 4 : 8);

        var initArray = ReadNUintArrayAtRawAddress(pInitArray, count);

        if (GetDynamicEntryOfType(ElfDynamicType.DT_INIT) is { } dtInit)
            initArray = initArray.Append(dtInit.Value).ToArray();

        _initializerPointers = initArray.Select(a => MapVirtualAddressToRaw(a)).ToList();
    }

    public override (ulong pCodeRegistration, ulong pMetadataRegistration) FindCodeAndMetadataReg(Il2CppMetadata metadata)
    {
        

        LibLogger.Verbose("\tChecking ELF Symbol Table for code and/or meta reg...");
        ulong codeReg = 0;
        ulong metadataReg = 0;
        if (_symbolTable.FirstOrDefault(s => s.Name.Contains("g_CodeRegistration")) is { } codeRegSymbol)
            codeReg = codeRegSymbol.VirtualAddress;

        if (_symbolTable.FirstOrDefault(s => s.Name.Contains("g_MetadataRegistration")) is { } metaRegSymbol)
            metadataReg = metaRegSymbol.VirtualAddress;

        if (codeReg != 0 && metadataReg != 0)
        {
            LibLogger.VerboseNewline("Found them.");
            return (codeReg, metadataReg);
        }

        LibLogger.VerboseNewline("Didn't find them, scanning binary...");

        
        if (InstructionSetId == DefaultInstructionSets.ARM_V7 && metadata.MetadataVersion < 24.2f)
        {
            var ret = FindCodeAndMetadataRegArm32();
            if (ret != (0, 0))
                return ret;
        }

        if (InstructionSetId == DefaultInstructionSets.ARM_V8 && metadata.MetadataVersion < 24.2f)
        {
            var ret = FindCodeAndMetadataRegArm64();
            if (ret != (0, 0))
                return ret;
        }

        return FindCodeAndMetadataRegDefaultBehavior(metadata);
    }

    private (ulong codeReg, ulong metaReg) FindCodeAndMetadataRegArm32()
    {
        
        
        
        

        
        
        
        var addSearchBytes = new byte[] { 0x00, 0x00, 0x8F, 0xE0, 0x01, 0x10, 0x8F, 0xE0 };

        
        
        var ldrSearchBytes = new byte[] { 0x10, 0x9F, 0xE5 };

        LibLogger.VerboseNewline($"\tARM-32 MODE: Checking {_initializerPointers!.Count} initializer pointers...");
        foreach (var initializerPointer in _initializerPointers!) 
        {
            
            var instructionBytes = ReadByteArrayAtRawAddress(initializerPointer, 0x18);

            
            if (!addSearchBytes.SequenceEqual(instructionBytes.Skip(0x10)))
                continue;

            
            if (!ldrSearchBytes.SequenceEqual(instructionBytes.Skip(9).Take(3)))
                continue;

            
            
            
            
            var pointerToPointerToCodegenRegFunction = instructionBytes[8] + initializerPointer + 0x10;

            
            
            Position = pointerToPointerToCodegenRegFunction;
            var pointerToCodegenRegFunction = ReadUInt32();

            
            pointerToCodegenRegFunction += (uint)initializerPointer + 0x1C;

            
            
            var functionBody = ReadClassArrayAtRawAddr<uint>(pointerToCodegenRegFunction, 10);

            
            if (functionBody[6].Bits(24, 8) != 0b_1110_1010)
                continue;

            
            var registerOffsets = new uint[3];

            var fail = false;
            for (var i = 0u; i <= 2u && !fail; i++)
            {
                var (registerNum, immediate) = ArmUtils.GetOperandsForLiteralLdr(functionBody[i]);
                if (registerNum > 2 || immediate == 0) 
                    fail = true;
                else
                    registerOffsets[registerNum] = immediate + i * 4 + 8; 
            }

            if (fail)
                continue;

            
            
            

            var pointers = new uint[3];

            for (var i = 3u; i <= 5 && !fail; i++)
            {
                var (addFirstReg, addSecondReg, addThirdReg) = ArmUtils.GetOperandsForRegisterAdd(functionBody[i]);
                var (ldrFirstReg, ldrSecondReg, ldrThirdReg) = ArmUtils.GetOperandsForRegisterLdr(functionBody[i]);

                if (addSecondReg == ArmUtils.PC_REG && addFirstReg == addThirdReg && addFirstReg <= 2)
                    
                    pointers[addFirstReg] = pointerToCodegenRegFunction + i * 4 + functionBody[registerOffsets[addFirstReg] / 4] + 8;
                else if (ldrSecondReg == ArmUtils.PC_REG && ldrFirstReg == ldrThirdReg && ldrFirstReg <= 2)
                {
                    
                    var p = pointerToCodegenRegFunction + i * 4 + functionBody[registerOffsets[ldrFirstReg] / 4] + 8;
                    
                    
                    pointers[ldrFirstReg] = (uint)ReadPointerAtVirtualAddress(p);
                }
                else
                    fail = true;
            }

            if (fail)
            {
                LibLogger.VerboseNewline($"\t\tInitializer function at 0x{initializerPointer:X} is probably NOT the il2cpp initializer.");
                continue;
            }

            LibLogger.VerboseNewline($"\t\tFound valid sequence of bytes for il2cpp initializer function at 0x{initializerPointer:X}.");

            return (pointers[0], pointers[1]);
        }

        return (0, 0);
    }

    private (ulong codeReg, ulong metaReg) FindCodeAndMetadataRegArm64()
    {
        LibLogger.VerboseNewline($"\tARM-64 MODE: Checking {_initializerPointers!.Count} initializer pointers...");
        foreach (var initializerPointer in _initializerPointers)
        {
            
            var func = MiniArm64Decompiler.ReadFunctionAtRawAddress(this, (uint)initializerPointer, 7);

            
            
            if (!MiniArm64Decompiler.IsB(func[^1]))
                continue;

            var registers = MiniArm64Decompiler.GetAddressesLoadedIntoRegisters(func, (ulong)(_globalOffset + initializerPointer), this);

            
            
            if (registers.Count == 2 && registers.ContainsKey(0) && registers.TryGetValue(1, out var x1))
            {
                
                var secondFunc = MiniArm64Decompiler.ReadFunctionAtRawAddress(this, (uint)MapVirtualAddressToRaw(x1), 7);

                if (!MiniArm64Decompiler.IsB(secondFunc[^1]))
                    continue;

                registers = MiniArm64Decompiler.GetAddressesLoadedIntoRegisters(secondFunc, x1, this);
            }

            
            
            
            if (registers.Count == 3 && registers.TryGetValue(0, out var x0) && registers.TryGetValue(1, out x1) && registers.ContainsKey(2))
            {
                LibLogger.VerboseNewline($"\t\tFound valid sequence of bytes for il2cpp initializer function at 0x{initializerPointer:X}.");
                return (x0, x1);
            }

            

            LibLogger.VerboseNewline($"\t\tInitializer function at 0x{initializerPointer:X} is probably NOT the il2cpp initializer - got {registers.Count} register values with keys {string.Join(", ", registers.Keys)}.");
        }

        return (0, 0);
    }

    private (ulong codeReg, ulong metaReg) FindCodeAndMetadataRegDefaultBehavior(Il2CppMetadata metadata)
    {
        var methodCount = metadata.methodDefs.Count(x => x.methodIndex >= 0);
        var typeDefinitionsCount = metadata.TypeDefinitionCount;
        
        LibLogger.VerboseNewline("Searching for il2cpp structures in an ELF binary using non-arch-specific method...");
        var searcher = new BinarySearcher(this, methodCount, typeDefinitionsCount);

        LibLogger.VerboseNewline("\tLooking for code reg (this might take a while)...");
        var codeReg = metadata.MetadataVersion >= 24.2f ? searcher.FindCodeRegistrationPost2019(metadata) : searcher.FindCodeRegistrationPre2019();
        LibLogger.VerboseNewline($"\tGot code reg 0x{codeReg:X}");

        LibLogger.VerboseNewline($"\tLooking for meta reg ({(metadata.MetadataVersion >= 27f ? "post-27" : "pre-27")})...");
        var metaReg = metadata.MetadataVersion >= 27f ? searcher.FindMetadataRegistrationPost24_5(metadata) : searcher.FindMetadataRegistrationPre24_5();
        LibLogger.VerboseNewline($"\tGot meta reg 0x{metaReg:x}");

        return (codeReg, metaReg);
    }

    public override long RawLength => _raw.Length;

    public override long MapVirtualAddressToRaw(ulong addr, bool throwOnError = true)
    {
        var section = _elfProgramHeaderEntries.FirstOrDefault(x => addr >= x.VirtualAddress && addr < x.VirtualAddress + x.VirtualSize);

        if (section == null)
            if (throwOnError)
                throw new InvalidOperationException($"No entry in the Elf PHT contains virtual address 0x{addr:X}");
            else
                return VirtToRawInvalidNoMatch;

        if (addr >= section.VirtualAddress + section.RawSize)
            if (throwOnError)
                throw new InvalidOperationException(
                    $"Virtual address {section.VirtualAddress:X} is located outside of the file-backed portion of Elf PHT section at 0x{section.VirtualAddress:X}");
            else
                return VirtToRawInvalidOutOfBounds;

        return (long)(addr - (section.VirtualAddress - section.RawAddress));
    }

    public override ulong MapRawAddressToVirtual(uint offset)
    {
        if (relocationBlocks.Any(b => b.start <= offset && b.end > offset))
            throw new InvalidOperationException("Attempt to map a relocation block to a virtual address");

        var section = _elfProgramHeaderEntries.First(x => offset >= x.RawAddress && offset < x.RawAddress + x.RawSize);

        return section.VirtualAddress + offset - section.RawAddress;
    }

    public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];

    public override ulong GetRva(ulong pointer) => (ulong)((long)pointer - _globalOffset);

    public override byte[] GetRawBinaryContent() => _raw;

    public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
    {
        if (!_exportNameTable.TryGetValue(toFind, out var exportedSymbol))
            return 0;

        return exportedSymbol.VirtualAddress;
    }

    public override bool IsExportedFunction(ulong addr) => _exportAddressTable.ContainsKey(addr);

    public override bool TryGetExportedFunctionName(ulong addr, [NotNullWhen(true)] out string? name)
    {
        if (_exportAddressTable.TryGetValue(addr, out var symbol))
        {
            name = symbol.Name;
            return true;
        }
        else
        {
            return base.TryGetExportedFunctionName(addr, out name);
        }
    }

    public override IEnumerable<KeyValuePair<string, ulong>> GetExportedFunctions()
    {
        return _exportNameTable.Select(kv => new KeyValuePair<string, ulong>(kv.Key, kv.Value.VirtualAddress));
    }

    public override ulong GetVirtualAddressOfPrimaryExecutableSection() => _elfSectionHeaderEntries.FirstOrDefault(s => s.Name == ".text")?.VirtualAddress ?? 0;

    public override byte[] GetEntirePrimaryExecutableSection()
    {
        var primarySection = _elfSectionHeaderEntries.FirstOrDefault(s => s.Name == ".text");

        if (primarySection == null)
            return [];

        return GetRawBinaryContent().SubArray((int)primarySection.RawAddress, (int)primarySection.Size);
    }
}
