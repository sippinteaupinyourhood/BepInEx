using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.OutputFormats;

public class IsilDumpOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "isil";
    public override string OutputFormatName => "ISIL Dump";

    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        outputRoot = Path.Combine(outputRoot, "IsilDump");

        var numAssemblies = context.Assemblies.Count;
        var i = 1;
        foreach (var assembly in context.Assemblies)
        {
            Logger.InfoNewline($"Processing assembly {i++} of {numAssemblies}: {assembly.Name}", "IsilOutputFormat");

            var assemblyNameClean = assembly.CleanAssemblyName;

            MiscUtils.ExecuteParallel(assembly.Types, type =>
            {
                if (type is InjectedTypeAnalysisContext)
                    return;

                if (type.Methods.Count == 0)
                    return;

                var typeDump = new StringBuilder();

                typeDump.Append("Type: ").AppendLine(type.Definition!.FullName).AppendLine();

                foreach (var method in type.Methods)
                {
                    if (method is InjectedMethodAnalysisContext)
                        continue;

                    typeDump.Append("Method: ").AppendLine(method.Definition!.HumanReadableSignature).AppendLine();

                    try
                    {
                        typeDump.AppendLine("Disassembly:");
                        typeDump.Append('\t').AppendLine(context.InstructionSet.PrintAssembly(method).Replace("\n", "\n\t"));

                        typeDump.AppendLine().AppendLine("ISIL:");

                        method.Analyze();

                        if (method.ConvertedIsil == null || method.ConvertedIsil.Count == 0)
                        {
                            typeDump.AppendLine("No ISIL was generated");
                            continue;
                        }

                        foreach (var isilInsn in method.ConvertedIsil)
                        {
                            typeDump.Append('\t').Append(isilInsn).AppendLine();
                        }

                        method.ReleaseAnalysisData();

                        typeDump.AppendLine();
                    }
                    catch (Exception e)
                    {
                        typeDump.Append("Method threw an exception while analyzing - ").AppendLine(e.ToString()).AppendLine();
                    }
                }

                WriteTypeDump(outputRoot, type, typeDump.ToString(), assemblyNameClean);
            });
        }
    }

    private static string GetFilePathForType(string outputRoot, TypeAnalysisContext type, string assemblyNameClean)
    {
        
        var assemblyDir = Path.Combine(outputRoot, assemblyNameClean);

        
        
        var ultimateDeclaringType = type;
        while (ultimateDeclaringType.DeclaringType != null)
            ultimateDeclaringType = ultimateDeclaringType.DeclaringType;

        var namespaceSplit = ultimateDeclaringType.Namespace!.Split('.');
        namespaceSplit = namespaceSplit
            .Peek(n => MiscUtils.InvalidPathChars.ForEach(c => n = n.Replace(c, '_')))
            .Select(n => MiscUtils.InvalidPathElements.Contains(n) ? $"__illegalwin32name_{n}__" : n)
            .ToArray();

        
        var declaringTypeHierarchy = new List<string>();
        var declaringType = type.DeclaringType;
        while (declaringType != null)
        {
            declaringTypeHierarchy.Add(declaringType.Name!);
            declaringType = declaringType.DeclaringType;
        }

        
        declaringTypeHierarchy.Reverse();

        
        string filename;
        if (declaringTypeHierarchy.Count > 0)
            filename = $"{string.Join("_NestedType_", declaringTypeHierarchy)}_NestedType_{type.Name}.txt";
        else
            filename = $"{type.Name}.txt";

        
        var directory = Path.Combine(namespaceSplit.Prepend(assemblyDir).ToArray());
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        
        MiscUtils.InvalidPathChars.ForEach(c => filename = filename.Replace(c, '_'));

        
        return Path.Combine(directory, filename);
    }

    private static void WriteTypeDump(string outputRoot, TypeAnalysisContext type, string typeDump, string assemblyNameClean)
    {
        var file = GetFilePathForType(outputRoot, type, assemblyNameClean);
        File.WriteAllText(file, typeDump);
    }
}
