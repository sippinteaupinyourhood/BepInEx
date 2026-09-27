



namespace WasmDisassembler;





public static class LEB128
{
    private const long SIGN_EXTEND_MASK = -1L;
    private const int INT64_BITSIZE = (sizeof(long) * 8);

    public static void WriteLEB128Signed(this Stream stream, long value) => WriteLEB128Signed(stream, value, out _);

    public static void WriteLEB128Signed(this Stream stream, long value, out int bytes)
    {
        bytes = 0;
        bool more = true;

        while (more)
        {
            byte chunk = (byte)(value & 0x7fL); 
            value >>= 7;

            bool signBitSet = (chunk & 0x40) != 0; 
            more = !((value == 0 && !signBitSet) || (value == -1 && signBitSet));
            if (more) { chunk |= 0x80; } 

            stream.WriteByte(chunk);
            bytes += 1;
        }

        ;
    }

    public static void WriteLEB128Unsigned(this Stream stream, ulong value) => WriteLEB128Unsigned(stream, value, out _);

    public static void WriteLEB128Unsigned(this Stream stream, ulong value, out int bytes)
    {
        bytes = 0;
        bool more = true;

        while (more)
        {
            byte chunk = (byte)(value & 0x7fUL); 
            value >>= 7;

            more = value != 0;
            if (more) { chunk |= 0x80; } 

            stream.WriteByte(chunk);
            bytes += 1;
        }

        ;
    }

    public static long ReadLEB128Signed(this Stream stream) => ReadLEB128Signed(stream, out _);

    public static long ReadLEB128Signed(this Stream stream, out int bytes)
    {
        bytes = 0;

        long value = 0;
        int shift = 0;
        bool more = true, signBitSet = false;

        while (more)
        {
            var next = stream.ReadByte();
            if (next < 0) { throw new InvalidOperationException("Unexpected end of stream"); }

            byte b = (byte)next;
            bytes += 1;

            more = (b & 0x80) != 0; 
            signBitSet = (b & 0x40) != 0; 

            long chunk = b & 0x7fL; 
            value |= chunk << shift;
            shift += 7;
        }

        ;

        
        if (shift < INT64_BITSIZE && signBitSet) { value |= SIGN_EXTEND_MASK << shift; }

        return value;
    }

    public static ulong ReadLEB128Unsigned(this Stream stream) => ReadLEB128Unsigned(stream, out _);

    public static ulong ReadLEB128Unsigned(this Stream stream, out int bytes)
    {
        bytes = 0;

        ulong value = 0;
        int shift = 0;
        bool more = true;

        while (more)
        {
            var next = stream.ReadByte();
            if (next < 0) { throw new InvalidOperationException("Unexpected end of stream"); }

            byte b = (byte)next;
            bytes += 1;

            more = (b & 0x80) != 0; 
            ulong chunk = b & 0x7fUL; 
            value |= chunk << shift;
            shift += 7;
        }

        return value;
    }
}
