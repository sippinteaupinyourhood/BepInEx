using System;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Wasm;

namespace Cpp2IL.Core.OutputFormats;

public class WasmNameSectionOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "wasm_name_section";
    public override string OutputFormatName => "Webassembly name section";

    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        
        
        
        

        
        
        

        if (context.Binary.GetType() != typeof(WasmFile))
            throw new Exception("This output format only works with WebAssembly files");

        if (!Directory.Exists(outputRoot))
            Directory.CreateDirectory(outputRoot);

        var outputFile = File.Create(Path.Combine(outputRoot, "namesection.dat"));

        var section =
            new MemoryStream(); 
        section.WriteName("name"); 

        var moduleNameSubsection = new MemoryStream();
        moduleNameSubsection.WriteName("Unity");
        section.WriteSizedData(moduleNameSubsection, 0x0); 
        moduleNameSubsection.Dispose();

        var paramSuccessCount = 0;
        var paramFailCount = 0;

        var functionData = context.AllTypes.SelectMany(t => t.Methods)
            .Select(method => (method, definition: WasmUtils.TryGetWasmDefinition(method)))
            .Where(v => v.definition is not null)
            .Select(v =>
            {
                var trueParamCount = v.definition!.GetType((WasmFile)LibCpp2IlMain.Binary!).ParamTypes.Length;

                
                var parameters = v.method.Parameters.Select(param => param.Name).ToList();

                if (!v.method.IsStatic)
                    parameters.Insert(0, "this");

                if (v.method.ReturnType is { IsValueType: true, Definition: null or { Size: > 8 } } rt && rt.IsWasmPrimitive()) 
                    parameters.Insert(0, "out");

                parameters.Add("methodInfo"); 

                if (trueParamCount != parameters.Count)
                {
                    
                    parameters.Clear();
                    paramFailCount++;
                }
                else
                {
                    paramSuccessCount++;
                }

                return (
                    Index: v.definition!.FunctionTableIndex,
                    Name: v.method.FullName,
                    Parameters: parameters
                );
            })
            .GroupBy(v => v.Index)
            .OrderBy(grouping => grouping.Key)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping.Select(v => (v.Name, v.Parameters)).ToList()
            );
        
        Logger.InfoNewline(
            $"Estimated parameter naming success rate: {(float)paramSuccessCount / (paramSuccessCount + paramFailCount) * 100:N2}%");

        var functionNameSubsection = new MemoryStream();
        functionNameSubsection.WriteLEB128Unsigned((ulong)functionData.Count); 

        var localNameSubsection = new MemoryStream();
        localNameSubsection.WriteLEB128Unsigned((ulong)functionData.Count); 

        foreach (var (idx, data) in functionData)
        {
            functionNameSubsection.WriteLEB128Unsigned((ulong)idx);
            localNameSubsection.WriteLEB128Unsigned((ulong)idx);

            functionNameSubsection.WriteName(data.Count == 1 ? data[0].Name : $"multiple_{data.Count}_{data[0].Name}");

            localNameSubsection.WriteLEB128Unsigned((ulong)data[0].Parameters.Count); 
            for (var i = 0; i < data[0].Parameters.Count; i++)
            {
                localNameSubsection.WriteLEB128Unsigned((ulong)i);
                
                localNameSubsection.WriteName(data.Count == 1
                    ? data[0].Parameters[i]
                    : $"multiple_{data.Count}_{data[0].Parameters[i]}");
            }
        }

        section.WriteSizedData(functionNameSubsection, 0x1); 
        section.WriteSizedData(localNameSubsection, 0x2); 

        outputFile.WriteSizedData(section, 0x0); 
    }
}

public static class Extensions
{
    public static void WriteName(this Stream memoryStream, string name)
    {
        var bytes = Encoding.Default.GetBytes(name);
        memoryStream.WriteLEB128Unsigned((ulong)bytes.Length);
        memoryStream.Write(bytes, 0, bytes.Length);
    }

    public static void WriteSizedData(this Stream memoryStream, MemoryStream data, byte? prependByte = null)
    {
        if (prependByte.HasValue)
        {
            memoryStream.WriteByte(prependByte.Value);
        }

        memoryStream.WriteLEB128Unsigned((ulong)data.Length);
        data.WriteTo(memoryStream);
    }
}
