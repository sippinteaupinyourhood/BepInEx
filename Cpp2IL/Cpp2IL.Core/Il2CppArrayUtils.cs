using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL;

namespace Cpp2IL.Core;

public static class Il2CppArrayUtils
{
    public static uint FirstItemOffset = (uint)(LibCpp2IlMain.Binary!.is32Bit ? 0x10 : 0x20);
    
    
    
    
    
    

    public static readonly List<UsefulOffset> UsefulOffsets =
    [
        new UsefulOffset("length", 0xC, typeof(int), true),

        
        new UsefulOffset("length", 0x18, typeof(int), false)
    ];

    public static string? GetOffsetName(uint offset)
    {
        var is32Bit = LibCpp2IlMain.Binary!.is32Bit;

        return UsefulOffsets.FirstOrDefault(o => o.is32Bit == is32Bit && o.offset == offset)?.name;
    }

    public static bool IsIl2cppLengthAccessor(uint offset)
    {
        return GetOffsetName(offset) == "length";
    }

    public static bool IsAtLeastFirstItemPtr(uint offset)
    {
        return offset >= FirstItemOffset;
    }

    public class UsefulOffset(string name, uint offset, Type type, bool is32Bit)
    {
        public string name = name;
        public uint offset = offset;
        public Type type = type;
        public bool is32Bit = is32Bit;
    }
}
