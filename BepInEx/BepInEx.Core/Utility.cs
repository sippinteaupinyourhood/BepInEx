using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using Mono.Cecil;

namespace BepInEx;




public static class Utility
{
    private const string TRUSTED_PLATFORM_ASSEMBLIES = "TRUSTED_PLATFORM_ASSEMBLIES";
    private static bool? sreEnabled;

    
    
    
    
    public static bool CLRSupportsDynamicAssemblies => CheckSRE();

    
    
    
    public static Encoding UTF8NoBom { get; } = new UTF8Encoding(false);

    private static bool CheckSRE()
    {
        try
        {
            if (sreEnabled.HasValue)
                return sreEnabled.Value;

            
            _ = new CustomAttributeBuilder(null, new object[0]);
        }
        catch (PlatformNotSupportedException)
        {
            sreEnabled = false;
            return sreEnabled.Value;
        }
        catch (ArgumentNullException)
        {
            
        }

        sreEnabled = true;
        return sreEnabled.Value;
    }

    
    
    
    
    
    
    public static bool TryDo(Action action, out Exception exception)
    {
        exception = null;
        try
        {
            action();
            return true;
        }
        catch (Exception e)
        {
            exception = e;
            return false;
        }
    }

    
    
    
    
    
    public static string CombinePaths(params string[] parts) => parts.Aggregate(Path.Combine);

    
    
    
    
    
    
    public static string ParentDirectory(string path, int levels = 1)
    {
        for (var i = 0; i < levels; i++)
            path = Path.GetDirectoryName(path);

        return path;
    }

    
    
    
    
    
    
    public static bool SafeParseBool(string input, bool defaultValue = false) =>
        bool.TryParse(input, out var result) ? result : defaultValue;

    
    
    
    
    
    public static string ConvertToWWWFormat(string path) => $"file://{path.Replace('\\', '/')}";

    
    
    
    
    
    public static bool IsNullOrWhiteSpace(this string self) => self == null || self.All(char.IsWhiteSpace);

    
    
    
    
    
    
    
    
    public static IEnumerable<TNode> TopologicalSort<TNode>(IEnumerable<TNode> nodes,
                                                            Func<TNode, IEnumerable<TNode>> dependencySelector)
    {
        var sorted_list = new List<TNode>();

        var visited = new HashSet<TNode>();
        var sorted = new HashSet<TNode>();

        foreach (var input in nodes)
        {
            var currentStack = new Stack<TNode>();
            if (!Visit(input, currentStack))
                throw new Exception("Cyclic Dependency:\r\n" + currentStack.Select(x => $" - {x}") 
                                                                           .Aggregate((a, b) =>
                                                                               $"{a}\r\n{b}")); 
        }


        return sorted_list;

        bool Visit(TNode node, Stack<TNode> stack)
        {
            if (visited.Contains(node))
            {
                if (!sorted.Contains(node)) return false;
            }
            else
            {
                visited.Add(node);
                stack.Push(node);
                if (dependencySelector(node).Any(dep => !Visit(dep, stack)))
                    return false;

                sorted.Add(node);
                sorted_list.Add(node);

                stack.Pop();
            }

            return true;
        }
    }

    
    
    
    
    
    
    
    public static bool TryResolveDllAssembly<T>(AssemblyName assemblyName,
                                                string directory,
                                                Func<string, T> loader,
                                                out T assembly) where T : class
    {
        assembly = null;

        var potentialDirectories = new List<string> { directory };

        if (!Directory.Exists(directory))
            return false;

        potentialDirectories.AddRange(Directory.GetDirectories(directory, "*", SearchOption.AllDirectories));

        foreach (var subDirectory in potentialDirectories)
        {
            var potentialPaths = new[]
            {
                $"{assemblyName.Name}.dll",
                $"{assemblyName.Name}.exe"
            };

            foreach (var potentialPath in potentialPaths)
            {
                var path = Path.Combine(subDirectory, potentialPath);

                if (!File.Exists(path))
                    continue;

                try
                {
                    assembly = loader(path);
                }
                catch (Exception)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    
    
    
    
    
    
    public static bool IsSubtypeOf(this TypeDefinition self, Type td)
    {
        if (self.FullName == td.FullName)
            return true;
        return self.FullName != "System.Object" && (self.BaseType?.Resolve()?.IsSubtypeOf(td) ?? false);
    }

    
    
    
    
    
    
    
    public static bool TryResolveDllAssembly(AssemblyName assemblyName, string directory, out Assembly assembly) =>
        TryResolveDllAssembly(assemblyName, directory, Assembly.LoadFrom, out assembly);

    
    
    
    
    
    
    
    
    public static bool TryResolveDllAssembly(AssemblyName assemblyName,
                                             string directory,
                                             ReaderParameters readerParameters,
                                             out AssemblyDefinition assembly) =>
        TryResolveDllAssembly(assemblyName, directory,
                              s => AssemblyDefinition.ReadAssembly(s, readerParameters), out assembly);

    
    
    
    
    
    
    
    
    
    public static bool TryOpenFileStream(string path,
                                         FileMode mode,
                                         out FileStream fileStream,
                                         FileAccess access = FileAccess.ReadWrite,
                                         FileShare share = FileShare.Read)
    {
        try
        {
            fileStream = new FileStream(path, mode, access, share);

            return true;
        }
        catch (IOException)
        {
            fileStream = null;

            return false;
        }
    }

    public static IEnumerable<MethodDefinition> EnumerateAllMethods(this TypeDefinition type)
    {
        var currentType = type;

        while (currentType != null)
        {
            foreach (var method in currentType.Methods)
                yield return method;

            currentType = currentType.BaseType?.Resolve();
        }
    }

    
    
    
    
    
    public static string HashStream(Stream stream)
    {
        using var md5 = MD5.Create();

        var buf = new byte[4096];
        int read;
        while ((read = stream.Read(buf, 0, buf.Length)) > 0)
            md5.TransformBlock(buf, 0, read, buf, 0);

        md5.TransformFinalBlock(new byte[0], 0, 0);

        return ByteArrayToString(md5.Hash);
    }

    
    
    
    
    
    public static string HashStrings(params string[] strings)
    {
        using var md5 = MD5.Create();

        foreach (var str in strings)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            md5.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        md5.TransformFinalBlock(new byte[0], 0, 0);

        return ByteArrayToString(md5.Hash);
    }

    
    
    
    
    
    public static string ByteArrayToString(byte[] data)
    {
        var builder = new StringBuilder(data.Length * 2);

        foreach (var b in data)
            builder.AppendFormat("{0:x2}", b);

        return builder.ToString();
    }

    
    
    
    
    
    public static string GetCommandLineArgValue(string arg)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length; i++)
            if (args[i] == arg && i + 1 < args.Length)
                return args[i + 1];
        return null;
    }

    
    
    
    
    
    
    
    
    
    
    
    public static bool TryParseAssemblyName(string fullName, out AssemblyName assemblyName)
    {
        try
        {
            assemblyName = new AssemblyName(fullName);
            return true;
        }
        catch (Exception)
        {
            assemblyName = null;
            return false;
        }
    }

    internal static void AddCecilPlatformAssemblies(this AppDomain appDomain, string assemblyDir)
    {
        if (!Directory.Exists(assemblyDir))
            return;
        
        var curTrusted = appDomain.GetData(TRUSTED_PLATFORM_ASSEMBLIES) as string;
        var addTrusted = string.Join(Path.PathSeparator.ToString(),
                                     Directory.GetFiles(assemblyDir, "*.dll",
                                                        SearchOption.TopDirectoryOnly));
        var newTrusted = curTrusted == null ? addTrusted : $"{curTrusted}{Path.PathSeparator}{addTrusted}";
        appDomain.SetData(TRUSTED_PLATFORM_ASSEMBLIES, newTrusted);
    }

    
    
    
    
    
    
    
    public static IEnumerable<string> GetUniqueFilesInDirectories(IEnumerable<string> directories,
                                                                  string pattern = "*")
    {
        var result = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var directory in directories)
        {
            
            
            if (!Directory.Exists(directory))
            {
                Logger.Log(LogLevel.Warning, $"Skipping search directory that does not exist: {directory}");
                continue;
            }

            foreach (var file in Directory.GetFiles(directory, pattern))
            {
                var fileName = Path.GetFileName(file);
                if (!result.ContainsKey(fileName))
                    result[fileName] = file;
            }
        }

        return result.Values;
    }
}
