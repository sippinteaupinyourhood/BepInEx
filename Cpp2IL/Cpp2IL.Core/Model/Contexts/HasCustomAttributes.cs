using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.CustomAttributes;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;




public abstract class HasCustomAttributes(uint token, ApplicationAnalysisContext appContext)
    : HasToken(token, appContext)
{
    private bool _hasAnalyzedCustomAttributeData;
    private bool _hasInitCustomAttributeData;

    
    
    
    public Memory<byte> RawIl2CppCustomAttributeData = Memory<byte>.Empty;

    
    
    
    public List<AnalyzedCustomAttribute>? CustomAttributes;

    
    
    
    
    
    public Il2CppCustomAttributeTypeRange? AttributeTypeRange;

    
    
    
    
    
    public List<Il2CppType>? AttributeTypes;

    
    
    
    
    
    public AttributeGeneratorMethodAnalysisContext? CaCacheGeneratorAnalysis;

    
    
    
    protected abstract int CustomAttributeIndex { get; }

    
    
    
    public abstract AssemblyAnalysisContext CustomAttributeAssembly { get; }

    
    
    
    protected virtual bool IsInjected => false;

    
    
    
    private int Pre29RangeIndex = -1;

    public bool IsCompilerGeneratedBasedOnCustomAttributes => HasCustomAttributeWithFullName("System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    public bool HasCustomAttributeWithFullName(string fullName)
    {
        return CustomAttributes?.Any(a => a.Constructor.DeclaringType!.FullName == fullName)
            ?? AttributeTypes?.Any(t => t.Type == Il2CppTypeEnum.IL2CPP_TYPE_CLASS && t.AsClass().FullName == fullName)
            ?? false;
    }


#pragma warning disable CS8618 
#pragma warning restore CS8618

    protected void InitCustomAttributeData()
    {
        if(IsInjected)
            return;
        
        _hasInitCustomAttributeData = true;
        if (AppContext.MetadataVersion >= 29)
        {
            var offsets = GetV29BlobOffsets();

            if (!offsets.HasValue)
                return;

            var (blobStart, blobEnd) = offsets.Value;
            RawIl2CppCustomAttributeData = AppContext.Metadata.ReadByteArrayAtRawAddress(blobStart, (int)(blobEnd - blobStart));

            return;
        }

        if (CustomAttributeAssembly.Definition is null)
            return;

        AttributeTypeRange = AppContext.Metadata.GetCustomAttributeData(CustomAttributeAssembly.Definition.Image, CustomAttributeIndex, Token, out Pre29RangeIndex);

        if (AttributeTypeRange == null || AttributeTypeRange.count == 0)
        {
            RawIl2CppCustomAttributeData = Array.Empty<byte>();
            AttributeTypes = [];
            return; 
        }

        AttributeTypes = Enumerable.Range(AttributeTypeRange.start, AttributeTypeRange.count)
            .Select(attrIdx => AppContext.Metadata!.attributeTypes![attrIdx]) 
            .Select(typeIdx => AppContext.Binary!.GetType(Il2CppVariableWidthIndex<Il2CppType>.MakeTemporaryForFixedWidthUsage(typeIdx)))
            .ToList();
    }

    private (long blobStart, long blobEnd)? GetV29BlobOffsets()
    {
        if (CustomAttributeAssembly.Definition is null)
            return null;

        var ranges = AppContext.Metadata.AttributeDataRanges;
        var rangeStart = CustomAttributeAssembly.Definition.Image.customAttributeStart;
        var rangeCount = (int)CustomAttributeAssembly.Definition.Image.customAttributeCount;
        if (ranges is null || rangeStart < 0 || rangeCount <= 0 || rangeStart > ranges.Count - rangeCount)
        {
            RawIl2CppCustomAttributeData = Array.Empty<byte>();
            return null;
        }

        var target = new Il2CppCustomAttributeDataRange() { token = Token };
        var caIndex = ranges.BinarySearch
        (
            rangeStart,
            rangeCount,
            target,
            new TokenComparer()
        );

        if (caIndex < 0)
        {
            RawIl2CppCustomAttributeData = Array.Empty<byte>();
            return null;
        }

        if (caIndex + 1 >= ranges.Count)
            return null;

        var attributeDataRange = ranges[caIndex];
        var next = ranges[caIndex + 1];

        var blobStart = AppContext.Metadata.metadataHeader.attributeData.Offset + attributeDataRange.startOffset;
        var blobEnd = AppContext.Metadata.metadataHeader.attributeData.Offset + next.startOffset;
        return (blobStart, blobEnd);
    }

    private void InitPre29AttributeGeneratorAnalysis(int rangeIndex)
    {
        ulong generatorPtr;
        if (AppContext.MetadataVersion < 27)
        {
            if (rangeIndex < 0)
            {
                RawIl2CppCustomAttributeData = Array.Empty<byte>();
                return;
            }

            try
            {
                generatorPtr = AppContext.Binary.GetCustomAttributeGenerator(rangeIndex);
            }
            catch (IndexOutOfRangeException)
            {
                Logger.WarnNewline("Custom attribute generator out of range for " + this, "CA Restore");
                RawIl2CppCustomAttributeData = Array.Empty<byte>();
                return;
            }
        }
        else
        {
            if(AttributeTypeRange == null || AttributeTypeRange.count == 0 || CustomAttributeAssembly.Definition is null)
            {
                RawIl2CppCustomAttributeData = Array.Empty<byte>();
                return;
            }
            
            var baseAddress = CustomAttributeAssembly.CodeGenModule!.customAttributeCacheGenerator;
            var relativeIndex = rangeIndex - CustomAttributeAssembly.Definition.Image.customAttributeStart;
            var ptrToAddress = baseAddress + (ulong)relativeIndex * AppContext.Binary.PointerSize;
            generatorPtr = AppContext.Binary.ReadPointerAtVirtualAddress(ptrToAddress);
        }

        if (generatorPtr == 0 || !AppContext.Binary.TryMapVirtualAddressToRaw(generatorPtr, out _))
        {
            Logger.WarnNewline($"Supposedly had custom attributes ({string.Join(", ", AttributeTypes ?? [])}), but generator was null for " + this, "CA Restore");
            RawIl2CppCustomAttributeData = Memory<byte>.Empty;
            return;
        }

        CaCacheGeneratorAnalysis = new(generatorPtr, AppContext, this);
        RawIl2CppCustomAttributeData = CaCacheGeneratorAnalysis.RawBytes;
    }

    
    
    
    public void AnalyzeCustomAttributeData(bool allowAnalysis = true)
    {
        if (_hasAnalyzedCustomAttributeData)
            return;
        
        if(IsInjected)
            return;
        
        if(!_hasInitCustomAttributeData)
            throw new($"Must call InitCustomAttributeData before AnalyzeCustomAttributeData on {this}");

        _hasAnalyzedCustomAttributeData = true;

        CustomAttributes = [];

        if (AppContext.MetadataVersion >= 29)
        {
            AnalyzeCustomAttributeDataV29();
            return;
        }
        
        InitPre29AttributeGeneratorAnalysis(Pre29RangeIndex);

        if (RawIl2CppCustomAttributeData.Length == 0)
            return;

        if (allowAnalysis)
        {
            try
            {
                CaCacheGeneratorAnalysis!.Analyze();
            }
            catch (Exception e)
            {
                Logger.WarnNewline("Failed to analyze custom attribute cache generator for " + this + " because " + e.Message, "CA Restore");
                return;
            }
        }

        

        foreach (var il2CppType in AttributeTypes ?? []) 
        {
            var typeDef = il2CppType.AsClass();
            var attributeTypeContext = AppContext.ResolveContextForType(typeDef) ?? throw new("Unable to find type " + typeDef.FullName);

            AnalyzedCustomAttribute attribute;
            if (attributeTypeContext.Methods.FirstOrDefault(c => c.Name == ".ctor" && c.Definition!.parameterCount == 0) is { } constructor)
            {
                attribute = new(constructor);
            }
            else if (attributeTypeContext.Methods.FirstOrDefault(c => c.Name == ".ctor") is { } anyConstructor)
            {
                
                attribute = new(anyConstructor);
            }
            else
                
                continue;

            
            CustomAttributes.Add(attribute);
        }
    }

    
    
    
    private void AnalyzeCustomAttributeDataV29()
    {
        if (RawIl2CppCustomAttributeData.Length == 0)
            return;

        using var blobStream = new MemoryStream(RawIl2CppCustomAttributeData.ToArray());
        var attributeCount = blobStream.ReadUnityCompressedUint();
        var constructors = V29AttributeUtils.ReadConstructors(blobStream, attributeCount, AppContext);

        
        var startOfData = blobStream.Position;
        var perAttributeStartOffsets = new Dictionary<Il2CppMethodDefinition, long>();

        CustomAttributes = [];
        foreach (var constructor in constructors)
        {
            perAttributeStartOffsets[constructor] = blobStream.Position;

            var attributeTypeContext = AppContext.ResolveContextForType(constructor.DeclaringType!) ?? throw new($"Unable to find type {constructor.DeclaringType!.FullName}");
            var attributeMethodContext = attributeTypeContext.GetMethod(constructor) ?? throw new($"Unable to find method {constructor.Name} in type {attributeTypeContext.Definition?.FullName}");

            try
            {
                CustomAttributes.Add(V29AttributeUtils.ReadAttribute(blobStream, attributeMethodContext, AppContext));
            }
            catch (Exception e)
            {
                Logger.ErrorNewline($"Failed to read attribute data for {constructor}, which has parameters {string.Join(", ", constructor.Parameters!.Select(p => p.Type))}", "CA Restore");
                Logger.ErrorNewline($"This member ({ToString()}) has {RawIl2CppCustomAttributeData.Length} bytes of data starting at 0x{GetV29BlobOffsets()!.Value.blobStart:X}", "CA Restore");
                Logger.ErrorNewline($"The post-constructor data started at 0x{startOfData:X} bytes into our blob", "CA Restore");
                Logger.ErrorNewline($"Data for this constructor started at 0x{perAttributeStartOffsets[constructor]:X} bytes into our blob, we are now 0x{blobStream.Position:X} bytes into the blob", "CA Restore");
                Logger.ErrorNewline($"The exception message was {e.Message}", "CA Restore");

                throw;
            }
        }
    }
}
