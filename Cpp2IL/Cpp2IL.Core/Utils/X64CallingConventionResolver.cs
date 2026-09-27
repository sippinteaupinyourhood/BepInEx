using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.PE;

namespace Cpp2IL.Core.Utils;

#pragma warning disable IDE0305, IDE0300

public static class X64CallingConventionResolver
{
    

    
    
    

    const int ptrSize = 8;

    private static bool IsXMM(ParameterAnalysisContext par)
    {
        var parameterType = par.ParameterType;
        return parameterType == parameterType.AppContext.SystemTypes.SystemSingleType
            || parameterType == parameterType.AppContext.SystemTypes.SystemDoubleType;
    }

    public static InstructionSetIndependentOperand[] ResolveForUnmanaged(ApplicationAnalysisContext app, ulong target)
    {
        
        

        return app.Binary is PE ? new[] {
            ToOperand(MicrosoftNormalRegister.rcx),
            ToOperand(MicrosoftNormalRegister.rdx),
            ToOperand(MicrosoftNormalRegister.r8),
            ToOperand(MicrosoftNormalRegister.r9)
        } : new[] {
            ToOperand(LinuxNormalRegister.rdi),
            ToOperand(LinuxNormalRegister.rsi),
            ToOperand(LinuxNormalRegister.rdx),
            ToOperand(LinuxNormalRegister.rcx),
            ToOperand(LinuxNormalRegister.r8),
            ToOperand(LinuxNormalRegister.r9)
        };
    }

    public static InstructionSetIndependentOperand[] ResolveForManaged(MethodAnalysisContext ctx)
    {
        
        

        List<InstructionSetIndependentOperand> args = new();

        var addThis = !ctx.IsStatic;
        var isReturningAnOversizedStructure = false; 

        



















































































        



        if (ctx.AppContext.Binary is PE)
        {
            








            var i = 0;

            if (isReturningAnOversizedStructure)
            {
                args.Add(ToOperand(MicrosoftNormalRegister.rcx + i));
                i++;
            }

            if (addThis)
            {
                args.Add(ToOperand(MicrosoftNormalRegister.rcx + i));
                i++;
            }

            void AddParameter(ParameterAnalysisContext? par)
            {
                if (i < 4)
                {
                    args.Add((par != null && IsXMM(par)) ? ToOperand(LinuxFloatingRegister.xmm0 + i) : ToOperand(MicrosoftNormalRegister.rcx + i));
                }
                else
                {
                    args.Add(InstructionSetIndependentOperand.MakeStack((i - 4) * ptrSize));
                }
            }

            for (; i < ctx.Parameters.Count; i++)
            {
                AddParameter(ctx.Parameters[i]);
            }

            AddParameter(null); 
        }
        else 
        {
            





















            LinuxNormalRegister nreg = 0;
            LinuxFloatingRegister freg = 0;
            var stack = 0;

            void AddParameter(ParameterAnalysisContext? par)
            {
                if (par != null && IsXMM(par))
                {
                    if (freg == LinuxFloatingRegister.Stack)
                    {
                        args.Add(InstructionSetIndependentOperand.MakeStack(stack));
                        stack += ptrSize;
                    }
                    else args.Add(ToOperand(freg++));
                }
                else
                {
                    if (nreg == LinuxNormalRegister.Stack)
                    {
                        args.Add(InstructionSetIndependentOperand.MakeStack(stack));
                        stack += ptrSize;
                    }
                    else args.Add(ToOperand(nreg++));
                }
            }

            if (isReturningAnOversizedStructure)
            {
                args.Add(ToOperand(nreg++));
            }

            if (addThis)
            {
                args.Add(ToOperand(nreg++));
            }

            foreach (var par in ctx.Parameters)
            {
                AddParameter(par);
            }

            AddParameter(null); 
        }
        

        return args.ToArray();
    }

    private static InstructionSetIndependentOperand ToOperand(MicrosoftNormalRegister Reg) => Reg switch
    {
        MicrosoftNormalRegister.rcx => InstructionSetIndependentOperand.MakeRegister("rcx"),
        MicrosoftNormalRegister.rdx => InstructionSetIndependentOperand.MakeRegister("rdx"),
        MicrosoftNormalRegister.r8 => InstructionSetIndependentOperand.MakeRegister("r8"),
        MicrosoftNormalRegister.r9 => InstructionSetIndependentOperand.MakeRegister("r9"),
        _ => throw new InvalidOperationException("Went past the register limit during resolution.")
    };

    private static InstructionSetIndependentOperand ToOperand(LinuxNormalRegister Reg) => Reg switch
    {
        LinuxNormalRegister.rdi => InstructionSetIndependentOperand.MakeRegister("rdi"),
        LinuxNormalRegister.rsi => InstructionSetIndependentOperand.MakeRegister("rsi"),
        LinuxNormalRegister.rdx => InstructionSetIndependentOperand.MakeRegister("rdx"),
        LinuxNormalRegister.rcx => InstructionSetIndependentOperand.MakeRegister("rcx"),
        LinuxNormalRegister.r8 => InstructionSetIndependentOperand.MakeRegister("r8"),
        LinuxNormalRegister.r9 => InstructionSetIndependentOperand.MakeRegister("r9"),
        _ => throw new InvalidOperationException("Went past the register limit during resolution.")
    };

    private static InstructionSetIndependentOperand ToOperand(LinuxFloatingRegister Reg) => Reg switch
    {
        LinuxFloatingRegister.xmm0 => InstructionSetIndependentOperand.MakeRegister("xmm0"),
        LinuxFloatingRegister.xmm1 => InstructionSetIndependentOperand.MakeRegister("xmm1"),
        LinuxFloatingRegister.xmm2 => InstructionSetIndependentOperand.MakeRegister("xmm2"),
        LinuxFloatingRegister.xmm3 => InstructionSetIndependentOperand.MakeRegister("xmm3"),
        LinuxFloatingRegister.xmm4 => InstructionSetIndependentOperand.MakeRegister("xmm4"),
        LinuxFloatingRegister.xmm5 => InstructionSetIndependentOperand.MakeRegister("xmm5"),
        LinuxFloatingRegister.xmm6 => InstructionSetIndependentOperand.MakeRegister("xmm6"),
        LinuxFloatingRegister.xmm7 => InstructionSetIndependentOperand.MakeRegister("xmm7"),
        _ => throw new InvalidOperationException("Went past the register limit during resolution.")
    };

    private enum MicrosoftNormalRegister
    {
        rcx, rdx, r8, r9
    }

    private enum LinuxNormalRegister
    {
        rdi, rsi, rdx, rcx, r8, r9, Stack
    }

    private enum LinuxFloatingRegister
    {
        xmm0, xmm1, xmm2, xmm3, xmm4, xmm5, xmm6, xmm7, Stack
    }
}
