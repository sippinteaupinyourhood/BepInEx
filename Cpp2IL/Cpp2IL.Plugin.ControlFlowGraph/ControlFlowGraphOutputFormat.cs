using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Extensions;
using System.Text;
using Cpp2IL.Core.Graphs;
using DotNetGraph.Core;
using DotNetGraph.Extensions;
using DotNetGraph.Compilation;

namespace Cpp2IL.Plugin.ControlFlowGraph;

public class ControlFlowGraphOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "cfg";
    public override string OutputFormatName => "CFG Dump";

    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        outputRoot = Path.Combine(outputRoot, "CFGDump");

        var numAssemblies = context.Assemblies.Count;
        var i = 1;
        foreach (var assembly in context.Assemblies.Where(a => a.Definition is not null))
        {
            Logger.InfoNewline($"Processing assembly {i++} of {numAssemblies}: {assembly.Name}", "ControlFlowGraphOutputFormat");

            var assemblyNameClean = assembly.CleanAssemblyName;

            MiscUtils.ExecuteParallel(assembly.Types, type =>
            {
                if (type is InjectedTypeAnalysisContext)
                    return;

                if (type.Methods.Count == 0)
                    return;

                foreach (var method in type.Methods)
                {
                    if (method is InjectedMethodAnalysisContext)
                        continue;

                    try
                    {
                        method.Analyze();

                        if (method.ControlFlowGraph == null)
                        {
                            continue;
                        }

                        WriteMethodGraph(outputRoot, method, GenerateGraph(method.ControlFlowGraph, method), assemblyNameClean).Wait();

                        method.ReleaseAnalysisData();
                    }
                    catch (Exception e)
                    {
                        Logger.ErrorNewline("Method threw an exception while dumping - " + e.ToString());
                    }
                }
            });
        }
    }

    private string GenerateGraphTitle(MethodAnalysisContext context)
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append("Type: ");
        stringBuilder.Append(context.DeclaringType?.FullName);
        stringBuilder.Append("\n");
        stringBuilder.Append("Method: ");
        stringBuilder.Append(context.DefaultName);
        stringBuilder.Append("\n");
        return stringBuilder.ToString();
    }

    public DotGraph GenerateGraph(ISILControlFlowGraph graph, MethodAnalysisContext method)
    {
        var directedGraph = new DotGraph()
            .WithIdentifier("ControlFlowGraph")
            .Directed()
            .WithLabel(GenerateGraphTitle(method));

        var nodeCache = new Dictionary<int, DotNode>();
        var edgeCache = new List<DotEdge>();

        DotNode GetOrAddNode(int id)
        {
            if (nodeCache.TryGetValue(id, out var node))
            {
                return node;
            }

            var newNode = new DotNode().WithIdentifier(id.ToString());
            directedGraph.Add(newNode);
            nodeCache[id] = newNode;
            return newNode;
        }

        DotEdge GetOrAddEdge(DotNode from, DotNode to)
        {
            foreach (var edge in edgeCache)
            {
                if (edge.From == from.Identifier && edge.To == to.Identifier) { return edge; }
            }

            var newEdge = new DotEdge()
                .From(from)
                .To(to);
            edgeCache.Add(newEdge);
            directedGraph.Add(newEdge);
            return newEdge;
        }


        foreach (var block in graph.Blocks)
        {
            var node = GetOrAddNode(block.ID);
            if (block.BlockType == BlockType.Entry)
            {
                node.WithColor("green");
                node.WithLabel("Entry point");
            }
            else if (block.BlockType == BlockType.Exit)
            {
                node.WithColor("red");
                node.WithLabel("Exit point");
            }
            else
            {
                node.WithShape("box");
                node.WithLabel(block.ToString());
            }

            foreach (var succ in block.Successors)
            {
                var target = GetOrAddNode(succ.ID);
                GetOrAddEdge(node, target);
            }
        }

        return directedGraph;
    }

    private static string GetFilePathForMethod(string outputRoot, MethodAnalysisContext method, string assemblyNameClean)
    {
        TypeAnalysisContext type = method.DeclaringType!;


        
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
            filename = $"{string.Join("_NestedType_", declaringTypeHierarchy)}_NestedType_{type.Name}";
        else
            filename = $"{type.Name}";

        
        var directory = Path.Combine(namespaceSplit.Prepend(assemblyDir).ToArray());
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        
        MiscUtils.InvalidPathChars.ForEach(c => filename = filename.Replace(c, '_'));

        filename = Path.Combine(directory, filename);

        if (!Directory.Exists(filename))
            Directory.CreateDirectory(filename);


        var methodFileName = method.DefaultName;

        var parameters = string.Join("_", method.Parameters.Select(p => p.ParameterType.Name));
        if (parameters.Length > 0)
        {
            methodFileName += "_";
            methodFileName += parameters;
        }

        methodFileName += ".dot";

        MiscUtils.InvalidPathChars.ForEach(c => methodFileName = methodFileName.Replace(c, '_'));


        
        var fullPath = Path.Combine(filename, methodFileName);

        if (fullPath.Length > 260)
        {
            
            fullPath = fullPath[..250];

            fullPath += ".dot";
        }

        return fullPath;
    }

    private static async Task WriteMethodGraph(string outputRoot, MethodAnalysisContext method, DotGraph graph, string assemblyNameClean)
    {
        var file = GetFilePathForMethod(outputRoot, method, assemblyNameClean);

        
        await using var writer = new StringWriter();
        var context = new CompilationContext(writer, new CompilationOptions());
        await graph.CompileAsync(context);
        var result = writer.GetStringBuilder().ToString();

        
        File.WriteAllText(file, result);
    }
}
