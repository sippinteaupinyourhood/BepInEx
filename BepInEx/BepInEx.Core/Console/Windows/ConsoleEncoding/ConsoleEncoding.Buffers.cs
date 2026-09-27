




namespace UnityInjector.ConsoleUtil;








internal partial class ConsoleEncoding
{
    private readonly byte[] _zeroByte = new byte[0];
    private readonly char[] _zeroChar = new char[0];
    private byte[] _byteBuffer = new byte[256];
    private char[] _charBuffer = new char[256];

    private void ExpandByteBuffer(int count)
    {
        if (_byteBuffer.Length < count)
            _byteBuffer = new byte[count];
    }

    private void ExpandCharBuffer(int count)
    {
        if (_charBuffer.Length < count)
            _charBuffer = new char[count];
    }

    private void ReadByteBuffer(byte[] bytes, int index, int count)
    {
        for (var i = 0; i < count; i++)
            bytes[index + i] = _byteBuffer[i];
    }

    private void ReadCharBuffer(char[] chars, int index, int count)
    {
        for (var i = 0; i < count; i++)
            chars[index + i] = _charBuffer[i];
    }

    private void WriteByteBuffer(byte[] bytes, int index, int count)
    {
        ExpandByteBuffer(count);
        for (var i = 0; i < count; i++)
            _byteBuffer[i] = bytes[index + i];
    }

    private void WriteCharBuffer(char[] chars, int index, int count)
    {
        ExpandCharBuffer(count);
        for (var i = 0; i < count; i++)
            _charBuffer[i] = chars[index + i];
    }
}
