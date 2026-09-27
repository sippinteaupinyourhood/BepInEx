namespace LibCpp2IL.BinaryStructures;

public class Il2CppCodeRegistration : ReadableClass
{
    




    public static int GetStructSize(bool isBinary32Bit, float metadataVersion)
    {
        
        var size = 0;
        var ptrSize = isBinary32Bit ? 4 : 8;

        if (metadataVersion <= 24.15f)
            
            size += 2 * ptrSize;

        
        size += 4 * ptrSize;

        if (metadataVersion is (>= 24.5f and < 27f) or >= 27.1f)
            
            size += ptrSize;

        
        size += 2 * ptrSize;

        if (metadataVersion <= 24.5f)
            
            size += 2 * ptrSize;

        
        size += 2 * ptrSize;

        if (metadataVersion is (>= 29.1f and < 31f) or >= 31.1f)
            
            size += 2 * ptrSize;

        if (metadataVersion >= 23f)
            
            size += 2 * ptrSize;

        if (metadataVersion >= 24.3f)
            
            size += 2 * ptrSize;

        if (metadataVersion >= 24.2f)
            
            size += 2 * ptrSize;

        return size;
    }

    [Version(Max = 24.15f)] public ulong methodPointersCount;
    [Version(Max = 24.15f)] public ulong methodPointers;

    public ulong reversePInvokeWrapperCount;
    public ulong reversePInvokeWrappers;

    public ulong genericMethodPointersCount;
    public ulong genericMethodPointers;

    
    [Version(Min = 27.1f)] [Version(Min = 24.5f, Max = 24.5f)]
    public ulong genericAdjustorThunks;

    public ulong invokerPointersCount;
    public ulong invokerPointers;

    [Version(Max = 24.5f)] 
    public ulong customAttributeCount;

    [Version(Max = 24.5f)] 
    public ulong customAttributeGeneratorListAddress;

    public ulong unresolvedVirtualCallCount; 
    public ulong unresolvedVirtualCallPointers;

    [Version(Min = 29.1f, Max=31f), Version(Min=31.1f)] public ulong unresolvedInstanceCallPointers;
    [Version(Min = 29.1f, Max=31f), Version(Min=31.1f)] public ulong unresolvedStaticCallPointers;

    [Version(Min = 23)] public ulong interopDataCount;
    [Version(Min = 23)] public ulong interopData;

    [Version(Min = 24.3f)] public ulong windowsRuntimeFactoryCount;
    [Version(Min = 24.3f)] public ulong windowsRuntimeFactoryTable;

    [Version(Min = 24.2f)] public ulong codeGenModulesCount;
    [Version(Min = 24.2f)] public ulong addrCodeGenModulePtrs;

    public override void Read(ClassReadingBinaryReader reader)
    {
        if (IsAtMost(24.15f))
        {
            methodPointersCount = reader.ReadNUint();
            methodPointers = reader.ReadNUint();
        }

        reversePInvokeWrapperCount = reader.ReadNUint();
        reversePInvokeWrappers = reader.ReadNUint();

        genericMethodPointersCount = reader.ReadNUint();
        genericMethodPointers = reader.ReadNUint();

        if (IsAtLeast(24.5f) && IsNot(27f))
            genericAdjustorThunks = reader.ReadNUint();

        invokerPointersCount = reader.ReadNUint();
        invokerPointers = reader.ReadNUint();

        if (IsAtMost(24.5f))
        {
            customAttributeCount = reader.ReadNUint();
            customAttributeGeneratorListAddress = reader.ReadNUint();
        }

        unresolvedVirtualCallCount = reader.ReadNUint();
        unresolvedVirtualCallPointers = reader.ReadNUint();

        if (IsAtLeast(29.1f) && IsNot(31f))
        {
            unresolvedInstanceCallPointers = reader.ReadNUint();
            unresolvedStaticCallPointers = reader.ReadNUint();
        }

        if (IsAtLeast(23f))
        {
            interopDataCount = reader.ReadNUint();
            interopData = reader.ReadNUint();
        }

        if (IsAtLeast(24.2f))
        {
            if (IsAtLeast(24.3f))
            {
                windowsRuntimeFactoryCount = reader.ReadNUint();
                windowsRuntimeFactoryTable = reader.ReadNUint();
            }

            codeGenModulesCount = reader.ReadNUint();
            addrCodeGenModulePtrs = reader.ReadNUint();
        }
    }
}
