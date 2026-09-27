using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using LibCpp2IL.NintendoSwitch;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Il2CppApiFunctions;

public class Arm64KeyFunctionAddresses : BaseKeyFunctionAddresses
{
    private List<Arm64Instruction> _allInstructions = [];

    protected override void Init(ApplicationAnalysisContext context)
    {
        var disassembler = CapstoneDisassembler.CreateArm64Disassembler(context.Binary.IsBigEndian ? Arm64DisassembleMode.BigEndian : Arm64DisassembleMode.LittleEndian);
        disassembler.EnableInstructionDetails = true;
        disassembler.DisassembleSyntax = DisassembleSyntax.Intel;
        disassembler.EnableSkipDataMode = true;

        var primaryExecutableSection = context.Binary.GetEntirePrimaryExecutableSection();
        var primaryExecutableSectionVa = context.Binary.GetVirtualAddressOfPrimaryExecutableSection();
        var endOfTextSection = primaryExecutableSectionVa + (ulong)primaryExecutableSection.Length;

        Logger.InfoNewline("\tRunning entire .text section through Arm64 disassembler, this might take up to several minutes for large games, and may fail on large games if you have <16GB ram...");

        Logger.VerboseNewline($"\tPrimary executable section is {primaryExecutableSection.Length} bytes, starting at 0x{primaryExecutableSectionVa:X} and extending to 0x{endOfTextSection:X}");
        var attributeGeneratorList = SharedState.AttributeGeneratorStarts.ToList();
        attributeGeneratorList.SortByExtractedKey(a => a);

        if (attributeGeneratorList.Count > 0)
        {
            if (context.Binary is not NsoFile)
            {
                Logger.VerboseNewline($"\tLast attribute generator function is at address 0x{attributeGeneratorList[^1]:X}. Skipping everything before that.");

                
                
                
                
                var oldLength = primaryExecutableSection.Length;

                var toRemove = (int)(attributeGeneratorList[^1] - primaryExecutableSectionVa);
                primaryExecutableSection = primaryExecutableSection.Skip(toRemove).ToArray();

                primaryExecutableSectionVa = attributeGeneratorList[^1];

                Logger.VerboseNewline($"\tBy trimming out attribute generator functions, reduced decompilation work by {toRemove} of {oldLength} bytes (a {toRemove * 100 / (float)oldLength:f1}% saving)");

                
                
                
                var methodAddresses = new List<ulong>();
                methodAddresses.SortByExtractedKey(a => a);

                if (methodAddresses[0] < endOfTextSection && context.Binary.GetVirtualAddressOfExportedFunctionByName("il2cpp_object_new") != 0)
                {
                    var exportAddresses = new[] { "il2cpp_object_new", "il2cpp_value_box", "il2cpp_runtime_class_init", "il2cpp_array_new_specific", "il2cpp_type_get_object", "il2cpp_resolve_icall", "il2cpp_string_new", "il2cpp_string_new_wrapper", "il2cpp_raise_exception" }.Select(context.Binary.GetVirtualAddressOfExportedFunctionByName).Where(a => a > 0).ToArray();

                    var lastExport = exportAddresses.Max();
                    var firstExport = exportAddresses.Min();

                    Logger.VerboseNewline($"\tDetected that the il2cpp-ified managed methods are in the .text section and api functions are available. Attempting to trim out managed methods for KFA scanning - the first managed method is at 0x{methodAddresses[0]:X} and the last at 0x{methodAddresses[^1]:X}, " +
                                          $"the first export function is at 0x{firstExport:X} and the last at 0x{lastExport:X}");

                    
                    var startFrom = Math.Min(firstExport, methodAddresses[^1]);

                    
                    if (startFrom > 0x100_0000)
                        startFrom -= 0x10_0000;

                    Logger.VerboseNewline($"\tTrimming everything before 0x{startFrom:X}.");
                    oldLength = primaryExecutableSection.Length;

                    toRemove = (int)(startFrom - primaryExecutableSectionVa);
                    primaryExecutableSection = primaryExecutableSection.Skip(toRemove).ToArray();

                    primaryExecutableSectionVa = startFrom;

                    Logger.VerboseNewline($"\tBy trimming out most of the il2cpp-ified managed methods, reduced decompilation work by {toRemove} of {oldLength} bytes (a {toRemove * 100L / (float)oldLength:f1}% saving)");
                }
                else if (methodAddresses[0] < endOfTextSection)
                {
                    Logger.VerboseNewline($"\tDetected that the il2cpp-ified managed methods are in the .text section, but api functions are not available. Attempting to (conservatively) trim out managed methods for KFA scanning - the first managed method is at 0x{methodAddresses[0]:X} and the last at 0x{methodAddresses[^1]:X}");

                    var startFrom = methodAddresses[^1];

                    
                    if (startFrom > 0x100_0000)
                        startFrom -= 0x10_0000;

                    Logger.VerboseNewline($"\tTrimming everything before 0x{startFrom:X}.");
                    oldLength = primaryExecutableSection.Length;

                    toRemove = (int)(startFrom - primaryExecutableSectionVa);
                    primaryExecutableSection = primaryExecutableSection.Skip(toRemove).ToArray();

                    primaryExecutableSectionVa = startFrom;

                    Logger.VerboseNewline($"\tBy trimming out most of the il2cpp-ified managed methods, reduced decompilation work by {toRemove} of {oldLength} bytes (a {toRemove * 100L / (float)oldLength:f1}% saving)");
                }
            }
            else
            {
                
                
                Logger.VerboseNewline($"\tNSO: Last attribute generator function is at address 0x{attributeGeneratorList[^1]:X}. Skipping everything after that.");

                var oldLength = primaryExecutableSection.Length;

                var toKeep = (int)(attributeGeneratorList[^1] - primaryExecutableSectionVa);
                primaryExecutableSection = primaryExecutableSection.SubArray(..toKeep);

                
                

                Logger.VerboseNewline($"\tBy trimming out everything after and including attribute generator functions, reduced decompilation work by {oldLength - toKeep} of {oldLength} bytes (a {(oldLength - toKeep) * 100L / (float)oldLength:f1}% saving)");
            }
        }

        _allInstructions = disassembler.Disassemble(primaryExecutableSection, (long)primaryExecutableSectionVa).ToList();
    }

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
    {
        var allBranchesToAddr = _allInstructions.Where(i => i.Mnemonic is "b" or "bl")
            .Where(i => i.Details.Operands[0].IsImmediate() && i.Details.Operands[0].Immediate == (long)addr)
            .ToList();

        foreach (var potentialBranch in allBranchesToAddr)
        {
            if (addressesToIgnore.Contains((ulong)potentialBranch.Address))
                continue;

            var backtrack = 0;
            var idx = _allInstructions.IndexOf(potentialBranch);

            do
            {
                
                
                
                if (idx - backtrack > 0)
                {
                    var prevInstruction = _allInstructions[idx - backtrack - 1];

                    if (addressesToIgnore.Contains((ulong)prevInstruction.Address))
                    {
                        backtrack++;
                        continue;
                    }

                    if (prevInstruction.IsSkippedData && prevInstruction.Bytes.All(b => b == 0))
                    {
                        
                        yield return (ulong)potentialBranch.Address - (ulong)(backtrack * 4);
                        break;
                    }

                    if (prevInstruction.Mnemonic is "adrp" or "str")
                    {
                        
                        break;
                    }

                    if (prevInstruction.Mnemonic is "b" or "bl")
                    {
                        
                        yield return (ulong)potentialBranch.Address - (ulong)(backtrack * 4);
                        break;
                    }

                    if (prevInstruction.Mnemonic is "ret")
                    {
                        
                        yield return (ulong)potentialBranch.Address - (ulong)(backtrack * 4);
                        break;
                    }
                }

                

                backtrack++;
            } while (backtrack * 4 < maxBytesBack);
        }
    }

    protected override ulong GetObjectIsInstFromSystemType()
    {
        Logger.Verbose("\tTrying to use System.Type::IsInstanceOfType to find il2cpp::vm::Object::IsInst...");
        var typeIsInstanceOfType = LibCpp2IlReflection.GetType("Type", "System")?.Methods?.FirstOrDefault(m => m.Name == "IsInstanceOfType");
        if (typeIsInstanceOfType == null)
        {
            Logger.VerboseNewline("Type or method not found, aborting.");
            return 0;
        }

        
        
        
        

        Logger.Verbose($"IsInstanceOfType found at 0x{typeIsInstanceOfType.MethodPointer:X}...");
        var instructions = Arm64Utils.GetArm64MethodBodyAtVirtualAddress(typeIsInstanceOfType.MethodPointer, false);

        var lastCall = instructions.LastOrDefault(i => i.Mnemonic == "bl");

        if (lastCall == null)
        {
            Logger.VerboseNewline("Method does not match expected signature. Aborting.");
            return 0;
        }

        Logger.VerboseNewline($"Success. IsInst found at 0x{lastCall.Details.Operands[0].Immediate:X}");
        return (ulong)lastCall.Details.Operands[0].Immediate;
    }

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
    {
        var idx = _allInstructions.FindIndex(i => i.Address == (long)thunkPtr);

        if (idx < 0)
            return 0;

        
        if (_allInstructions[idx].Mnemonic is "b" or "bl")
            return (ulong)_allInstructions[idx].Details.Operands[0].Immediate;

        

        for (var i = 0; i <= 12; i++)
        {
            idx++;
            if (_allInstructions[idx].Mnemonic is "b" or "bl")
                return (ulong)_allInstructions[idx].Details.Operands[0].Immediate;
        }

        return 0;
    }

    protected override int GetCallerCount(ulong toWhere) => _allInstructions.Where(i => i.Mnemonic is "b" or "bl")
        .Count(i => i.Details.Operands[0].IsImmediate() && i.Details.Operands[0].Immediate == (long)toWhere);

    protected override void AttemptInstructionAnalysisToFillGaps()
    {
        if (il2cpp_object_new == 0)
        {
            Logger.Verbose("\tAttempting to use Array GetEnumerator to find il2cpp_codegen_object_new...");
            if (LibCpp2IlReflection.GetType("Array", "System") is { } arrayTypeDef)
            {
                if (arrayTypeDef.Methods!.FirstOrDefault(m => m.Name == "GetEnumerator") is { } methodDef)
                {
                    var ptr = methodDef.MethodPointer;
                    var body = Arm64Utils.GetArm64MethodBodyAtVirtualAddress(ptr);

                    
                    var probableResult = 0L;
                    var numSeen = 0;
                    for (var i = 0; i < body.Count - 4; i++)
                    {
                        if (body[i].Mnemonic != "adrp" || body[i + 1].Mnemonic != "ldr" || body[i + 2].Mnemonic != "ldr" || body[i + 3].Mnemonic != "bl")
                            continue;

                        if (numSeen++ < 2) 
                            probableResult = body[i + 3].Details.Operands[0].Immediate;
                    }

                    if (probableResult > 0)
                    {
                        Logger.Verbose($"Probably found at 0x{probableResult:X}...");

                        
                        var thunk = FindFunctionThisIsAThunkOf((ulong)probableResult);
                        long addrVmObjectNew;
                        if (thunk > 0)
                            
                            addrVmObjectNew = (long)thunk;
                        else
                            
                            addrVmObjectNew = probableResult;

                        var allThunks = FindAllThunkFunctions((ulong)addrVmObjectNew, 16, (ulong)probableResult).ToList();

                        allThunks.SortByExtractedKey(GetCallerCount); 
                        allThunks.Reverse(); 

                        il2cpp_object_new = allThunks.FirstOrDefault(); 

                        Logger.VerboseNewline($"Leaving il2cpp_object_new at 0x{il2cpp_object_new:X}");
                    }
                    else
                        Logger.VerboseNewline("Couldn't find a matching instruction, can't be used.");
                }
                else
                    Logger.VerboseNewline("Method stripped, can't be used.");
            }
            else
                Logger.VerboseNewline("Type stripped, can't be used.");
        }
    }
}
