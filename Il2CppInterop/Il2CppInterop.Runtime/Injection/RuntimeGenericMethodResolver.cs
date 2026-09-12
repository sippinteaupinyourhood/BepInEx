using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime.Injection.Hooks;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;

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
        EnsureMapLoaded();

        var module = (ulong)NativeLibrary.Load("GameAssembly.dll").ToInt64();
        var metadata = module + _metadataRegistrationRva;
        long instCount = *(int*)(metadata + 0x10);
        var instTable = *(Il2CppGenericInst***)(metadata + 0x18);
        if (instCount <= 0 || instCount > 5_000_000 || instTable == null)
            throw new InvalidDataException("Invalid generic instantiation table in resolver map");

        var requested = stackalloc Il2CppTypeStruct*[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var reflectionType = (byte*)IL2CPP.Il2CppObjectBaseToPtrNotNull(arguments[i]);
            requested[i] = *(Il2CppTypeStruct**)(reflectionType + IntPtr.Size * 2);
        }

        Il2CppGenericInst* methodInst = null;
        for (long i = 0; i < instCount; i++)
        {
            var candidate = instTable[i];
            if (candidate == null || candidate->type_argc != arguments.Length) continue;
            var match = true;
            for (var a = 0; a < arguments.Length; a++)
            {
                if (!SameType(candidate->type_argv[a], requested[a])) { match = false; break; }
            }
            if (match) { methodInst = candidate; break; }
        }
        if (methodInst == null)
        {
            // Reflection can request a valid closed generic combination that
            // the game never used at AOT compile time (GetComponent<T> is the
            // common case). Such an instantiation is consequently absent from
            // MetadataRegistration.genericInsts. Build the same native context
            // dynamically and retain it for the lifetime of the process.
            var argv = (Il2CppTypeStruct**)Marshal.AllocHGlobal(
                checked(arguments.Length * sizeof(Il2CppTypeStruct*)));
            for (var i = 0; i < arguments.Length; i++) argv[i] = requested[i];

            methodInst = (Il2CppGenericInst*)Marshal.AllocHGlobal(sizeof(Il2CppGenericInst));
            methodInst->type_argc = (uint)arguments.Length;
            methodInst->type_argv = argv;
        }

        var reflectionMethod = (Il2CppReflectionMethod*)IL2CPP.Il2CppObjectBaseToPtrNotNull(definition);
        var inflated = GenericMethod_GetMethod_Hook.ResolveOriginal(reflectionMethod->method, null, methodInst);
        if (inflated == null) throw new MissingMethodException("GenericMethod::GetMethod returned null");

        var result = IL2CPP.il2cpp_method_get_object((IntPtr)inflated, IntPtr.Zero);
        return result == IntPtr.Zero ? null : Il2CppObjectPool.Get<Il2CppSystem.Reflection.MethodInfo>(result);
    }

    private static bool SameType(Il2CppTypeStruct* left, Il2CppTypeStruct* right)
    {
        if (left == right) return true;
        if (left == null || right == null) return false;

        // Metadata registration may contain structurally identical Il2CppType
        // records at different addresses. Generic-inst lookup is semantic, so
        // pointer identity alone incorrectly rejects valid instantiations.
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
                Path.Combine(Environment.CurrentDirectory, "runtime-il2cpp-resolver-map.json")
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
