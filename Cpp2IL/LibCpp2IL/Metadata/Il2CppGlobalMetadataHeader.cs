namespace LibCpp2IL.Metadata;

public class Il2CppGlobalMetadataHeader : ReadableClass
{
    public uint magicNumber;
    public int version;

    public Il2CppGlobalMetadataSectionHeader stringLiteral; 
    public Il2CppGlobalMetadataSectionHeader stringLiteralData;
    public Il2CppGlobalMetadataSectionHeader @string; 
    public Il2CppGlobalMetadataSectionHeader events; 
    public Il2CppGlobalMetadataSectionHeader properties; 
    public Il2CppGlobalMetadataSectionHeader methods; 
    public Il2CppGlobalMetadataSectionHeader parameterDefaultValues; 
    public Il2CppGlobalMetadataSectionHeader fieldDefaultValues; 
    public Il2CppGlobalMetadataSectionHeader fieldAndParameterDefaultValueData; 
    public Il2CppGlobalMetadataSectionHeader fieldMarshaledSizes; 
    public Il2CppGlobalMetadataSectionHeader parameters; 
    public Il2CppGlobalMetadataSectionHeader fields; 
    public Il2CppGlobalMetadataSectionHeader genericParameters; 
    public Il2CppGlobalMetadataSectionHeader genericParameterConstraints; 
    public Il2CppGlobalMetadataSectionHeader genericContainers; 
    public Il2CppGlobalMetadataSectionHeader nestedTypes; 
    public Il2CppGlobalMetadataSectionHeader interfaces; 
    public Il2CppGlobalMetadataSectionHeader vtableMethods; 
    public Il2CppGlobalMetadataSectionHeader interfaceOffsets; 
    public Il2CppGlobalMetadataSectionHeader typeDefinitions; 
    
    [Version(Min = 104f)] public Il2CppGlobalMetadataSectionHeader typeInlineArrays; 

    [Version(Max = 24.15f)] public Il2CppGlobalMetadataSectionHeader rgctxEntries; 

    public Il2CppGlobalMetadataSectionHeader images; 
    public Il2CppGlobalMetadataSectionHeader assemblies; 

    [Version(Max = 24.5f)] public Il2CppGlobalMetadataSectionHeader metadataUsageLists; 
    [Version(Max = 24.5f)] public Il2CppGlobalMetadataSectionHeader metadataUsagePairs; 

    public Il2CppGlobalMetadataSectionHeader fieldRefs; 
    public Il2CppGlobalMetadataSectionHeader referencedAssemblies; 

    
    [Version(Max = 27.9f)] public Il2CppGlobalMetadataSectionHeader attributesInfo; 
    [Version(Max = 27.9f)] public Il2CppGlobalMetadataSectionHeader attributeTypes; 

    
    [Version(Min = 27.9f)] public Il2CppGlobalMetadataSectionHeader attributeData; 
    [Version(Min = 27.9f)] public Il2CppGlobalMetadataSectionHeader attributeDataRange; 

    public Il2CppGlobalMetadataSectionHeader unresolvedVirtualCallParameterTypes; 
    public Il2CppGlobalMetadataSectionHeader unresolvedVirtualCallParameterRanges; 

    [Version(Min = 23)] public Il2CppGlobalMetadataSectionHeader windowsRuntimeTypeNames; 

    [Version(Min = 27)] public Il2CppGlobalMetadataSectionHeader windowsRuntimeStrings; 

    [Version(Min = 24)] public Il2CppGlobalMetadataSectionHeader exportedTypeDefinitions; 

    public override void Read(ClassReadingBinaryReader reader)
    {
        magicNumber = reader.ReadUInt32();
        version = reader.ReadInt32();

        stringLiteral = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        stringLiteralData = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        @string = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        events = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        properties = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        methods = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        parameterDefaultValues = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        fieldDefaultValues = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        fieldAndParameterDefaultValueData = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        fieldMarshaledSizes = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        parameters = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        fields = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        genericParameters = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        genericParameterConstraints = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        genericContainers = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        nestedTypes = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        interfaces = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        vtableMethods = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        interfaceOffsets = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        typeDefinitions = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        
        if (IsAtLeast(104f))
            typeInlineArrays = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsAtMost(24.15f))
            rgctxEntries = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        images = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        assemblies = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsLessThan(27f))
        {
            metadataUsageLists = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
            metadataUsagePairs = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        }

        fieldRefs = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        referencedAssemblies = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsLessThan(29f))
        {
            attributesInfo = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
            attributeTypes = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        }
        else
        {
            attributeData = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
            attributeDataRange = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        }

        unresolvedVirtualCallParameterTypes = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        unresolvedVirtualCallParameterRanges = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
        windowsRuntimeTypeNames = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsAtLeast(27f))
            windowsRuntimeStrings = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();

        if (IsAtLeast(24f))
            exportedTypeDefinitions = reader.ReadReadableHereNoLock<Il2CppGlobalMetadataSectionHeader>();
    }
}
