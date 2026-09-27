using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Il2CppApiFunctions;

public class X86KeyFunctionAddresses : BaseKeyFunctionAddresses
{
    private InstructionList? _cachedDisassembledBytes;

    private InstructionList DisassembleTextSection()
    {
        if (_cachedDisassembledBytes == null)
        {
            var toDisasm = LibCpp2IlMain.Binary!.GetEntirePrimaryExecutableSection();
            _cachedDisassembledBytes = X86Utils.Disassemble(toDisasm, LibCpp2IlMain.Binary.GetVirtualAddressOfPrimaryExecutableSection());
        }

        return _cachedDisassembledBytes;
    }

    public override void Find(ApplicationAnalysisContext applicationAnalysisContext)
    {
        base.Find(applicationAnalysisContext);

        _cachedDisassembledBytes = null; 
    }

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
    {
        
        var allInstructions = DisassembleTextSection();

        
        var matchingJmps = allInstructions.Where(i => i.Mnemonic is Mnemonic.Jmp or Mnemonic.Call && i.NearBranchTarget == addr).ToList();

        foreach (var matchingJmp in matchingJmps)
        {
            if (addressesToIgnore.Contains(matchingJmp.IP)) continue;

            
            var offsetInPe = (ulong)LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(matchingJmp.IP);
            if (offsetInPe == 0 || offsetInPe == (ulong)(LibCpp2IlMain.Binary!.RawLength - 1))
                continue;

            
            var previousByte = LibCpp2IlMain.Binary.GetByteAtRawAddress(offsetInPe - 1);
            var nextByte = LibCpp2IlMain.Binary.GetByteAtRawAddress(offsetInPe + (ulong)matchingJmp.Length);

            
            if (previousByte == 0xCC && nextByte == 0xCC)
            {
                yield return matchingJmp.IP;
                continue;
            }

            if (nextByte == 0xCC && maxBytesBack > 0)
            {
                for (ulong backtrack = 1; backtrack < maxBytesBack && offsetInPe - backtrack > 0; backtrack++)
                {
                    if (addressesToIgnore.Contains(matchingJmp.IP - (backtrack - 1)))
                        
                        break;

                    if (LibCpp2IlMain.Binary!.GetByteAtRawAddress(offsetInPe - backtrack) == 0xCC)
                    {
                        yield return matchingJmp.IP - (backtrack - 1);
                        break;
                    }
                }
            }
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
        var instructions = X86Utils.GetMethodBodyAtVirtAddressNew(typeIsInstanceOfType.MethodPointer, true);

        var lastCall = instructions.LastOrDefault(i => i.Mnemonic == Mnemonic.Call);

        if (lastCall.Mnemonic == Mnemonic.INVALID)
        {
            Logger.VerboseNewline("Method does not match expected signature. Aborting.");
            return 0;
        }

        Logger.VerboseNewline($"Success. IsInst found at 0x{lastCall.NearBranchTarget:X}");
        return lastCall.NearBranchTarget;
    }

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
    {
        var instructions = X86Utils.GetMethodBodyAtVirtAddressNew(thunkPtr, true);

        try
        {
            var target = prioritiseCall ? Mnemonic.Call : Mnemonic.Jmp;
            var matchingCall = instructions.FirstOrDefault(i => i.Mnemonic == target);

            if (matchingCall.Mnemonic == Mnemonic.INVALID)
            {
                target = target == Mnemonic.Call ? Mnemonic.Jmp : Mnemonic.Call;
                matchingCall = instructions.First(i => i.Mnemonic == target);
            }

            return matchingCall.NearBranchTarget;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    protected override int GetCallerCount(ulong toWhere)
    {
        
        var allInstructions = DisassembleTextSection();

        
        return allInstructions.Count(i => i.Mnemonic == Mnemonic.Jmp || i.Mnemonic == Mnemonic.Call && i.NearBranchTarget == toWhere);
    }
}
