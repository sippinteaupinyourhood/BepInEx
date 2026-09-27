using System;
using System.Threading;

namespace LibCpp2IL.Metadata;












public readonly record struct Il2CppVariableWidthIndex<T> where T : ReadableClass
{
    
    private static int widthForThisTypeOnCurrentApplication = -1; 
    
    
    
    
    
    
    
    private static object readSessionLock = new object();
    
    private readonly int value;

    public bool IsNull => value < 0; 
    public bool IsNonNull => !IsNull;
    public int Value => value;
    
    public static Il2CppVariableWidthIndex<T> operator +(Il2CppVariableWidthIndex<T> index, Il2CppVariableWidthIndex<T> offset)
        => new(index.value + offset.value);

    public Il2CppVariableWidthIndex() 
        => throw new NotSupportedException("Do not manually construct an Il2CppVariableWidthIndex. This struct is only intended to be used as a field type in other ReadableClass types, and should be read using the static Read method.");

    private Il2CppVariableWidthIndex(int value) 
        => this.value = value;
    
    public static Il2CppVariableWidthIndex<T> Null => new(-1);
    
    
    
    
    
    
    
    public static Il2CppVariableWidthIndex<T> MakeTemporaryForFixedWidthUsage(int value)
        => new(value);

    public static Il2CppVariableWidthIndex<T> Read(ClassReadingBinaryReader reader)
    {
        if (widthForThisTypeOnCurrentApplication == -1)
            throw new Exception($"Attempted to read an Il2CppVariableWidthIndex before determining the width for this type ({typeof(T)}) on this application. This should never happen, but if it does, it means something has gone very wrong with the threading in Cpp2IL. Please report this to the developers.");

        switch (widthForThisTypeOnCurrentApplication)
        {
            case 1:
                var val = reader.ReadByte();
                if(val == byte.MaxValue)
                    return Null;
                return new(val);
            case 2:
                var val2 = reader.ReadUInt16();
                if(val2 == ushort.MaxValue)
                    return Null;
                return new(val2);
            case 4:
                return new(reader.ReadInt32());
            default:
                throw new Exception($"Invalid width {widthForThisTypeOnCurrentApplication} for Il2CppVariableWidthIndex of type {typeof(T)}.");
        }
    }

    internal static void BeginReadSession(int width)
    {
        var lockTaken = false;
        Monitor.Enter(readSessionLock, ref lockTaken);
        if(!lockTaken)
            throw new Exception("Failed to acquire read session lock for Il2CppVariableWidthIndex. This should never happen, but if it does, it means something has gone very wrong with the threading in Cpp2IL. Please report this to the developers.");
        
        widthForThisTypeOnCurrentApplication = width;
    }

    internal static void BeginReadSessionOnLegacyVersion()
    {
        var lockTaken = false;
        Monitor.Enter(readSessionLock, ref lockTaken);
        if(!lockTaken)
            throw new Exception("Failed to acquire read session lock for Il2CppVariableWidthIndex. This should never happen, but if it does, it means something has gone very wrong with the threading in Cpp2IL. Please report this to the developers.");
        
        widthForThisTypeOnCurrentApplication = 4; 
    }
    
    internal static void EndReadSession()
    {
        widthForThisTypeOnCurrentApplication = -1;
        Monitor.Exit(readSessionLock);
    }
}
