using System.Collections.Generic;

namespace LibCpp2IL.Wasm;

public class WasmElementSegment
{
    public byte Flags;
    public ElementSegmentMode Mode;

    public ulong TableIdx;
    public ConstantExpression? Offset;
    public ulong Count;

    public byte ElemKind;
    public List<ulong>? FunctionIndices;

    public WasmTypeEnum ElemType;
    public List<ConstantExpression>? ConstantExpressions;

    public WasmElementSegment(WasmFile file)
    {
        Flags = file.ReadByte();
        if ((Flags & 3) == 2)
            
            TableIdx = file.BaseStream.ReadLEB128Unsigned();
        else
            
            TableIdx = 0;

        if ((Flags & 1) == 0)
        {
            
            Mode = ElementSegmentMode.Active;
            Offset = new(file);
        }
        else if ((Flags & 2) == 0)
            Mode = ElementSegmentMode.Passive;
        else
            Mode = ElementSegmentMode.Declarative;

        if ((Flags & 3) == 0)
        {
            
            ElemKind = 0;
            ElemType = WasmTypeEnum.funcRef;
        }
        else
        {
            
            var typeCode = file.ReadByte();
            if ((Flags & 4) == 0)
                ElemKind = typeCode;
            else
                ElemType = (WasmTypeEnum)typeCode;
        }

        Count = file.BaseStream.ReadLEB128Unsigned();

        if ((Flags & 4) == 0)
        {
            
            FunctionIndices = [];
            for (var i = 0UL; i < Count; i++)
            {
                FunctionIndices.Add(file.BaseStream.ReadLEB128Unsigned());
            }
        }
        else
        {
            
            ConstantExpressions = [];
            for (var i = 0UL; i < Count; i++)
            {
                ConstantExpressions.Add(new(file));
            }
        }
    }

    public enum ElementSegmentMode
    {
        Active,
        Passive,
        Declarative
    }
}
