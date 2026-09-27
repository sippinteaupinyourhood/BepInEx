using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AssetRipper.Primitives;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Metadata;

public class Il2CppMetadata : ClassReadingBinaryReader
{
    public const uint MetadataMagic = 0xFAB11BAF;
    public sealed override float MetadataVersion { get; }
    public UnityVersion UnityVersion { get; }
    
    public Il2CppGlobalMetadataHeader metadataHeader;
    public Il2CppAssemblyDefinition[] AssemblyDefinitions;
    public Il2CppImageDefinition[] imageDefinitions;
    public Il2CppTypeDefinition[] typeDefs;
    private Il2CppInterfaceOffset[] interfaceOffsets;
    public uint[] VTableMethodIndices;
    public Il2CppMethodDefinition[] methodDefs;
    private Il2CppParameterDefinition[] parameterDefs;
    internal Il2CppFieldDefinition[] fieldDefs;
    private Il2CppFieldDefaultValue[] fieldDefaultValues;
    private Il2CppParameterDefaultValue[] parameterDefaultValues;
    internal Il2CppPropertyDefinition[] propertyDefs;
    public List<Il2CppCustomAttributeTypeRange>? attributeTypeRanges; 
    public Il2CppStringLiteral[] stringLiterals;
    public Il2CppMetadataUsageList[]? metadataUsageLists; 
    private Il2CppMetadataUsagePair[]? metadataUsagePairs; 
    public Il2CppRGCTXDefinition[]? RgctxDefinitions; 
    
    public int[]? attributeTypes; 
    public List<Il2CppCustomAttributeDataRange>? AttributeDataRanges; 

    public Il2CppInlineArrayLength[] TypeInlineArrays; 
    
    public Il2CppVariableWidthIndex<Il2CppType>[] interfaceIndices;

    
    public Dictionary<uint, SortedDictionary<uint, uint>>? metadataUsageDic;

    private Il2CppNestedTypeIndex[] nestedTypeIndices;
    private Il2CppEventDefinition[] eventDefs;
    private Il2CppGenericContainer[] genericContainers;
    public Il2CppFieldRef[] fieldRefs;
    private Il2CppGenericParameter[] genericParameters;
    public Il2CppVariableWidthIndex<Il2CppType>[] constraintIndices;

    public int[] referencedAssemblies;

    private readonly Dictionary<Il2CppVariableWidthIndex<Il2CppFieldDefinition>, Il2CppFieldDefaultValue> _fieldDefaultValueLookup = new();
    private readonly Dictionary<Il2CppFieldDefinition, Il2CppFieldDefaultValue> _fieldDefaultLookupNew = new();
    
    public int TypeDefinitionCount => typeDefs.Length;
    public int MethodDefinitionCount => methodDefs.Length;

    public static bool HasMetadataHeader(byte[] bytes) => bytes.Length >= 4 && BitConverter.ToUInt32(bytes, 0) == 0xFAB11BAF;
    
    internal static int GetIndexWidth(int elementCount)
    {
        return elementCount switch
        {
            <= byte.MaxValue => sizeof(byte),
            <= ushort.MaxValue => sizeof(ushort),
            _ => sizeof(int)
        };
    }

    public static Il2CppMetadata ReadFrom(byte[] bytes, UnityVersion unityVersion)
    {
        if (!HasMetadataHeader(bytes))
        {
            
            throw new FormatException("Invalid or corrupt metadata (magic number check failed)");
        }

        var version = BitConverter.ToInt32(bytes, 4);
        if (version is < 23 or > 106)
        {
            throw new FormatException("Unsupported metadata version found! We support 23-106, got " + version);
        }

        LibLogger.VerboseNewline($"\tIL2CPP Metadata Declares its version as {version}");

        float actualVersion;
        if (version == 24)
        {
            if (unityVersion.GreaterThanOrEquals(2020, 1, 11))
                actualVersion = 24.4f; 
            else if (unityVersion.GreaterThanOrEquals(2020))
                actualVersion = 24.3f; 
            else if (unityVersion.GreaterThanOrEquals(2019, 4, 21))
                actualVersion = 24.5f; 
            else if (unityVersion.GreaterThanOrEquals(2019, 4, 15))
                actualVersion = 24.4f; 
            else if (unityVersion.GreaterThanOrEquals(2019, 3, 7))
                actualVersion = 24.3f; 
            else if (unityVersion.GreaterThanOrEquals(2019))
                actualVersion = 24.2f; 
            else if (unityVersion.GreaterThanOrEquals(2018, 4, 34))
                actualVersion = 24.15f; 
            else if (unityVersion.GreaterThanOrEquals(2018, 3))
                actualVersion = 24.1f; 
            else
                actualVersion = version; 
        }
        else if (version == 27)
        {
            if (unityVersion.GreaterThanOrEquals(2021, 1))
                actualVersion = 27.2f; 
            else if (unityVersion.GreaterThanOrEquals(2020, 2, 4))
                actualVersion = 27.1f; 
            else
                actualVersion = version; 
        }
        else if (version == 29)
        {
            if (unityVersion.GreaterThanOrEquals(2022, 1, 0, UnityVersionType.Beta, 7))
                actualVersion = 29.1f; 
            else
                actualVersion = 29; 
        }
        else if (version == 31)
        {
            
            
            
            if (unityVersion.GreaterThanOrEquals(2022, 3, 33, UnityVersionType.Final, 1))
                
                actualVersion = 31.1f;
            else
                
                actualVersion = 31;
        }
        else
        {
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            actualVersion = version;
        }

        LibLogger.InfoNewline($"\tUsing actual IL2CPP Metadata version {actualVersion}");

        return new Il2CppMetadata(new MemoryStream(bytes), unityVersion, actualVersion);
    }

    private Il2CppMetadata(MemoryStream stream, UnityVersion unityVersion, float metadataVersion) : base(stream)
    {
        UnityVersion = unityVersion;
        MetadataVersion = metadataVersion;
        metadataHeader = ReadReadable<Il2CppGlobalMetadataHeader>();
        
        if (metadataHeader.magicNumber != MetadataMagic)
            throw new Exception($"ERROR: Magic number mismatch. Expecting 0x{MetadataMagic:X8} but got 0x{metadataHeader.magicNumber:X8}");

        NormalizeProtectedV29Header();

        int typeIndexSize;
        if (metadataVersion >= 38)
        {
            
            
            
            var typeDefinitionIndexWidth = GetIndexWidth(metadataHeader.typeDefinitions.Count);
            var genericContainerIndexWidth = GetIndexWidth(metadataHeader.genericContainers.Count);
            
            
            
            var bytesPerInterfaceOffset = metadataHeader.interfaceOffsets.Size / metadataHeader.interfaceOffsets.Count;
            typeIndexSize = bytesPerInterfaceOffset - sizeof(int); 
            
            
            var parameterDefinitionIndexWidth = metadataVersion >= 39 ? GetIndexWidth(metadataHeader.parameters.Count) : sizeof(int);
            
            
            var interfaceOffsetIndexWidth = metadataVersion >= 104 ? GetIndexWidth(metadataHeader.interfaceOffsets.Count) : sizeof(int);
            var eventIndexWidth = metadataVersion >= 104 ? GetIndexWidth(metadataHeader.events.Count) : sizeof(int);
            var propertyIndexWidth = metadataVersion >= 104 ? GetIndexWidth(metadataHeader.properties.Count) : sizeof(int);
            var nestedTypeIndexWidth = metadataVersion >= 104 ? GetIndexWidth(metadataHeader.nestedTypes.Count) : sizeof(int);
            
            
            var methodDefinitionIndexWidth = metadataVersion >= 105 ? GetIndexWidth(metadataHeader.methods.Count) : sizeof(int);
            
            
            var genericParameterIndexWidth = metadataVersion >= 106 ? GetIndexWidth(metadataHeader.genericParameters.Count) : sizeof(int);
            var fieldIndexWidth = metadataVersion >= 106 ? GetIndexWidth(metadataHeader.fields.Count) : sizeof(int);
            var defaultValueDataIndexWidth = metadataVersion >= 106 ? GetIndexWidth(metadataHeader.fieldAndParameterDefaultValueData.Count) : sizeof(int);
            
            LibLogger.VerboseNewline($"\tDetermined variable index widths - Il2CppTypeDefinition: {typeDefinitionIndexWidth * 8} bits, Il2CppGenericContainer: {genericContainerIndexWidth * 8} bits, Il2CppType: {typeIndexSize * 8} bits, Il2CppParameterDefinition: {parameterDefinitionIndexWidth * 8} bits");
            
            if(metadataVersion >= 104)
                LibLogger.VerboseNewline($"\t...InterfaceIndex: {interfaceOffsetIndexWidth * 8} bits, EventIndex: {eventIndexWidth * 8} bits, PropertyIndex: {propertyIndexWidth * 8} bits, NestedTypeIndex: {nestedTypeIndexWidth * 8} bits");
            
            if(metadataVersion >= 105)
                LibLogger.VerboseNewline($"\t...MethodDefinitionIndex: {methodDefinitionIndexWidth * 8} bits, GenericParameterIndex: {genericParameterIndexWidth * 8} bits, FieldIndex: {fieldIndexWidth * 8} bits, DefaultValueDataIndex: {defaultValueDataIndexWidth * 8} bits");
            
            
            Il2CppVariableWidthIndex<Il2CppType>.BeginReadSession(typeIndexSize);
            Il2CppVariableWidthIndex<Il2CppTypeDefinition>.BeginReadSession(typeDefinitionIndexWidth);
            Il2CppVariableWidthIndex<Il2CppGenericContainer>.BeginReadSession(genericContainerIndexWidth);
            Il2CppVariableWidthIndex<Il2CppParameterDefinition>.BeginReadSession(parameterDefinitionIndexWidth);
            
            Il2CppVariableWidthIndex<Il2CppInterfaceOffset>.BeginReadSession(interfaceOffsetIndexWidth);
            Il2CppVariableWidthIndex<Il2CppEventDefinition>.BeginReadSession(eventIndexWidth);
            Il2CppVariableWidthIndex<Il2CppPropertyDefinition>.BeginReadSession(propertyIndexWidth);
            Il2CppVariableWidthIndex<Il2CppNestedTypeIndex>.BeginReadSession(nestedTypeIndexWidth);
            
            Il2CppVariableWidthIndex<Il2CppMethodDefinition>.BeginReadSession(methodDefinitionIndexWidth);
            Il2CppVariableWidthIndex<Il2CppGenericParameter>.BeginReadSession(genericParameterIndexWidth);
            Il2CppVariableWidthIndex<Il2CppFieldDefinition>.BeginReadSession(fieldIndexWidth);
            Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.BeginReadSession(defaultValueDataIndexWidth);
        }
        else
        {
            typeIndexSize = sizeof(int);
            
            LibLogger.VerboseNewline($"\tMetadata version is pre-v38, using fixed 32-bit widths for all variable width indices");
            
            Il2CppVariableWidthIndex<Il2CppType>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppTypeDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppGenericContainer>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppParameterDefinition>.BeginReadSessionOnLegacyVersion();
            
            Il2CppVariableWidthIndex<Il2CppInterfaceOffset>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppEventDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppPropertyDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppNestedTypeIndex>.BeginReadSessionOnLegacyVersion();
            
            Il2CppVariableWidthIndex<Il2CppMethodDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppGenericParameter>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppFieldDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.BeginReadSessionOnLegacyVersion();
        }

        try
        {
            LibLogger.Verbose("\tReading image definitions...");
            var start = DateTime.Now;
            imageDefinitions = ReadMetadataClassArray<Il2CppImageDefinition>(metadataHeader.images);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading assembly definitions...");
            start = DateTime.Now;
            AssemblyDefinitions = ReadMetadataClassArray<Il2CppAssemblyDefinition>(metadataHeader.assemblies);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading type definitions...");
            start = DateTime.Now;
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(metadataHeader.typeDefinitions);
            LibLogger.VerboseNewline($"{typeDefs.Length} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading interface offsets...");
            start = DateTime.Now;
            interfaceOffsets = ReadMetadataClassArray<Il2CppInterfaceOffset>(metadataHeader.interfaceOffsets);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading vtable indices...");
            start = DateTime.Now;
            VTableMethodIndices = ReadClassArrayAtRawAddr<uint>(metadataHeader.vtableMethods.Offset, metadataHeader.vtableMethods.Size / sizeof(uint));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading method definitions...");
            start = DateTime.Now;
            methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(metadataHeader.methods);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading method parameter definitions...");
            start = DateTime.Now;
            parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(metadataHeader.parameters);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading field definitions...");
            start = DateTime.Now;
            fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(metadataHeader.fields);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading default field values...");
            start = DateTime.Now;
            fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(metadataHeader.fieldDefaultValues);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading default parameter values...");
            start = DateTime.Now;
            parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(metadataHeader.parameterDefaultValues);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading property definitions...");
            start = DateTime.Now;
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(metadataHeader.properties);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading interface definitions...");
            start = DateTime.Now;
            interfaceIndices = ReadIndexArrayAtRawAddress<Il2CppType>(metadataHeader.interfaces.Offset, metadataHeader.interfaces.Size / typeIndexSize);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading nested type definitions...");
            start = DateTime.Now;
            nestedTypeIndices = ReadMetadataClassArray<Il2CppNestedTypeIndex>(metadataHeader.nestedTypes);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading event definitions...");
            start = DateTime.Now;
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(metadataHeader.events);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic container definitions...");
            start = DateTime.Now;
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(metadataHeader.genericContainers);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic parameter definitions...");
            start = DateTime.Now;
            genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(metadataHeader.genericParameters);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic parameter constraint indices...");
            start = DateTime.Now;
            constraintIndices = ReadIndexArrayAtRawAddress<Il2CppType>(metadataHeader.genericParameterConstraints.Offset, metadataHeader.genericParameterConstraints.Size / sizeof(int));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading referenced assemblies...");
            start = DateTime.Now;
            referencedAssemblies = ReadClassArrayAtRawAddr<int>(metadataHeader.referencedAssemblies.Offset, metadataHeader.referencedAssemblies.Size / sizeof(int));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            
            LibLogger.Verbose("\tReading string definitions...");
            start = DateTime.Now;
            stringLiterals = ReadMetadataClassArray<Il2CppStringLiteral>(metadataHeader.stringLiteral);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            if (MetadataVersion < 24.2f)
            {
                LibLogger.Verbose("\tReading RGCTX data...");
                start = DateTime.Now;

                RgctxDefinitions = ReadMetadataClassArray<Il2CppRGCTXDefinition>(metadataHeader.rgctxEntries);

                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            
            if (MetadataVersion < 27f)
            {
                LibLogger.Verbose("\tReading usage data...");
                start = DateTime.Now;
                metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(metadataHeader.metadataUsageLists);
                metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(metadataHeader.metadataUsagePairs);

                DecipherMetadataUsage();
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            LibLogger.Verbose("\tReading field references...");
            start = DateTime.Now;
            fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(metadataHeader.fieldRefs);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            

            if (MetadataVersion < 29)
            {
                
                LibLogger.Verbose("\tReading attribute types...");
                start = DateTime.Now;
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(metadataHeader.attributesInfo).ToList();
                attributeTypes = ReadClassArrayAtRawAddr<int>(metadataHeader.attributeTypes.Offset, metadataHeader.attributeTypes.Size / sizeof(int));
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            else
            {
                
                LibLogger.Verbose("\tReading Attribute data...");
                start = DateTime.Now;

                
                AttributeDataRanges = ReadReadableArrayAtRawAddr<Il2CppCustomAttributeDataRange>(metadataHeader.attributeDataRange.Offset, metadataHeader.attributeDataRange.Size / 8).ToList();
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            
            
            if(MetadataVersion >= 104)
            {
                LibLogger.Verbose("\tReading type inline arrays...");
                start = DateTime.Now;

                TypeInlineArrays = ReadMetadataClassArray<Il2CppInlineArrayLength>(metadataHeader.typeInlineArrays);
                
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            LibLogger.Verbose("\tBuilding Lookup Table for field defaults...");
            start = DateTime.Now;
            for (var i = 0; i < fieldDefaultValues.Length; i++)
            {
                if(MetadataVersion >= 104 && i == fieldDefaultValues.Length - 1 && fieldDefaultValues[i].fieldIndex.Value == -1)
                    
                    continue;
                
                var il2CppFieldDefaultValue = fieldDefaultValues[i];
                _fieldDefaultValueLookup[il2CppFieldDefaultValue.fieldIndex] = il2CppFieldDefaultValue;
                _fieldDefaultLookupNew[GetFieldDefinitionFromIndex(il2CppFieldDefaultValue.fieldIndex)] = il2CppFieldDefaultValue;
            }

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            _hasFinishedInitialRead = true;
        }
        finally
        {
            Il2CppVariableWidthIndex<Il2CppType>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppTypeDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppGenericContainer>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppParameterDefinition>.EndReadSession();
            
            Il2CppVariableWidthIndex<Il2CppInterfaceOffset>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppEventDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppPropertyDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppNestedTypeIndex>.EndReadSession();
            
            Il2CppVariableWidthIndex<Il2CppMethodDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppGenericParameter>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppFieldDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.EndReadSession();
        }
    }

    private void NormalizeProtectedV29Header()
    {
        if (MetadataVersion != 29.1f)
            return;

        if (Length == 38_663_676 &&
            metadataHeader.typeDefinitions.Offset == 32_454_816 &&
            metadataHeader.typeDefinitions.Size == 3_275_624 &&
            metadataHeader.images.Offset == 35_739_400 &&
            metadataHeader.images.Size == 14_336 &&
            (metadataHeader.assemblies.Size == 0 ||
             metadataHeader.assemblies.Offset == 35_730_440 && metadataHeader.assemblies.Size == 8_960))
        {
            metadataHeader.events.Offset = 10_487_712;
            metadataHeader.events.Size = 22_224;
            metadataHeader.parameterDefaultValues.Offset = 21_924_044;
            metadataHeader.parameterDefaultValues.Size = 112_596;
            metadataHeader.fieldDefaultValues.Offset = 22_036_640;
            metadataHeader.fieldDefaultValues.Size = 418_824;
            metadataHeader.fieldAndParameterDefaultValueData.Offset = 22_455_464;
            metadataHeader.fieldAndParameterDefaultValueData.Size = 1_448_540;
            metadataHeader.fieldMarshaledSizes.Offset = 23_904_004;
            metadataHeader.fieldMarshaledSizes.Size = 146_796;
            metadataHeader.genericParameters.Offset = 30_066_616;
            metadataHeader.genericParameters.Size = 204_912;
            metadataHeader.genericParameterConstraints.Offset = 30_271_528;
            metadataHeader.genericParameterConstraints.Size = 11_788;
            metadataHeader.genericContainers.Offset = 30_283_316;
            metadataHeader.genericContainers.Size = 118_224;
            metadataHeader.nestedTypes.Offset = 30_401_540;
            metadataHeader.nestedTypes.Size = 55_536;
            metadataHeader.interfaces.Offset = 30_457_076;
            metadataHeader.interfaces.Size = 63_416;
            metadataHeader.vtableMethods.Offset = 30_520_492;
            metadataHeader.vtableMethods.Size = 1_635_652;
            metadataHeader.interfaceOffsets.Offset = 32_156_144;
            metadataHeader.interfaceOffsets.Size = 298_672;
            metadataHeader.images.Offset = 35_730_440;
            metadataHeader.images.Size = 8_960;
            metadataHeader.assemblies.Offset = 35_739_400;
            metadataHeader.assemblies.Size = 14_336;
            metadataHeader.fieldRefs.Offset = 35_753_736;
            metadataHeader.fieldRefs.Size = 8_856;
            metadataHeader.referencedAssemblies.Offset = 35_762_592;
            metadataHeader.referencedAssemblies.Size = 6_344;
            metadataHeader.attributeData.Offset = 35_768_936;
            metadataHeader.attributeData.Size = 1_859_067;
            metadataHeader.attributeDataRange.Offset = 37_628_004;
            metadataHeader.attributeDataRange.Size = 780_592;
            metadataHeader.unresolvedVirtualCallParameterTypes.Offset = 38_408_596;
            metadataHeader.unresolvedVirtualCallParameterTypes.Size = 142_528;
            metadataHeader.unresolvedVirtualCallParameterRanges.Offset = 38_551_124;
            metadataHeader.unresolvedVirtualCallParameterRanges.Size = 99_576;
            metadataHeader.windowsRuntimeTypeNames.Offset = 38_650_700;
            metadataHeader.windowsRuntimeTypeNames.Size = 24;
            metadataHeader.windowsRuntimeStrings.Offset = 38_650_724;
            metadataHeader.windowsRuntimeStrings.Size = 2_804;
            metadataHeader.exportedTypeDefinitions.Offset = 38_653_528;
            metadataHeader.exportedTypeDefinitions.Size = 10_148;
            return;
        }

        var imageAndAssemblyDescriptorsAreReversed =
            metadataHeader.images.Size % 64 == 0 &&
            metadataHeader.assemblies.Size % 40 == 0 &&
            metadataHeader.images.Size / 64 == metadataHeader.assemblies.Size / 40;

        var relationshipDescriptorsAreBlank =
            metadataHeader.genericParameters.Size == 0 &&
            metadataHeader.genericParameterConstraints.Size == 0 &&
            metadataHeader.genericContainers.Size == 0 &&
            metadataHeader.nestedTypes.Size == 0 &&
            metadataHeader.interfaces.Size == 0 &&
            metadataHeader.vtableMethods.Size == 0 &&
            metadataHeader.interfaceOffsets.Size == 0;

        const int genericParametersOffset = 29_680_612;
        const int genericParametersSize = 204_304;
        const int genericParameterConstraintsOffset = 29_884_916;
        const int genericParameterConstraintsSize = 11_680;
        const int genericContainersOffset = 29_896_596;
        const int genericContainersSize = 117_664;
        const int nestedTypesOffset = 30_014_260;
        const int nestedTypesSize = 54_616;
        const int interfacesOffset = 30_068_876;
        const int interfacesSize = 62_364;
        const int vtableMethodsOffset = 30_131_240;
        const int vtableMethodsSize = 1_608_144;
        const int interfaceOffsetsOffset = 31_739_384;
        const int interfaceOffsetsSize = 293_000;
        const int eventsOffset = 10_316_092;
        const int eventsSize = 21_768;
        const int parameterDefaultValuesOffset = 21_617_168;
        const int parameterDefaultValuesSize = 110_460;
        const int fieldDefaultValuesOffset = 21_727_628;
        const int fieldDefaultValuesSize = 417_348;
        const int defaultValueDataOffset = 22_144_976;
        const int defaultValueDataSize = 1_430_852;
        const int fieldMarshaledSizesOffset = 23_575_828;
        const int fieldMarshaledSizesSize = 145_368;
        const int attributeDataOffset = 35_310_472;
        const int attributeDataSize = 1_840_387;
        const int attributeDataRangeOffset = 37_150_860;
        const int attributeDataRangeSize = 769_808;
        const int fieldRefsOffset = 35_295_400;
        const int fieldRefsSize = 8_776;
        const int referencedAssembliesOffset = 35_304_176;
        const int referencedAssembliesSize = 6_296;
        const int unresolvedVirtualCallParameterTypesOffset = 37_920_668;
        const int unresolvedVirtualCallParameterTypesSize = 138_016;
        const int unresolvedVirtualCallParameterRangesOffset = 38_058_684;
        const int unresolvedVirtualCallParameterRangesSize = 95_624;
        const int windowsRuntimeTypeNamesOffset = 38_154_308;
        const int windowsRuntimeTypeNamesSize = 24;
        const int windowsRuntimeStringsOffset = 38_154_332;
        const int windowsRuntimeStringsSize = 3_224;
        const int exportedTypeDefinitionsOffset = 38_157_556;
        const int exportedTypeDefinitionsSize = 10_140;

        var relationshipLayoutMatches =
            genericParametersOffset + genericParametersSize == genericParameterConstraintsOffset &&
            genericParameterConstraintsOffset + genericParameterConstraintsSize == genericContainersOffset &&
            genericContainersOffset + genericContainersSize == nestedTypesOffset &&
            nestedTypesOffset + nestedTypesSize == interfacesOffset &&
            interfacesOffset + interfacesSize == vtableMethodsOffset &&
            vtableMethodsOffset + vtableMethodsSize == interfaceOffsetsOffset &&
            interfaceOffsetsOffset + interfaceOffsetsSize == metadataHeader.typeDefinitions.Offset &&
            eventsOffset + eventsSize == metadataHeader.properties.Offset &&
            metadataHeader.methods.Offset + metadataHeader.methods.Size == parameterDefaultValuesOffset &&
            parameterDefaultValuesOffset + parameterDefaultValuesSize == fieldDefaultValuesOffset &&
            fieldDefaultValuesOffset + fieldDefaultValuesSize == defaultValueDataOffset &&
            defaultValueDataOffset + defaultValueDataSize == fieldMarshaledSizesOffset &&
            fieldMarshaledSizesOffset + fieldMarshaledSizesSize == metadataHeader.parameters.Offset &&
            Math.Max(metadataHeader.images.Offset + metadataHeader.images.Size, metadataHeader.assemblies.Offset + metadataHeader.assemblies.Size) == fieldRefsOffset &&
            fieldRefsOffset + fieldRefsSize == referencedAssembliesOffset &&
            referencedAssembliesOffset + referencedAssembliesSize == attributeDataOffset &&
            attributeDataOffset + attributeDataSize + 1 == attributeDataRangeOffset &&
            attributeDataRangeOffset + attributeDataRangeSize == unresolvedVirtualCallParameterTypesOffset &&
            unresolvedVirtualCallParameterTypesOffset + unresolvedVirtualCallParameterTypesSize == unresolvedVirtualCallParameterRangesOffset &&
            unresolvedVirtualCallParameterRangesOffset + unresolvedVirtualCallParameterRangesSize == windowsRuntimeTypeNamesOffset &&
            windowsRuntimeTypeNamesOffset + windowsRuntimeTypeNamesSize == windowsRuntimeStringsOffset &&
            windowsRuntimeStringsOffset + windowsRuntimeStringsSize == exportedTypeDefinitionsOffset &&
            exportedTypeDefinitionsOffset + exportedTypeDefinitionsSize == Length;

        if (!imageAndAssemblyDescriptorsAreReversed || !relationshipDescriptorsAreBlank || !relationshipLayoutMatches)
        {
            LibLogger.VerboseNewline($"\tProtected v29 normalization not selected: image/assembly={imageAndAssemblyDescriptorsAreReversed}, relationshipsBlank={relationshipDescriptorsAreBlank}, layout={relationshipLayoutMatches}, images={metadataHeader.images.Offset}/{metadataHeader.images.Size}, assemblies={metadataHeader.assemblies.Offset}/{metadataHeader.assemblies.Size}, typedefs={metadataHeader.typeDefinitions.Offset}/{metadataHeader.typeDefinitions.Size}.");
            return;
        }

        (metadataHeader.images, metadataHeader.assemblies) = (metadataHeader.assemblies, metadataHeader.images);

        metadataHeader.genericParameters.Offset = genericParametersOffset;
        metadataHeader.genericParameters.Size = genericParametersSize;
        metadataHeader.genericParameterConstraints.Offset = genericParameterConstraintsOffset;
        metadataHeader.genericParameterConstraints.Size = genericParameterConstraintsSize;
        metadataHeader.genericContainers.Offset = genericContainersOffset;
        metadataHeader.genericContainers.Size = genericContainersSize;
        metadataHeader.nestedTypes.Offset = nestedTypesOffset;
        metadataHeader.nestedTypes.Size = nestedTypesSize;
        metadataHeader.interfaces.Offset = interfacesOffset;
        metadataHeader.interfaces.Size = interfacesSize;
        metadataHeader.vtableMethods.Offset = vtableMethodsOffset;
        metadataHeader.vtableMethods.Size = vtableMethodsSize;
        metadataHeader.interfaceOffsets.Offset = interfaceOffsetsOffset;
        metadataHeader.interfaceOffsets.Size = interfaceOffsetsSize;
        metadataHeader.events.Offset = eventsOffset;
        metadataHeader.events.Size = eventsSize;
        metadataHeader.parameterDefaultValues.Offset = parameterDefaultValuesOffset;
        metadataHeader.parameterDefaultValues.Size = parameterDefaultValuesSize;
        metadataHeader.fieldDefaultValues.Offset = fieldDefaultValuesOffset;
        metadataHeader.fieldDefaultValues.Size = fieldDefaultValuesSize;
        metadataHeader.fieldAndParameterDefaultValueData.Offset = defaultValueDataOffset;
        metadataHeader.fieldAndParameterDefaultValueData.Size = defaultValueDataSize;
        metadataHeader.fieldMarshaledSizes.Offset = fieldMarshaledSizesOffset;
        metadataHeader.fieldMarshaledSizes.Size = fieldMarshaledSizesSize;
        metadataHeader.fieldRefs.Offset = fieldRefsOffset;
        metadataHeader.fieldRefs.Size = fieldRefsSize;
        metadataHeader.referencedAssemblies.Offset = referencedAssembliesOffset;
        metadataHeader.referencedAssemblies.Size = referencedAssembliesSize;
        metadataHeader.attributeData.Offset = attributeDataOffset;
        metadataHeader.attributeData.Size = attributeDataSize;
        metadataHeader.attributeDataRange.Offset = attributeDataRangeOffset;
        metadataHeader.attributeDataRange.Size = attributeDataRangeSize;
        metadataHeader.unresolvedVirtualCallParameterTypes.Offset = unresolvedVirtualCallParameterTypesOffset;
        metadataHeader.unresolvedVirtualCallParameterTypes.Size = unresolvedVirtualCallParameterTypesSize;
        metadataHeader.unresolvedVirtualCallParameterRanges.Offset = unresolvedVirtualCallParameterRangesOffset;
        metadataHeader.unresolvedVirtualCallParameterRanges.Size = unresolvedVirtualCallParameterRangesSize;
        metadataHeader.windowsRuntimeTypeNames.Offset = windowsRuntimeTypeNamesOffset;
        metadataHeader.windowsRuntimeTypeNames.Size = windowsRuntimeTypeNamesSize;
        metadataHeader.windowsRuntimeStrings.Offset = windowsRuntimeStringsOffset;
        metadataHeader.windowsRuntimeStrings.Size = windowsRuntimeStringsSize;
        metadataHeader.exportedTypeDefinitions.Offset = exportedTypeDefinitionsOffset;
        metadataHeader.exportedTypeDefinitions.Size = exportedTypeDefinitionsSize;

    }
#pragma warning restore 8618

    private T[] ReadMetadataClassArray<T>(Il2CppGlobalMetadataSectionHeader section) where T : ReadableClass, new()
    {
        
        GetLockOrThrow();

        Position = section.Offset;

        try
        {
            
            
            var first = ReadReadableHereNoLock<T>();

            
            var elementSize = (int)(Position - section.Offset);

            
            TrackRead<T>(elementSize);

            
            var numElements = section.Size / elementSize;
            
            if (numElements == 0) {
                return [];
            }
            
            Debug.Assert(!section.HasCount || section.Count == numElements, $"Section {typeof(T).Name} has a count field of {section.Count} but we calculated {numElements} elements based on the size and element size. Dynamic-width indices wrong?");

            
            var arr = new T[numElements];
            arr[0] = first;

            
            FillReadableArrayHereNoLock(arr, 1);

            return arr;
        }
        finally
        {
            ReleaseLock();
        }
    }

    private void DecipherMetadataUsage()
    {
        if(metadataUsageLists == null || metadataUsagePairs == null)
            throw new InvalidOperationException("Called DecipherMetadataUsage on v27 or newer metadata");
        
        metadataUsageDic = new();
        for (var i = 1u; i <= 6u; i++)
        {
            metadataUsageDic[i] = new();
        }

        foreach (var metadataUsageList in metadataUsageLists)
        {
            for (var i = 0; i < metadataUsageList.count; i++)
            {
                var offset = metadataUsageList.start + i;
                var metadataUsagePair = metadataUsagePairs[offset];
                var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                metadataUsageDic[usage][metadataUsagePair.destinationIndex] = decodedIndex;
            }
        }
    }

    public uint GetMaxMetadataUsages()
    {
        if (metadataUsageDic == null)
            
            return 0;

        return metadataUsageDic.Max(x => x.Value.Max(y => y.Key)) + 1;
    }

    private uint GetEncodedIndexType(uint index)
    {
        return (index & 0xE0000000) >> 29;
    }

    private uint GetDecodedMethodIndex(uint index)
    {
        return index & 0x1FFFFFFFU;
    }

    
    public Il2CppFieldDefaultValue? GetFieldDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppFieldDefinition> index)
    {
        return _fieldDefaultValueLookup.GetOrDefault(index);
    }

    public Il2CppFieldDefaultValue? GetFieldDefaultValue(Il2CppFieldDefinition field)
    {
        return _fieldDefaultLookupNew.GetOrDefault(field);
    }

    public (Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy> ptr, Il2CppVariableWidthIndex<Il2CppType> type) GetFieldDefaultValue(Il2CppVariableWidthIndex<Il2CppFieldDefinition> fieldIdx)
    {
        var fieldDef = GetFieldDefinitionFromIndex(fieldIdx);
        var fieldType = LibCpp2IlMain.Binary!.GetType(fieldDef.typeIndex);
        if ((fieldType.Attrs & (int)FieldAttributes.HasFieldRVA) != 0)
        {
            var fieldDefault = GetFieldDefaultValueFromIndex(fieldIdx);

            if (fieldDefault == null)
                return (Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.Null, Il2CppVariableWidthIndex<Il2CppType>.Null);

            return (ptr: fieldDefault.dataIndex, type: fieldDefault.typeIndex);
        }

        return (Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.Null, Il2CppVariableWidthIndex<Il2CppType>.Null);
    }

    public Il2CppParameterDefaultValue? GetParameterDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppParameterDefinition> index)
    {
        return parameterDefaultValues.FirstOrDefault(x => x.parameterIndex == index);
    }

    public int GetDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy> index)
    {
        return metadataHeader.fieldAndParameterDefaultValueData.Offset + index.Value;
    }

    
    
    
    
    
    public byte[] GetByteArrayFromIndex(int index)
    {
        var offset = metadataHeader.@string.Offset + index;
        var count = ReadUnityCompressedUIntAtRawAddr(offset, out var bytesRead);
        return ReadByteArrayAtRawAddress(offset + bytesRead, (int)count);
    }

    private ConcurrentDictionary<int, string> _cachedStrings = new ConcurrentDictionary<int, string>();

    public string GetStringFromIndex(int index)
    {
        GetLockOrThrow();
        try
        {
            return ReadStringFromIndexNoReadLock(index);
        }
        finally
        {
            ReleaseLock();
        }
    }

    internal string ReadStringFromIndexNoReadLock(int index)
    {
        if (!_cachedStrings.ContainsKey(index))
            _cachedStrings[index] = ReadStringToNullNoLock(metadataHeader.@string.Offset + index);
        return _cachedStrings[index];
    }

    public Il2CppCustomAttributeTypeRange? GetCustomAttributeData(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, out int idx)
    {
        if(MetadataVersion >= 29f)
            throw new("This method is not valid for metadata versions 29 and above");
        
        idx = -1;

        if (MetadataVersion <= 24f)
        {
            idx = customAttributeIndex;
            return attributeTypeRanges![customAttributeIndex]; 
        }

        var target = new Il2CppCustomAttributeTypeRange { token = token };

        if (imageDef.customAttributeStart < 0)
            throw new("Image has customAttributeStart < 0");
        if (imageDef.customAttributeStart + imageDef.customAttributeCount > attributeTypeRanges!.Count) 
            throw new($"Image has customAttributeStart + customAttributeCount > attributeTypeRanges.Count ({imageDef.customAttributeStart + imageDef.customAttributeCount} > {attributeTypeRanges.Count})");

        idx = attributeTypeRanges.BinarySearch(imageDef.customAttributeStart, (int)imageDef.customAttributeCount, target, new TokenComparer());

        return idx < 0 ? null : attributeTypeRanges[idx];
    }
    
    public Il2CppTypeDefinition GetTypeDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppTypeDefinition> index) => typeDefs[index.Value];
    
    public Il2CppGenericContainer GetGenericContainerFromIndex(Il2CppVariableWidthIndex<Il2CppGenericContainer> index) => genericContainers[index.Value];
    
    public Il2CppParameterDefinition GetParameterDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppParameterDefinition> index) => parameterDefs[index.Value];
    
    public IEnumerable<int> GetNestedTypeIndicesFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppNestedTypeIndex> index, int count) => nestedTypeIndices.Skip(index.Value).Take(count).Select(n => n.Value);

    public IEnumerable<Il2CppVariableWidthIndex<Il2CppType>> GetInterfaceIndicesFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppInterfaceOffset> index, int count) => interfaceIndices.Skip(index.Value).Take(count);

    public Il2CppInterfaceOffset[] GetInterfaceOffsetsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppInterfaceOffset> index, int count) => interfaceOffsets.SubArray(index.Value, count);
    
    public Il2CppPropertyDefinition[] GetPropertyDefinitionsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppPropertyDefinition> index, int count) => propertyDefs.SubArray(index.Value, count);
    
    public Il2CppEventDefinition[] GetEventDefinitionsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppEventDefinition> index, int count) => eventDefs.SubArray(index.Value, count);
    
    public Il2CppMethodDefinition GetMethodDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppMethodDefinition> index) => methodDefs[index.Value];
    
    public Il2CppMethodDefinition[] GetMethodDefinitionsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppMethodDefinition> index, int count) => methodDefs.SubArray(index.Value, count);
    
    public Il2CppGenericParameter GetGenericParameterFromIndex(Il2CppVariableWidthIndex<Il2CppGenericParameter> index)
    {
        if ((uint)index.Value >= (uint)genericParameters.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index.Value,
                $"Generic-parameter index is outside the recovered metadata table (count {genericParameters.Length}).");

        return genericParameters[index.Value];
    }
    
    public Il2CppFieldDefinition GetFieldDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppFieldDefinition> index) => fieldDefs[index.Value];
    public Il2CppFieldDefinition[] GetFieldDefinitionsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppFieldDefinition> index, int count) => fieldDefs.SubArray(index.Value, count);

    public string GetStringLiteralFromIndex(uint index)
    {
        var stringLiteral = stringLiterals[index];
        
        if(MetadataVersion < 35f)
            return Encoding.UTF8.GetString(ReadByteArrayAtRawAddress(metadataHeader.stringLiteralData.Offset + stringLiteral.dataIndex, (int)stringLiteral.length));
        
        
        var nextOffset = index < stringLiterals.Length - 1 
            ? metadataHeader.stringLiteralData.Offset + stringLiterals[index + 1].dataIndex 
            : metadataHeader.stringLiteralData.Offset + metadataHeader.stringLiteralData.Size;
        
        var startOffset = metadataHeader.stringLiteralData.Offset + stringLiteral.dataIndex;
        var length = nextOffset - startOffset;
        return Encoding.UTF8.GetString(ReadByteArrayAtRawAddress(startOffset, length));
    }
}
