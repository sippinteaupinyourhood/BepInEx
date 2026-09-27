using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
using LibCpp2IL.Wasm;

namespace Cpp2IL.Core.Utils;

public static class WasmUtils
{
    internal static readonly Dictionary<int, List<Il2CppMethodDefinition>> MethodDefinitionIndices = new();
    private static Regex DynCallRemappingRegex = new(@"Module\[\s*[""'](dynCall_[^""']+)[""']\s*\]\s*=\s*Module\[\s*[""']asm[""']\s*\]\[\s*[""']([^""']+)[""']\s*\]\s*\)\.apply", RegexOptions.Compiled);

    public static string BuildSignature(MethodAnalysisContext definition)
    {
        var instanceParam = definition.IsStatic ? "" : "i";

        
        
        
        
        

        var returnTypeSignature = definition.ReturnType.IsWasmPrimitive()
            ? GetSignatureLetter(definition.ReturnType)
            : definition.ReturnType switch
            {
                { Namespace: nameof(System), Name: "Void" } => "v",
                { IsValueType: true, Definition: null or { Size: < 0 or > 8 } } => "vi", 
                { IsValueType: true, Definition.Size: > 4 } => "j", 
                { IsValueType: true, Definition.Size: <= 4 } => "i", 
                _ => GetSignatureLetter(definition.ReturnType!)
            };

        return $"{returnTypeSignature}{instanceParam}{string.Join("", definition.Parameters!.Select(p => GetSignatureLetter(p.ParameterType, p.IsRef)))}i"; 
    }

    public static bool IsWasmPrimitive(this TypeAnalysisContext type)
    {
        var typeEnum = type.Type;

        
        return typeEnum is >= Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN and <= Il2CppTypeEnum.IL2CPP_TYPE_R8;
    }

    private static string GetSignatureLetter(TypeAnalysisContext type, bool isRefOrOut = false)
    {
        if (isRefOrOut)
            
            return "i";

        if (type is WrappedTypeAnalysisContext)
            
            return "i";

        if (type.IsEnumType)
            type = type.EnumUnderlyingType ?? throw new($"Enum type {type} has no underlying type");

        

        return type.Name switch
        {
            "Void" => "v",
            "Int64" => "j",
            "Single" => "f",
            "Double" => "d",
            "Int32" => "i",
            _ when !type.IsWasmPrimitive() && type is { IsValueType: true, IsEnumType: false, Definition.Size: <= 8 and > 0 } => "j", 
            _ => "i"
        };
    }

    public static string GetGhidraFunctionName(WasmFunctionDefinition functionDefinition)
    {
        var index = functionDefinition.IsImport
            ? ((WasmFile)LibCpp2IlMain.Binary!).FunctionTable.IndexOf(functionDefinition)
            : functionDefinition.FunctionTableIndex;

        return $"unnamed_function_{index}";
    }

    public static WasmFunctionDefinition? TryGetWasmDefinition(MethodAnalysisContext definition)
    {
        try
        {
            return GetWasmDefinition(definition);
        }
        catch
        {
            return null;
        }
    }

    public static WasmFunctionDefinition GetWasmDefinition(MethodAnalysisContext context)
    {
        if (context.Definition == null)
            throw new($"Attempted to get wasm definition for probably-injected method context: {context}");
        
        
        var signature = BuildSignature(context);
        try
        {
            return ((WasmFile)LibCpp2IlMain.Binary!).GetFunctionFromIndexAndSignature(context.Definition.MethodPointer, signature);
        }
        catch (Exception e)
        {
            throw new($"Failed to find wasm definition for {context}\nwhich has params {context.Parameters?.ToStringEnumerable()}", e);
        }
    }

    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    

    public static Dictionary<string, string> ExtractAndParseDynCallRemaps(string frameworkJsFile)
    {
        
        
        





        var ret = new Dictionary<string, string>();
        var matches = DynCallRemappingRegex.Matches(frameworkJsFile);
        foreach (Match match in matches)
        {
            
            
            var origName = match.Groups[1];
            var remappedName = match.Groups[2];

            ret[remappedName.Value] = origName.Value;
        }

        return ret;
    }
}
