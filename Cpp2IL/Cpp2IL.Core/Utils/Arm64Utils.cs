using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils;

public static class Arm64Utils
{
    private static readonly ConcurrentDictionary<Arm64RegisterId, string> CachedArm64RegNamesNew = new();
    private static CapstoneArm64Disassembler? _arm64Disassembler;

    public static string GetRegisterNameNew(Arm64RegisterId registerId)
    {
        var key = registerId;
        if (registerId == Arm64RegisterId.Invalid)
            return "";

        if (CachedArm64RegNamesNew.TryGetValue(key, out var ret))
            return ret;

        
        
        

        
        if (registerId is >= Arm64RegisterId.ARM64_REG_W0 and <= Arm64RegisterId.ARM64_REG_W30)
            registerId = (registerId - Arm64RegisterId.ARM64_REG_W0) + Arm64RegisterId.ARM64_REG_X0;

        if (registerId is >= Arm64RegisterId.ARM64_REG_X0 and <= Arm64RegisterId.ARM64_REG_X28)
        {
            ret = $"x{registerId - Arm64RegisterId.ARM64_REG_X0}";
            CachedArm64RegNamesNew[key] = ret;
            return ret;
        }

        if (registerId is Arm64RegisterId.ARM64_REG_SP)
        {
            CachedArm64RegNamesNew[key] = "sp";
            return "sp";
        }

        if (registerId is Arm64RegisterId.ARM64_REG_FP)
        {
            CachedArm64RegNamesNew[key] = "fp";
            return "fp";
        }

        if (registerId is Arm64RegisterId.ARM64_REG_LR)
        {
            CachedArm64RegNamesNew[key] = "lr";
            return "lr";
        }

        if (registerId is Arm64RegisterId.ARM64_REG_WZR or Arm64RegisterId.ARM64_REG_XZR)
        {
            
            CachedArm64RegNamesNew[key] = "xzr";
            return "xzr";
        }

        
        

        
        if (registerId is >= Arm64RegisterId.ARM64_REG_B0 and <= Arm64RegisterId.ARM64_REG_B31)
            registerId = (registerId - Arm64RegisterId.ARM64_REG_B0) + Arm64RegisterId.ARM64_REG_V0;

        
        if (registerId is >= Arm64RegisterId.ARM64_REG_H0 and <= Arm64RegisterId.ARM64_REG_H31)
            registerId = (registerId - Arm64RegisterId.ARM64_REG_H0) + Arm64RegisterId.ARM64_REG_V0;

        
        if (registerId is >= Arm64RegisterId.ARM64_REG_B0 and <= Arm64RegisterId.ARM64_REG_B31)
            registerId = (registerId - Arm64RegisterId.ARM64_REG_B0) + Arm64RegisterId.ARM64_REG_V0;

        
        if (registerId is >= Arm64RegisterId.ARM64_REG_D0 and <= Arm64RegisterId.ARM64_REG_D31)
            registerId = (registerId - Arm64RegisterId.ARM64_REG_D0) + Arm64RegisterId.ARM64_REG_V0;

        
        if (registerId is >= Arm64RegisterId.ARM64_REG_Q0 and <= Arm64RegisterId.ARM64_REG_Q31)
            registerId = (registerId - Arm64RegisterId.ARM64_REG_Q0) + Arm64RegisterId.ARM64_REG_V0;

        ret = $"v{registerId - Arm64RegisterId.ARM64_REG_V0}";
        CachedArm64RegNamesNew[key] = ret;
        return ret;
    }

    private static void InitArm64Decompilation()
    {
        var disassembler = CapstoneDisassembler.CreateArm64Disassembler(LibCpp2IlMain.Binary!.IsBigEndian ? Arm64DisassembleMode.BigEndian : Arm64DisassembleMode.LittleEndian);
        disassembler.EnableInstructionDetails = true;
        disassembler.EnableSkipDataMode = true;
        disassembler.DisassembleSyntax = DisassembleSyntax.Intel;
        _arm64Disassembler = disassembler;
    }

    public static List<Arm64Instruction> GetArm64MethodBodyAtVirtualAddress(ulong virtAddress, bool managed = true, int count = -1)
    {
        if (_arm64Disassembler == null)
            InitArm64Decompilation();

        
        
        
        if (managed)
        {
            var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtAddress);

            
            if (startOfNext > 0)
            {
                var rawStartOfNextMethod = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(startOfNext);

                var rawStart = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(virtAddress);
                if (rawStartOfNextMethod < rawStart)
                    rawStartOfNextMethod = LibCpp2IlMain.Binary.RawLength;

                byte[] bytes = LibCpp2IlMain.Binary.GetRawBinaryContent().SubArray((int)rawStart..(int)rawStartOfNextMethod);

                var iter = _arm64Disassembler!.Iterate(bytes, (long)virtAddress);
                if (count > 0)
                    iter = iter.Take(count);

                return iter.ToList();
            }
        }

        
        var pos = (int)LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(virtAddress);
        var allBytes = LibCpp2IlMain.Binary.GetRawBinaryContent();
        List<Arm64Instruction> ret = [];

        while (!ret.Any(i => i.Mnemonic is "b" or ".byte") && (count == -1 || ret.Count < count))
        {
            
            ret.AddRange(_arm64Disassembler!.Iterate(allBytes.SubArray(pos..(pos + 4)), (long)virtAddress));
            virtAddress += 4;
            pos += 4;
        }

        return ret;
    }
}
