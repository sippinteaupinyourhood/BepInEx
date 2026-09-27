using System;
using System.Diagnostics;
using System.Text;

namespace Cpp2IL.Core.ISIL;






public readonly struct IsilMemoryOperand : IsilOperandData
{
    public readonly InstructionSetIndependentOperand? Base = null; 
    public readonly InstructionSetIndependentOperand? Index = null;
    public readonly long Addend = 0;
    public readonly int Scale = 0;

    
    
    
    
    public IsilMemoryOperand(long addend)
    {
        Addend = addend;
    }

    
    
    
    
    public IsilMemoryOperand(InstructionSetIndependentOperand @base)
    {
        Debug.Assert(@base.Type == InstructionSetIndependentOperand.OperandType.Register);

        Base = @base;
    }

    
    
    
    
    
    public IsilMemoryOperand(InstructionSetIndependentOperand @base, long addend)
    {
        Debug.Assert(@base.Type == InstructionSetIndependentOperand.OperandType.Register);

        Base = @base;
        Addend = addend;
    }

    
    
    
    
    
    
    public IsilMemoryOperand(InstructionSetIndependentOperand @base, InstructionSetIndependentOperand index, int scale)
    {
        Debug.Assert(@base.Type == InstructionSetIndependentOperand.OperandType.Register);
        Debug.Assert(index.Type == InstructionSetIndependentOperand.OperandType.Register);
        Debug.Assert(scale > 0);

        Base = @base;
        Index = index;
        Scale = scale;
    }

    
    
    
    
    
    
    
    public IsilMemoryOperand(InstructionSetIndependentOperand @base, InstructionSetIndependentOperand index, long addend, int scale)
    {
        Debug.Assert(@base.Type == InstructionSetIndependentOperand.OperandType.Register);
        Debug.Assert(index.Type == InstructionSetIndependentOperand.OperandType.Register);
        Debug.Assert(scale > 0);

        Base = @base;
        Index = index;
        Addend = addend;
        Scale = scale;
    }

    public override string ToString()
    {
        var ret = new StringBuilder("[");
        var needsPlus = false;

        if (Base != null)
        {
            ret.Append(Base);
            needsPlus = true;
        }

        if (Addend != 0)
        {
            if (needsPlus)
                ret.Append(Addend > 0 ? '+' : '-');

            if (Addend > 0x10000)
                ret.AppendFormat("0x{0:X}", Math.Abs(Addend));
            else
                ret.Append(Math.Abs(Addend));
            needsPlus = true;
        }

        if (Index != null)
        {
            if (needsPlus)
                ret.Append("+");
            ret.Append(Index);

            if (Scale > 1)
            {
                ret.Append("*");
                ret.Append(Scale);
            }
        }

        ret.Append(']');

        return ret.ToString();
    }
}
