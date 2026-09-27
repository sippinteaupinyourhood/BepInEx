using System.Collections.Generic;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Wasm;

public class WasmExportSection : WasmSection
{
    public ulong ExportCount;
    public readonly List<WasmExportEntry> Exports = [];

    internal WasmExportSection(WasmSectionId type, long pointer, ulong size, WasmFile file) : base(type, pointer, size)
    {
        ExportCount = file.BaseStream.ReadLEB128Unsigned();
        for (var i = 0UL; i < ExportCount; i++)
        {
            var export = new WasmExportEntry(file);
            
            
            Exports.Add(export);
        }

        LibLogger.VerboseNewline($"\t\tRead {Exports.Count} exported functions");
    }
}
