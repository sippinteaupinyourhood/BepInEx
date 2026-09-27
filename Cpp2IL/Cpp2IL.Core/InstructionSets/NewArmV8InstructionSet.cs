using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Disarm;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Disarm.InternalDisassembly;
using LibCpp2IL;

namespace Cpp2IL.Core.InstructionSets;

public class NewArmV8InstructionSet : Cpp2IlInstructionSet
{
    [ThreadStatic]
    private static Dictionary<Arm64Register, ulong> adrpOffsets = new();
    
    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (context is not ConcreteGenericMethodAnalysisContext)
        {
            
            var startOfNextFunction = (int)MiscUtils.GetAddressOfNextFunctionStart(context.UnderlyingPointer);
            var ptrAsInt = (int)context.UnderlyingPointer;
            var count = startOfNextFunction - ptrAsInt;

            if (startOfNextFunction > 0)
                return LibCpp2IlMain.Binary!.GetRawBinaryContent().AsMemory(ptrAsInt, count);
        }

        var result = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);
        var lastInsn = result.LastValid();

        var start = (int)context.AppContext.Binary.MapVirtualAddressToRaw(context.UnderlyingPointer);
        
        
        var end = (int)context.AppContext.Binary.MapVirtualAddressToRaw(lastInsn.Address) + 4;

        
        if (start < 0 || end < 0 || start >= context.AppContext.Binary.RawLength || end >= context.AppContext.Binary.RawLength)
            throw new Exception($"Failed to map virtual address 0x{context.UnderlyingPointer:X} to raw address for method {context!.DeclaringType?.FullName}/{context.Name} - start: 0x{start:X}, end: 0x{end:X} are out of bounds for length {context.AppContext.Binary.RawLength}.");

        return context.AppContext.Binary.GetRawBinaryContent().AsMemory(start, end - start);
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var insns = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);

        var builder = new IsilBuilder();

        if (adrpOffsets == null!) 
            adrpOffsets = new();
        
        adrpOffsets.Clear();

        foreach (var instruction in insns)
        {
            ConvertInstructionStatement(instruction, builder, context);
        }
        
        adrpOffsets.Clear();

        builder.FixJumps();

        return builder.BackingStatementList;
    }

    private void ConvertInstructionStatement(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.MOV:
            case Arm64Mnemonic.MOVZ:
            case Arm64Mnemonic.FMOV:
            case Arm64Mnemonic.SXTW: 
            case Arm64Mnemonic.LDR:
            case Arm64Mnemonic.LDRB:
                
                
                if (instruction.MemIsPreIndexed) 
                {
                    
                    if(instruction.Op0Kind == Arm64OperandKind.Register)
                        adrpOffsets.Remove(instruction.Op0Reg);
                    
                    var operate = ConvertOperand(instruction, 1);
                    if (operate.Data is IsilMemoryOperand operand)
                    {
                        var register = operand.Base!.Value;
                        
                        builder.Add(instruction.Address, register, register, InstructionSetIndependentOperand.MakeImmediate(operand.Addend));
                        
                        builder.Move(instruction.Address, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(
                            InstructionSetIndependentOperand.MakeRegister(register.ToString()!.ToUpperInvariant()),
                            0)));
                        break;
                    }
                }

                if (instruction.Op1Kind == Arm64OperandKind.Memory && adrpOffsets.TryGetValue(instruction.MemBase, out var page) && instruction.MemOffset != 0 && instruction.MemAddendReg == Arm64Register.INVALID)
                {
                    
                    
                    
                    
                    var offset = instruction.MemOffset + (long) page;
                    
                    
                    if(instruction.Op0Kind == Arm64OperandKind.Register)
                        adrpOffsets.Remove(instruction.Op0Reg);
                    
                    builder.Move(instruction.Address, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeMemory(new(offset)));
                    break;
                }

                
                if(instruction.Op0Kind == Arm64OperandKind.Register)
                    adrpOffsets.Remove(instruction.Op0Reg);
                
                builder.Move(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Arm64Mnemonic.MOVN:
            {
                
                
                
                if(instruction.Op0Kind == Arm64OperandKind.Register)
                    adrpOffsets.Remove(instruction.Op0Reg);
                
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.Move(instruction.Address, temp, ConvertOperand(instruction, 1));
                builder.Not(instruction.Address, temp);
                builder.Move(instruction.Address, ConvertOperand(instruction, 0), temp);
                break;
            }
            case Arm64Mnemonic.STR:
            case Arm64Mnemonic.STUR: 
            case Arm64Mnemonic.STRB:
                
                builder.Move(instruction.Address, ConvertOperand(instruction, 1), ConvertOperand(instruction, 0));
                break;
            case Arm64Mnemonic.STP:
                
            {
                var dest = ConvertOperand(instruction, 2);
                if (dest.Data is IsilRegisterOperand { RegisterName: "X31" }) 
                {
                    builder.Move(instruction.Address, dest, ConvertOperand(instruction, 0));
                    builder.Move(instruction.Address, dest, ConvertOperand(instruction, 1));
                }
                else if (dest.Data is IsilMemoryOperand memory)
                {
                    var firstRegister = ConvertOperand(instruction, 0);
                    long size = ((IsilRegisterOperand)firstRegister.Data).RegisterName[0] == 'W' ? 4 : 8;
                    builder.Move(instruction.Address, dest, firstRegister); 
                    memory = new IsilMemoryOperand(memory.Base!.Value, memory.Addend + size);
                    dest = InstructionSetIndependentOperand.MakeMemory(memory);
                    builder.Move(instruction.Address, dest, ConvertOperand(instruction, 1)); 
                }
                else 
                {
                    var firstRegister = ConvertOperand(instruction, 0);
                    long size = ((IsilRegisterOperand)firstRegister.Data).RegisterName[0] == 'W' ? 4 : 8;
                    builder.Move(instruction.Address, dest, firstRegister);
                    builder.Add(instruction.Address, dest, dest, InstructionSetIndependentOperand.MakeImmediate(size));
                    builder.Move(instruction.Address, dest, ConvertOperand(instruction, 1));
                }
            }
                break;
            case Arm64Mnemonic.ADRP:
                
                
                adrpOffsets[instruction.Op0Reg] = (ulong)instruction.Op1Imm;
                break;
            case Arm64Mnemonic.LDP when instruction.Op2Kind == Arm64OperandKind.Memory:
                
                var destRegSize = instruction.Op0Reg switch
                {
                    
                    >= Arm64Register.V0 and <= Arm64Register.V31 => 16, 
                    
                    >= Arm64Register.D0 and <= Arm64Register.D31 => 8,
                    
                    >= Arm64Register.S0 and <= Arm64Register.S31 => 4,
                    
                    >= Arm64Register.H0 and <= Arm64Register.H31 => 2,
                    
                    >= Arm64Register.W0 and <= Arm64Register.W31 => 4,
                    
                    >= Arm64Register.X0 and <= Arm64Register.X31 => 8,
                    _ => throw new($"Unknown register size for LDP: {instruction.Op0Reg}")
                };

                var dest1 = ConvertOperand(instruction, 0);
                var dest2 = ConvertOperand(instruction, 1);
                var mem = ConvertOperand(instruction, 2);

                
                var memInternal = mem.Data as IsilMemoryOperand?;
                var mem2 = new IsilMemoryOperand(memInternal!.Value.Base!.Value, memInternal.Value.Addend + destRegSize);

                builder.Move(instruction.Address, dest1, mem);
                builder.Move(instruction.Address, dest2, InstructionSetIndependentOperand.MakeMemory(mem2));
                break;
            case Arm64Mnemonic.BL:
                builder.Call(instruction.Address, instruction.BranchTarget, GetArgumentOperandsForCall(context, instruction.BranchTarget).ToArray());
                break;
            case Arm64Mnemonic.RET:
                builder.Return(instruction.Address, GetReturnRegisterForContext(context));
                break;
            case Arm64Mnemonic.B:
                var target = instruction.BranchTarget;

                if (target < context.UnderlyingPointer || target > context.UnderlyingPointer + (ulong)context.RawBytes.Length)
                {
                    
                    builder.Call(instruction.Address, instruction.BranchTarget, GetArgumentOperandsForCall(context, instruction.BranchTarget).ToArray());
                    builder.Return(instruction.Address, GetReturnRegisterForContext(context));
                }

                break;
            case Arm64Mnemonic.BR:
                
                builder.CallRegister(instruction.Address, ConvertOperand(instruction, 0), noReturn: true);
                break;
            case Arm64Mnemonic.CBNZ:
            case Arm64Mnemonic.CBZ:
            {
                
                var targetAddr = (ulong)((long)instruction.Address + instruction.Op1Imm);

                
                builder.Compare(instruction.Address, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(0));

                
                if (instruction.Mnemonic == Arm64Mnemonic.CBZ)
                    builder.JumpIfEqual(instruction.Address, targetAddr);
                else
                    builder.JumpIfNotEqual(instruction.Address, targetAddr);
            }
                break;

            case Arm64Mnemonic.CMP:
                
                
                goto default;

            case Arm64Mnemonic.TBNZ:
            
            
            case Arm64Mnemonic.TBZ:
                
                
            {
                var targetAddr = (ulong)((long)instruction.Address + instruction.Op2Imm);
                var bit = InstructionSetIndependentOperand.MakeImmediate(1 << (int)instruction.Op1Imm);
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                var src = ConvertOperand(instruction, 0);
                builder.Move(instruction.Address, temp, src); 
                builder.And(instruction.Address, temp, temp, bit); 
                builder.Compare(instruction.Address, temp, bit); 
                if (instruction.Mnemonic == Arm64Mnemonic.TBNZ)
                    builder.JumpIfEqual(instruction.Address, targetAddr); 
                else
                    builder.JumpIfNotEqual(instruction.Address, targetAddr); 
            }
                break;
            case Arm64Mnemonic.UBFM:
                
                
            {
                var dest = ConvertOperand(instruction, 0);
                builder.Move(instruction.Address, dest, ConvertOperand(instruction, 1)); 
                builder.ShiftRight(instruction.Address, dest, ConvertOperand(instruction, 2)); 
                var imms = (int)instruction.Op3Imm;
                builder.And(instruction.Address, dest, dest,
                    InstructionSetIndependentOperand.MakeImmediate((1 << imms) - 1)); 
            }
                break;

            case Arm64Mnemonic.MUL:
            case Arm64Mnemonic.FMUL:
                
                builder.Multiply(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.ADD:
            case Arm64Mnemonic.ADDS: 
            case Arm64Mnemonic.FADD:
                
                builder.Add(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.SUB:
            case Arm64Mnemonic.SUBS: 
            case Arm64Mnemonic.FSUB:
                
                builder.Subtract(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.AND:
            case Arm64Mnemonic.ANDS:
                
                builder.And(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.ORR:
                
                builder.Or(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.EOR:
                
                builder.Xor(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            default:
                builder.NotImplemented(instruction.Address, $"Instruction {instruction.Mnemonic} not yet implemented.");
                break;
        }
    }

    private InstructionSetIndependentOperand ConvertOperand(Arm64Instruction instruction, int operand)
    {
        var kind = operand switch
        {
            0 => instruction.Op0Kind,
            1 => instruction.Op1Kind,
            2 => instruction.Op2Kind,
            3 => instruction.Op3Kind,
            _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
        };

        if (kind is Arm64OperandKind.Immediate or Arm64OperandKind.ImmediatePcRelative)
        {
            var imm = operand switch
            {
                0 => instruction.Op0Imm,
                1 => instruction.Op1Imm,
                2 => instruction.Op2Imm,
                3 => instruction.Op3Imm,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            if (kind == Arm64OperandKind.ImmediatePcRelative)
                imm += (long)instruction.Address + 4; 

            return InstructionSetIndependentOperand.MakeImmediate(imm);
        }

        if (kind == Arm64OperandKind.FloatingPointImmediate)
        {
            var imm = operand switch
            {
                0 => instruction.Op0FpImm,
                1 => instruction.Op1FpImm,
                2 => instruction.Op2FpImm,
                3 => instruction.Op3FpImm,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            return InstructionSetIndependentOperand.MakeImmediate(imm);
        }

        if (kind == Arm64OperandKind.Register)
        {
            var reg = operand switch
            {
                0 => instruction.Op0Reg,
                1 => instruction.Op1Reg,
                2 => instruction.Op2Reg,
                3 => instruction.Op3Reg,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            return InstructionSetIndependentOperand.MakeRegister(reg.ToString().ToUpperInvariant());
        }

        if (kind == Arm64OperandKind.Memory)
        {
            var reg = instruction.MemBase;
            var offset = instruction.MemOffset;
            var isPreIndexed = instruction.MemIsPreIndexed;

            if (reg == Arm64Register.INVALID)
                
                return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(offset));

            
            return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(
                InstructionSetIndependentOperand.MakeRegister(reg.ToString().ToUpperInvariant()),
                offset));
        }

        if (kind == Arm64OperandKind.VectorRegisterElement)
        {
            var reg = operand switch
            {
                0 => instruction.Op0Reg,
                1 => instruction.Op1Reg,
                2 => instruction.Op2Reg,
                3 => instruction.Op3Reg,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            var vectorElement = operand switch
            {
                0 => instruction.Op0VectorElement,
                1 => instruction.Op1VectorElement,
                2 => instruction.Op2VectorElement,
                3 => instruction.Op3VectorElement,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            var width = vectorElement.Width switch
            {
                Arm64VectorElementWidth.B => IsilVectorRegisterElementOperand.VectorElementWidth.B,
                Arm64VectorElementWidth.H => IsilVectorRegisterElementOperand.VectorElementWidth.H,
                Arm64VectorElementWidth.S => IsilVectorRegisterElementOperand.VectorElementWidth.S,
                Arm64VectorElementWidth.D => IsilVectorRegisterElementOperand.VectorElementWidth.D,
                _ => throw new ArgumentOutOfRangeException(nameof(vectorElement.Width), $"Unknown vector element width {vectorElement.Width}")
            };

            
            return InstructionSetIndependentOperand.MakeVectorElement(reg.ToString().ToUpperInvariant(), width, vectorElement.Index);
        }

        return InstructionSetIndependentOperand.MakeImmediate($"<UNIMPLEMENTED OPERAND TYPE {kind}>");
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new NewArm64KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context) => context.RawBytes.Span.Length <= 0 ? "" : string.Join("\n", Disassembler.Disassemble(context.RawBytes.Span, context.UnderlyingPointer, new Disassembler.Options(true, true, false)).ToList());

    private InstructionSetIndependentOperand? GetReturnRegisterForContext(MethodAnalysisContext context)
    {
        var returnType = context.ReturnType;
        if (returnType.Namespace == nameof(System))
        {
            return returnType.Name switch
            {
                "Void" => null, 
                "Double" => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.V0)), 
                "Single" => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.V0)), 
                _ => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0)), 
            };
        }

        

        
        return InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0));
    }

    private List<InstructionSetIndependentOperand> GetArgumentOperandsForCall(MethodAnalysisContext contextBeingAnalyzed, ulong callAddr)
    {
        if (!contextBeingAnalyzed.AppContext.MethodsByAddress.TryGetValue(callAddr, out var methodsAtAddress))
            
            return [];

        
        var contextBeingCalled = methodsAtAddress.First();

        var vectorCount = 0;
        var nonVectorCount = 0;

        var ret = new List<InstructionSetIndependentOperand>();

        
        if (!contextBeingCalled.IsStatic)
        {
            ret.Add(InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0)));
            nonVectorCount++;
        }

        foreach (var parameter in contextBeingCalled.Parameters)
        {
            var paramType = parameter.ParameterType;
            if (paramType.Namespace == nameof(System))
            {
                switch (paramType.Name)
                {
                    case "Single":
                    case "Double":
                        ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.V0 + vectorCount++).ToString().ToUpperInvariant()));
                        break;
                    default:
                        ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.X0 + nonVectorCount++).ToString().ToUpperInvariant()));
                        break;
                }
            }
            else
            {
                ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.X0 + nonVectorCount++).ToString().ToUpperInvariant()));
            }
        }

        return ret;
    }
}
