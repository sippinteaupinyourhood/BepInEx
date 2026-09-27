using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.InstructionSets;


public class X86InstructionSet : Cpp2IlInstructionSet
{
    private static readonly MasmFormatter Formatter = new();
    private static readonly StringOutput Output = new();

    private static string FormatInstructionInternal(Instruction instruction)
    {
        Formatter.Format(instruction, Output);
        return Output.ToStringAndReset();
    }

    public static string FormatInstruction(Instruction instruction)
    {
        lock (Formatter)
        {
            return FormatInstructionInternal(instruction);
        }
    }

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator) => X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, isAttributeGenerator);

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new X86KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        lock (Formatter)
        {
            var insns = X86Utils.Iterate(X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, false), context.UnderlyingPointer);

            return string.Join("\n", insns.Select(FormatInstructionInternal));
        }
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var builder = new IsilBuilder();

        foreach (var instruction in X86Utils.Iterate(context))
        {
            ConvertInstructionStatement(instruction, builder, context);
        }

        builder.FixJumps();

        return builder.BackingStatementList;
    }


    private void ConvertInstructionStatement(Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        var callNoReturn = false;
        int operandSize;

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Mov:
            case Mnemonic.Movzx: 
            case Mnemonic.Movsx: 
            case Mnemonic.Movsxd: 
            case Mnemonic.Movaps: 
            case Mnemonic.Movups: 
            case Mnemonic.Movss: 
            case Mnemonic.Movd: 
            case Mnemonic.Movq: 
            case Mnemonic.Movsd: 
            case Mnemonic.Movdqa: 
            case Mnemonic.Cvtdq2ps: 
            case Mnemonic.Cvtps2pd: 
            case Mnemonic.Cvttsd2si: 
            case Mnemonic.Movdqu: 
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Cbw: 
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.AX)), 
                    InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.AL)));
                break;
            case Mnemonic.Cwde: 
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EAX)), 
                    InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.AX)));
                break;
            case Mnemonic.Cdqe: 
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.RAX)), 
                    InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EAX)));
                break;
            
            case Mnemonic.Cwd: 
            {
                
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.Move(instruction.IP, temp, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.AX))); 
                builder.ShiftRight(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(15)); 
                builder.Compare(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(1)); 
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1);
                
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.DX)), InstructionSetIndependentOperand.MakeImmediate(ushort.MaxValue));
                builder.Goto(instruction.IP, instruction.IP + 2);
                builder.Move(instruction.IP + 1, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.DX)), InstructionSetIndependentOperand.MakeImmediate(0));
                builder.Nop(instruction.IP + 2);
                break;
            }
            case Mnemonic.Cdq: 
            {
                
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.Move(instruction.IP, temp, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EAX))); 
                builder.ShiftRight(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(31)); 
                builder.Compare(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(1)); 
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1);
                
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EDX)), InstructionSetIndependentOperand.MakeImmediate(uint.MaxValue));
                builder.Goto(instruction.IP, instruction.IP + 2);
                builder.Move(instruction.IP + 1, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EDX)), InstructionSetIndependentOperand.MakeImmediate(0));
                builder.Nop(instruction.IP + 2);
                break;
            }
            case Mnemonic.Cqo: 
            {
                
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.Move(instruction.IP, temp, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.RAX))); 
                builder.ShiftRight(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(63)); 
                builder.Compare(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(1)); 
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1);
                
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.RDX)), InstructionSetIndependentOperand.MakeImmediate(ulong.MaxValue));
                builder.Goto(instruction.IP, instruction.IP + 2);
                builder.Move(instruction.IP + 1, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.RDX)), InstructionSetIndependentOperand.MakeImmediate(0));
                builder.Nop(instruction.IP + 2);
                break;
            }
            case Mnemonic.Lea:
                builder.LoadAddress(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Xor:
            case Mnemonic.Xorps: 
                if (instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
                    builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(0));
                else
                    builder.Xor(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Shl: 
            case Mnemonic.Sal: 
                builder.ShiftLeft(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Shr: 
            case Mnemonic.Sar: 
                builder.ShiftRight(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.And:
            case Mnemonic.Andps: 
                builder.And(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Or:
            case Mnemonic.Orps: 
                builder.Or(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Not:
                builder.Neg(instruction.IP, ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Neg: 
                builder.Neg(instruction.IP, ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Imul:
                if (instruction.OpCount == 1)
                {
                    int opSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                    switch (opSize) 
                    {
                        case 1: 
                            builder.Multiply(instruction.IP, Register.AX.MakeIndependent(), ConvertOperand(instruction, 0), Register.AL.MakeIndependent());
                            return;
                        case 2: 

                            break;
                        case 4: 

                            break;
                        case 8: 

                            break;
                        default: 

                            break;
                    }

                    
                    goto default;
                }
                else if (instruction.OpCount == 3) builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                else builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));

                break;
            case Mnemonic.Mulss:
            case Mnemonic.Vmulss:
                if (instruction.OpCount == 3)
                    builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                else if (instruction.OpCount == 2)
                    builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                else
                    goto default;

                break;
            
            case Mnemonic.Divss: 
                builder.Divide(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Vdivss: 
                builder.Divide(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;
            
            case Mnemonic.Ret:
                

                
                
                

                if (context.IsVoid)
                    builder.Return(instruction.IP);
                else if (context.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                    builder.Return(instruction.IP, InstructionSetIndependentOperand.MakeRegister("xmm0"));
                else
                    builder.Return(instruction.IP, InstructionSetIndependentOperand.MakeRegister("rax"));
                break;
            case Mnemonic.Push:
                operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                builder.ShiftStack(instruction.IP, -operandSize);
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeStack(0), ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Pop:
                operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeStack(0));
                builder.ShiftStack(instruction.IP, operandSize);
                break;
            case Mnemonic.Sub:
            case Mnemonic.Add:
                var isSubtract = instruction.Mnemonic == Mnemonic.Sub;

                
                if (instruction.Op0Register == Register.RSP && instruction.Op1Kind.IsImmediate())
                {
                    var amount = (int)instruction.GetImmediate(1);
                    builder.ShiftStack(instruction.IP, isSubtract ? -amount : amount);
                    break;
                }

                var left = ConvertOperand(instruction, 0);
                var right = ConvertOperand(instruction, 1);
                if (isSubtract)
                    builder.Subtract(instruction.IP, left, left, right);
                else
                    builder.Add(instruction.IP, left, left, right);

                break;
            case Mnemonic.Addss:
            case Mnemonic.Subss:
            {
                
                
                InstructionSetIndependentOperand dest;
                InstructionSetIndependentOperand src1;
                InstructionSetIndependentOperand src2;

                if (instruction.OpCount == 3)
                {
                    
                    dest = ConvertOperand(instruction, 0);
                    src1 = ConvertOperand(instruction, 1);
                    src2 = ConvertOperand(instruction, 2);
                }
                else if (instruction.OpCount == 2)
                {
                    
                    dest = ConvertOperand(instruction, 0);
                    src1 = dest;
                    src2 = ConvertOperand(instruction, 1);
                }
                else
                    goto default;

                if (instruction.Mnemonic == Mnemonic.Subss)
                    builder.Subtract(instruction.IP, dest, src1, src2);
                else
                    builder.Add(instruction.IP, dest, src1, src2);
                break;
            }
            
            case Mnemonic.Dec:
                builder.Subtract(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;
            case Mnemonic.Inc:
                builder.Add(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;

            case Mnemonic.Shufps: 
            {
                if (instruction.Op1Kind == OpKind.Memory)
                    goto default;
                
                var imm = instruction.Immediate8;
                var src1 = X86Utils.GetRegisterName(instruction.Op0Register);
                var src2 = X86Utils.GetRegisterName(instruction.Op1Register);
                var dest = "XMM_TEMP";
                
                builder.Move(instruction.IP, ConvertVector(dest, 0), ConvertVector(src1, imm & 0b11)); 
                
                builder.Move(instruction.IP, ConvertVector(dest, 1), ConvertVector(src1, (imm >> 2) & 0b11)); 
                
                builder.Move(instruction.IP, ConvertVector(dest, 2), ConvertVector(src2, (imm >> 4) & 0b11));
                
                builder.Move(instruction.IP, ConvertVector(dest, 3), ConvertVector(src2, (imm >> 6) & 0b11));
                
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister(dest)); 
                break;

                static InstructionSetIndependentOperand ConvertVector(string reg, int imm) =>
                    InstructionSetIndependentOperand.MakeVectorElement(reg, IsilVectorRegisterElementOperand.VectorElementWidth.S, imm);
            }
                
            case Mnemonic.Unpcklps : 
            {
                if (instruction.Op1Kind == OpKind.Memory)
                    goto default;
                
                var src1 = X86Utils.GetRegisterName(instruction.Op0Register);
                var src2 = X86Utils.GetRegisterName(instruction.Op1Register);
                var dest = "XMM_TEMP";
                builder.Move(instruction.IP, ConvertVector(dest, 0), ConvertVector(src1, 0)); 
                builder.Move(instruction.IP, ConvertVector(dest, 1), ConvertVector(src2, 0)); 
                builder.Move(instruction.IP, ConvertVector(dest, 2), ConvertVector(src1, 1)); 
                builder.Move(instruction.IP, ConvertVector(dest, 3), ConvertVector(src2, 1)); 
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister(dest)); 
                break;

                static InstructionSetIndependentOperand ConvertVector(string reg, int imm) =>
                    InstructionSetIndependentOperand.MakeVectorElement(reg, IsilVectorRegisterElementOperand.VectorElementWidth.S, imm);
            }
            
            case Mnemonic.Call:
                
                

                var target = instruction.NearBranchTarget;

                if (instruction.Op0Kind == OpKind.Register)
                {
                    builder.CallRegister(instruction.IP, ConvertOperand(instruction, 0));
                }
                else if (context.AppContext.MethodsByAddress.TryGetValue(target, out var possibleMethods))
                {
                    if (possibleMethods.Count == 1)
                    {
                        builder.Call(instruction.IP, target, X64CallingConventionResolver.ResolveForManaged(possibleMethods[0]));
                    }
                    else
                    {
                        MethodAnalysisContext ctx = null!;
                        var lpars = -1;

                        
                        foreach (var method in possibleMethods)
                        {
                            var pars = method.Parameters.Count;
                            if (method.IsStatic) pars++;
                            if (pars > lpars)
                            {
                                lpars = pars;
                                ctx = method;
                            }
                        }

                        
                        

                        builder.Call(instruction.IP, target, X64CallingConventionResolver.ResolveForManaged(ctx));
                    }
                }
                else
                {
                    
                    
                    

                    builder.Call(instruction.IP, target, X64CallingConventionResolver.ResolveForUnmanaged(context.AppContext, target));
                }

                if (callNoReturn)
                {
                    
                    
                    
                    

                    
                    
                    

                    goto case Mnemonic.Ret;
                }

                break;
            case Mnemonic.Test:
                if (instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
                {
                    builder.Compare(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(0));
                    break;
                }

                
                goto case Mnemonic.Cmp;
            case Mnemonic.Cmp:
            case Mnemonic.Comiss: 
            case Mnemonic.Ucomiss: 
                builder.Compare(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            
            case Mnemonic.Cmove: 
            case Mnemonic.Cmovne: 
            case Mnemonic.Cmova:
            case Mnemonic.Cmovg:
            case Mnemonic.Cmovae:
            case Mnemonic.Cmovge:
            case Mnemonic.Cmovb:
            case Mnemonic.Cmovl:
            case Mnemonic.Cmovbe:
            case Mnemonic.Cmovle: 
            case Mnemonic.Cmovs: 
            case Mnemonic.Cmovns: 
                switch (instruction.Mnemonic)
                {
                    case Mnemonic.Cmove: 
                        builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1); 
                        break;
                    case Mnemonic.Cmovne: 
                        builder.JumpIfEqual(instruction.IP, instruction.IP + 1); 
                        break;
                    case Mnemonic.Cmovs: 
                        builder.JumpIfNotSign(instruction.IP, instruction.IP + 1); 
                        break;
                    case Mnemonic.Cmovns: 
                        builder.JumpIfSign(instruction.IP, instruction.IP + 1); 
                        break;
                    case Mnemonic.Cmova:
                    case Mnemonic.Cmovg: 
                        builder.JumpIfLessOrEqual(instruction.IP, instruction.IP + 1); 
                        break;
                    case Mnemonic.Cmovae:
                    case Mnemonic.Cmovge: 
                        builder.JumpIfLess(instruction.IP, instruction.IP + 1); 
                        break;
                    case Mnemonic.Cmovb:
                    case Mnemonic.Cmovl: 
                        builder.JumpIfGreaterOrEqual(instruction.IP, instruction.IP + 1); 
                        break;
                    case Mnemonic.Cmovbe:
                    case Mnemonic.Cmovle: 
                        builder.JumpIfGreater(instruction.IP, instruction.IP + 1); 
                        break;
                }
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)); 
                builder.Nop(instruction.IP + 1);
                break;

            case Mnemonic.Maxss: 
            case Mnemonic.Minss: 
            {
                var dest = ConvertOperand(instruction, 0);
                var src = ConvertOperand(instruction, 1);
                builder.Compare(instruction.IP, dest, src); 
                if (instruction.Mnemonic == Mnemonic.Maxss)
                    builder.JumpIfGreaterOrEqual(instruction.IP, instruction.IP + 1); 
                else
                    builder.JumpIfLessOrEqual(instruction.IP, instruction.IP + 1); 
                builder.Move(instruction.IP, dest, src); 
                builder.Nop(instruction.IP + 1); 
                break;
            }
            
            case Mnemonic.Cmpxchg: 
            {
                var accumulator = InstructionSetIndependentOperand.MakeRegister(instruction.Op1Register.GetSize() switch
                {
                    8 => X86Utils.GetRegisterName(Register.RAX),
                    4 => X86Utils.GetRegisterName(Register.EAX),
                    2 => X86Utils.GetRegisterName(Register.AX),
                    1 => X86Utils.GetRegisterName(Register.AL),
                    _ => throw new NotSupportedException("unexpected behavior")
                });
                var dest = ConvertOperand(instruction, 0);
                var src = ConvertOperand(instruction, 1);
                builder.Compare(instruction.IP, accumulator, dest);
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1); 
                
                builder.Move(instruction.IP, dest, src); 
                builder.Goto(instruction.IP, instruction.IP + 2); 
                
                
                builder.Move(instruction.IP + 1, accumulator, dest); 
                
                builder.Nop(instruction.IP + 2); 
                break;
            }
            
            case Mnemonic.Jmp:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    var methodEnd = instruction.IP + (ulong)context.RawBytes.Length;
                    var methodStart = context.UnderlyingPointer;

                    if (jumpTarget < methodStart || jumpTarget > methodEnd)
                    {
                        callNoReturn = true;
                        goto case Mnemonic.Call;
                    }
                    else
                    {
                        builder.Goto(instruction.IP, jumpTarget);
                        break;
                    }
                }
                if (instruction.Op0Kind == OpKind.Register) 
                {
                    builder.CallRegister(instruction.IP, ConvertOperand(instruction, 0), noReturn: true);
                    break;
                }

                goto default;
            case Mnemonic.Je:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfEqual(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jne:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfNotEqual(instruction.IP, jumpTarget);
                    break;
                }
                goto default;
            case Mnemonic.Js:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfSign(instruction.IP, jumpTarget);
                    break;
                }
                
                goto default;
            case Mnemonic.Jns:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfNotSign(instruction.IP, jumpTarget);
                    break;
                }
                
                goto default;
            case Mnemonic.Jg:
            case Mnemonic.Ja:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfGreater(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jl:
            case Mnemonic.Jb:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfLess(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jge:
            case Mnemonic.Jae:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfGreaterOrEqual(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jle:
            case Mnemonic.Jbe:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfLessOrEqual(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Xchg:
                
                builder.Exchange(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Int:
            case Mnemonic.Int3:
                builder.Interrupt(instruction.IP); 
                break;
            case Mnemonic.Prefetchw: 
            case Mnemonic.Nop:
                
                
                builder.Nop(instruction.IP);
                break;
            default:
                builder.NotImplemented(instruction.IP, FormatInstruction(instruction));
                break;
        }
    }


    private InstructionSetIndependentOperand ConvertOperand(Instruction instruction, int operand)
    {
        var kind = instruction.GetOpKind(operand);

        if (kind == OpKind.Register)
            return InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.GetOpRegister(operand)));
        if (kind.IsImmediate())
            return InstructionSetIndependentOperand.MakeImmediate(instruction.GetImmediate(operand));
        if (kind == OpKind.Memory && instruction.MemoryBase == Register.RSP)
            return InstructionSetIndependentOperand.MakeStack((int)instruction.MemoryDisplacement32);

        
        

        if (instruction.IsIPRelativeMemoryOperand)
            return InstructionSetIndependentOperand.MakeMemory(new((long)instruction.IPRelativeMemoryAddress));

        
        if (instruction.MemoryIndex != Register.None && instruction.MemoryBase != Register.None && instruction.MemoryDisplacement64 != 0)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            var mIndex = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryIndex));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, mIndex, instruction.MemoryDisplacement32, instruction.MemoryIndexScale));
        }

        
        if (instruction.MemoryIndex != Register.None && instruction.MemoryBase != Register.None)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            var mIndex = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryIndex));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, mIndex, instruction.MemoryIndexScale));
        }

        
        if (instruction.MemoryBase != Register.None && instruction.MemoryDisplacement64 > 0)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, (long)instruction.MemoryDisplacement64));
        }

        
        if (instruction.MemoryBase != Register.None)
        {
            return InstructionSetIndependentOperand.MakeMemory(new(InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase))));
        }

        
        return InstructionSetIndependentOperand.MakeMemory(new((long)instruction.MemoryDisplacement64));
    }
}
