using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.Injection.Hooks;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Startup;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime.Injection;

public static unsafe class RuntimeGenericMethodResolver
{
    private static readonly object Sync = new();
    private static bool _loaded;
    private static ulong _metadataRegistrationRva;

    public static Il2CppSystem.Reflection.MethodInfo MakeGenericMethod(
        Il2CppSystem.Reflection.MethodInfo definition,
        Il2CppReferenceArray<Il2CppSystem.Type> arguments)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));
        return definition.MakeGenericMethod(arguments);
    }

    private static bool SameType(Il2CppTypeStruct* left, Il2CppTypeStruct* right)
    {
        if (left == right) return true;
        if (left == null || right == null) return false;

        
        
        
        var leftType = UnityVersionHandler.Wrap(left);
        var rightType = UnityVersionHandler.Wrap(right);
        return leftType != null && rightType != null
            && leftType.Type == rightType.Type
            && leftType.Data == rightType.Data
            && leftType.ByRef == rightType.ByRef;
    }

    private static void EnsureMapLoaded()
    {
        if (_loaded) return;
        lock (Sync)
        {
            if (_loaded) return;
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "runtime-il2cpp-resolver-map.json"),
                Path.Combine(baseDir, "BepInEx", "runtime-il2cpp-resolver-map.json"),
                Path.Combine(baseDir, "..", "runtime-il2cpp-resolver-map.json"),
                Path.Combine(Environment.CurrentDirectory, "runtime-il2cpp-resolver-map.json"),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "runtime-il2cpp-resolver-map.json")
            };
            var path = Array.Find(candidates, File.Exists)
                ?? throw new FileNotFoundException("runtime-il2cpp-resolver-map.json was not found");
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(8192, checked((int)Math.Min(stream.Length, 8192)))];
            _ = stream.Read(buffer, 0, buffer.Length);
            var header = System.Text.Encoding.UTF8.GetString(buffer);
            var match = Regex.Match(header,
                "\\\"metadataRegistration\\\"\\s*:\\s*\\{.*?\\\"rva\\\"\\s*:\\s*\\\"0x([0-9A-Fa-f]+)\\\"",
                RegexOptions.Singleline);
            if (!match.Success) throw new InvalidDataException("Resolver map metadataRegistration RVA is missing");
            _metadataRegistrationRva = Convert.ToUInt64(match.Groups[1].Value, 16);
            _loaded = true;
        }
    }
}
