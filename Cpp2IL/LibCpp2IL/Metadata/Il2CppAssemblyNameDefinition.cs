using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace LibCpp2IL.Metadata;

public class Il2CppAssemblyNameDefinition : ReadableClass
{
    public int nameIndex;
    public int cultureIndex;

    [Version(Max = 24.1f)] [Version(Min = 24.2f, Max = 24.3f)] 
    public int hashValueIndex;

    public int publicKeyIndex;
    public uint hash_alg;
    public int hash_len;
    public uint flags;
    public int major;
    public int minor;
    public int build;
    public int revision;
    public ulong publicKeyToken;

    public string Name => LibCpp2IlMain.TheMetadata!.GetStringFromIndex(nameIndex);
    public string Culture => LibCpp2IlMain.TheMetadata!.GetStringFromIndex(cultureIndex);

    public byte[]? PublicKey
    {
        get
        {
            
            
            

            if (IsAtLeast(24.4f) || Is(24.15f))
            {
                
                
                
                
                var result = LibCpp2IlMain.TheMetadata!.GetByteArrayFromIndex(publicKeyIndex);
                return result.Length == 0 ? null : result;
            }
            else
            {
                var str = LibCpp2IlMain.TheMetadata!.GetStringFromIndex(publicKeyIndex);

                if (str is "NULL")
                    return null; 

                
                
                
                if (str.Length > 2 && str[0] is '"' && str[^1] is '"' && TryParseByteArray(str.AsSpan()[1..^1], out var result))
                    return result;

                return null; 
            }

            static bool IsHexDigit(char c) => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');

            static bool TryParseByte(ReadOnlySpan<char> characters, out byte result)
            {
#if NET6_0_OR_GREATER
                return byte.TryParse(characters, System.Globalization.NumberStyles.HexNumber, null, out result);
#else
                return byte.TryParse(characters.ToString(), System.Globalization.NumberStyles.HexNumber, null, out result);
#endif
            }

            static bool TryParseByteArray(ReadOnlySpan<char> characters, [NotNullWhen(true)] out byte[]? result)
            {
                var bytes = new List<byte>();

                var i = 0;
                while (i < characters.Length)
                {
                    
                    
                    
                    
                    if (i > characters.Length - 3 || characters[i] is not '\\' || characters[i + 1] is not 'x')
                    {
                        result = null;
                        return false;
                    }

                    i += 2;

                    if (!IsHexDigit(characters[i]))
                    {
                        result = null;
                        return false;
                    }

                    ReadOnlySpan<char> hexCharacters;
                    if (i + 1 < characters.Length && characters[i + 1] is not '\\')
                    {
                        if (!IsHexDigit(characters[i + 1]))
                        {
                            result = null;
                            return false;
                        }
                        hexCharacters = characters.Slice(i, 2);
                        i += 2;
                    }
                    else
                    {
                        hexCharacters = characters.Slice(i, 1);
                        i += 1;
                    }

                    if (!TryParseByte(hexCharacters, out var b))
                    {
                        result = null;
                        return false;
                    }

                    bytes.Add(b);
                }

                result = bytes.ToArray();
                return true;
            }
        }
    }

    
    
    
    
    
    
    public byte[]? PublicKeyToken
    {
        get
        {
            return publicKeyToken == default ? null : BitConverter.GetBytes(publicKeyToken);
        }
    }

    public string HashValue => LibCpp2IlMain.MetadataVersion > 24.3f ? "NULL" : LibCpp2IlMain.TheMetadata!.GetStringFromIndex(hashValueIndex);

    public override string ToString()
    {
        var pkt = string.Join("-", BitConverter.GetBytes(publicKeyToken).Select(b => b.ToString("X2")));
        return $"{Name}, Version={major}.{minor}.{build}.{revision}, PublicKeyToken={pkt}";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();
        cultureIndex = reader.ReadInt32();
        if (IsAtMost(24.3f) && IsNot(24.15f))
            hashValueIndex = reader.ReadInt32();
        publicKeyIndex = reader.ReadInt32();
        hash_alg = reader.ReadUInt32();
        hash_len = reader.ReadInt32();
        flags = reader.ReadUInt32();
        major = reader.ReadInt32();
        minor = reader.ReadInt32();
        build = reader.ReadInt32();
        revision = reader.ReadInt32();
        publicKeyToken = reader.ReadUInt64();
    }
}
