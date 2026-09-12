using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BepInEx.Unity.IL2CPP;

internal static class InteropOutputValidator
{
    internal static int NormalizeAndValidate(string outputDirectory, int sourceAssemblyCount,
                                             string unityReferenceDirectory)
    {
        var changedCount = 0;
        foreach (var path in Directory.GetFiles(outputDirectory, "*.dll"))
        {
            var temporaryPath = path + ".normalized";
            var changed = false;
            var unityReferencePath = Path.Combine(unityReferenceDirectory, Path.GetFileName(path));
            AssemblyDefinition unityReference = null;
            DefaultAssemblyResolver unityReferenceResolver = null;
            if (File.Exists(unityReferencePath))
            {
                unityReferenceResolver = new DefaultAssemblyResolver();
                unityReferenceResolver.AddSearchDirectory(unityReferenceDirectory);
                unityReference = AssemblyDefinition.ReadAssembly(unityReferencePath, new ReaderParameters
                {
                    ReadingMode = ReadingMode.Immediate,
                    InMemory = true,
                    AssemblyResolver = unityReferenceResolver
                });
            }

            using (var resolver = new DefaultAssemblyResolver())
            {
                resolver.AddSearchDirectory(outputDirectory);
                using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
                   {
                       ReadingMode = ReadingMode.Immediate,
                       InMemory = true,
                       AssemblyResolver = resolver
                   });
                foreach (var module in assembly.Modules)
                {
                    var typeNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var type in module.GetTypes())
                    {
                        if (typeNames.Add(type.FullName))
                            continue;
                        Rename(type, typeNames);
                        changedCount++;
                        changed = true;
                    }

                    foreach (var type in module.GetTypes())
                    {
                        changedCount += RenameDuplicates(type.Fields, x => x.Name, x => x.Name,
                                                         (x, value) => x.Name = value, ref changed);
                        changedCount += RenameDuplicates(type.Properties,
                                                         x => $"{x.Name}|{Join(x.Parameters.Select(p => p.ParameterType.FullName))}",
                                                         x => x.Name, (x, value) => x.Name = value, ref changed);
                        changedCount += RenameDuplicates(type.Events, x => x.Name, x => x.Name,
                                                         (x, value) => x.Name = value, ref changed);
                        changedCount += RenameDuplicates(type.Methods,
                                                         x => $"{x.Name}|{x.GenericParameters.Count}|{Join(x.Parameters.Select(p => p.ParameterType.FullName))}",
                                                         x => x.Name, (x, value) => x.Name = value, ref changed);
                    }

                    changedCount += RepairCompilerAttributes(module, ref changed);
                    changedCount += RepairUnityWebRequestAudio(module, ref changed);
                    if (unityReference != null)
                        changedCount += RepairUnityEnumConstants(module, unityReference.MainModule, ref changed);
                }

                if (changed)
                    assembly.Write(temporaryPath);
            }

            unityReference?.Dispose();
            unityReferenceResolver?.Dispose();

            if (!changed)
                continue;

            File.SetAttributes(path, FileAttributes.Normal);
            File.Copy(temporaryPath, path, true);
            File.Delete(temporaryPath);
        }

        Validate(outputDirectory, sourceAssemblyCount);
        return changedCount;
    }

    private static int RepairUnityEnumConstants(ModuleDefinition module, ModuleDefinition referenceModule,
                                                ref bool changed)
    {
        var count = 0;
        foreach (var type in module.GetTypes())
        {
            if (!type.IsEnum)
                continue;

            var referenceType = referenceModule.GetType(type.FullName);
            if (referenceType == null || !referenceType.IsEnum)
                continue;

            var referenceFields = referenceType.Fields
                                               .Where(field => field.IsLiteral && field.HasConstant)
                                               .ToDictionary(field => field.Name, field => field.Constant,
                                                             StringComparer.Ordinal);
            foreach (var field in type.Fields)
            {
                if (!field.IsLiteral || !referenceFields.TryGetValue(field.Name, out var expectedValue))
                    continue;
                if (field.HasConstant && Equals(field.Constant, expectedValue))
                    continue;

                field.Constant = expectedValue;
                count++;
                changed = true;
            }
        }
        return count;
    }

    private static int RepairUnityWebRequestAudio(ModuleDefinition module, ref bool changed)
    {
        var type = module.GetType("UnityEngine.Networking.DownloadHandlerAudioClip");
        if (type == null)
            return 0;

        var stringType = module.TypeSystem.String;
        var initialize = type.Methods.FirstOrDefault(method => method.Name == "InternalCreateAudioClip" &&
                                                               method.Parameters.Count == 2 &&
                                                               method.Parameters[0].ParameterType.FullName == stringType.FullName);
        if (initialize == null)
            return 0;

        var audioType = initialize.Parameters[1].ParameterType;
        if (type.Methods.Any(method => method.IsConstructor && method.Parameters.Count == 2 &&
                                      method.Parameters[0].ParameterType.FullName == stringType.FullName &&
                                      method.Parameters[1].ParameterType.FullName == audioType.FullName))
            return 0;

        var baseType = type.BaseType.Resolve();
        var pointerConstructor = baseType?.Methods.FirstOrDefault(method => method.IsConstructor &&
                                                                  method.Parameters.Count == 1 &&
                                                                  method.Parameters[0].ParameterType.MetadataType == MetadataType.IntPtr);
        if (pointerConstructor == null)
            return 0;

        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig |
                                                        MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                               module.TypeSystem.Void);
        constructor.Parameters.Add(new ParameterDefinition("url", ParameterAttributes.None, stringType));
        constructor.Parameters.Add(new ParameterDefinition("audioType", ParameterAttributes.None, audioType));
        var il = constructor.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Conv_I));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(pointerConstructor)));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Call, initialize));
        il.Append(il.Create(OpCodes.Ret));
        type.Methods.Add(constructor);
        changed = true;
        return 1;
    }

    private static void Rename(TypeDefinition type, HashSet<string> names)
    {
        var candidate = $"{type.Name}__{type.MetadataToken.RID:X8}";
        var sequence = 0;
        type.Name = candidate;
        while (!names.Add(type.FullName))
            type.Name = $"{candidate}_{++sequence}";
    }

    private static int RenameDuplicates<T>(IEnumerable<T> members, Func<T, string> key,
                                           Func<T, string> name, Action<T, string> setName,
                                           ref bool changed) where T : IMetadataTokenProvider
    {
        var count = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (keys.Add(key(member)))
                continue;
            var candidate = $"{name(member)}__{member.MetadataToken.RID:X8}";
            var sequence = 0;
            setName(member, candidate);
            while (!keys.Add(key(member)))
                setName(member, $"{candidate}_{++sequence}");
            count++;
            changed = true;
        }
        return count;
    }

    private static int RepairCompilerAttributes(ModuleDefinition module, ref bool changed)
    {
        var count = 0;
        var nullable = module.GetType("System.Runtime.CompilerServices.NullableAttribute");
        if (nullable == null && string.Equals(module.Assembly.Name.Name, "UnityEngine.CoreModule",
                                               StringComparison.Ordinal))
        {
            nullable = AddNullableAttribute(module);
            count++;
        }
        if (nullable != null && !HasConstructor(nullable, MetadataType.Byte, false))
        {
            AddConstructor(nullable, module.TypeSystem.Byte);
            count++;
        }
        if (nullable != null && !HasConstructor(nullable, MetadataType.Byte, true))
        {
            AddConstructor(nullable, new ArrayType(module.TypeSystem.Byte));
            count++;
        }

        var unmanaged = module.GetType("System.Runtime.CompilerServices.IsUnmanagedAttribute");
        if (unmanaged != null && !unmanaged.Methods.Any(x => x.IsConstructor && x.Parameters.Count == 0))
        {
            AddConstructor(unmanaged);
            count++;
        }
        changed |= count != 0;
        return count;
    }

    private static TypeDefinition AddNullableAttribute(ModuleDefinition module)
    {
        var attributeType = module.ImportReference(typeof(Attribute));
        var nullable = new TypeDefinition("System.Runtime.CompilerServices", "NullableAttribute",
                                          TypeAttributes.NotPublic | TypeAttributes.Sealed |
                                          TypeAttributes.BeforeFieldInit,
                                          attributeType);
        module.Types.Add(nullable);

        var flags = new FieldDefinition("NullableFlags", FieldAttributes.Public | FieldAttributes.InitOnly,
                                        new ArrayType(module.TypeSystem.Byte));
        nullable.Fields.Add(flags);

        var attributeConstructor = typeof(Attribute).GetConstructor(System.Reflection.BindingFlags.Instance |
                                                                    System.Reflection.BindingFlags.NonPublic,
                                                                    null, Type.EmptyTypes, null)
                                   ?? throw new InvalidDataException("System.Attribute constructor was not found.");
        var importedAttributeConstructor = module.ImportReference(attributeConstructor);

        var byteConstructor = NewConstructor(nullable, module.TypeSystem.Byte);
        var byteIl = byteConstructor.Body.GetILProcessor();
        byteIl.Append(byteIl.Create(OpCodes.Ldarg_0));
        byteIl.Append(byteIl.Create(OpCodes.Call, importedAttributeConstructor));
        byteIl.Append(byteIl.Create(OpCodes.Ldarg_0));
        byteIl.Append(byteIl.Create(OpCodes.Ldc_I4_1));
        byteIl.Append(byteIl.Create(OpCodes.Newarr, module.TypeSystem.Byte));
        byteIl.Append(byteIl.Create(OpCodes.Dup));
        byteIl.Append(byteIl.Create(OpCodes.Ldc_I4_0));
        byteIl.Append(byteIl.Create(OpCodes.Ldarg_1));
        byteIl.Append(byteIl.Create(OpCodes.Stelem_I1));
        byteIl.Append(byteIl.Create(OpCodes.Stfld, flags));
        byteIl.Append(byteIl.Create(OpCodes.Ret));

        var arrayConstructor = NewConstructor(nullable, new ArrayType(module.TypeSystem.Byte));
        var arrayIl = arrayConstructor.Body.GetILProcessor();
        arrayIl.Append(arrayIl.Create(OpCodes.Ldarg_0));
        arrayIl.Append(arrayIl.Create(OpCodes.Call, importedAttributeConstructor));
        arrayIl.Append(arrayIl.Create(OpCodes.Ldarg_0));
        arrayIl.Append(arrayIl.Create(OpCodes.Ldarg_1));
        arrayIl.Append(arrayIl.Create(OpCodes.Stfld, flags));
        arrayIl.Append(arrayIl.Create(OpCodes.Ret));
        return nullable;
    }

    private static MethodDefinition NewConstructor(TypeDefinition type, TypeReference parameterType)
    {
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig |
                                                        MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                               type.Module.TypeSystem.Void);
        constructor.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, parameterType));
        type.Methods.Add(constructor);
        return constructor;
    }

    private static bool HasConstructor(TypeDefinition type, MetadataType elementType, bool array)
        => type.Methods.Any(method => method.IsConstructor && method.Parameters.Count == 1 &&
            (array
                ? method.Parameters[0].ParameterType is ArrayType value && value.ElementType.MetadataType == elementType
                : method.Parameters[0].ParameterType.MetadataType == elementType));

    private static void AddConstructor(TypeDefinition type, params TypeReference[] parameterTypes)
    {
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig |
                                                        MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                                               type.Module.TypeSystem.Void);
        for (var index = 0; index < parameterTypes.Length; index++)
            constructor.Parameters.Add(new ParameterDefinition($"value{index}", ParameterAttributes.None,
                                                               parameterTypes[index]));
        var baseConstructor = typeof(Attribute).GetConstructor(System.Reflection.BindingFlags.Instance |
                                                               System.Reflection.BindingFlags.NonPublic,
                                                               null, Type.EmptyTypes, null)
                              ?? throw new InvalidDataException("System.Attribute constructor was not found.");
        var il = constructor.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, type.Module.ImportReference(baseConstructor)));
        il.Append(il.Create(OpCodes.Ret));
        type.Methods.Add(constructor);
    }

    private static void Validate(string outputDirectory, int sourceAssemblyCount)
    {
        var paths = Directory.GetFiles(outputDirectory, "*.dll");
        if (paths.Length < sourceAssemblyCount)
            throw new InvalidDataException($"Interop generation produced only {paths.Length} assemblies for {sourceAssemblyCount} source assemblies.");

        foreach (var required in new[] { "Il2Cppmscorlib.dll", "UnityEngine.CoreModule.dll", "Assembly-CSharp.dll" })
            if (!File.Exists(Path.Combine(outputDirectory, required)))
                throw new InvalidDataException($"Interop generation did not produce {required}.");

        var assemblyIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nativeTypeIdentities = new HashSet<string>(StringComparer.Ordinal);
        var nativeTypeBindingCount = 0;
        foreach (var path in paths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
            {
                ReadingMode = ReadingMode.Immediate,
                InMemory = true
            });
            if (!assemblyIdentities.Add(assembly.Name.FullName))
                throw new InvalidDataException($"Interop generation produced duplicate assembly identity {assembly.Name.FullName}.");
            foreach (var module in assembly.Modules)
            {
                EnsureUnique(module.GetTypes(), x => x.FullName, path, "type");
                foreach (var type in module.GetTypes())
                {
                    if (type.DeclaringType == null &&
                        TryGetNativeTypeBinding(type, out var imageName, out var namespaceName,
                                                out var nativeName))
                    {
                        ValidateNativeTypeBinding(type, assembly.Name.Name, imageName,
                                                  namespaceName, nativeName, nativeTypeIdentities,
                                                  path);
                        nativeTypeBindingCount++;
                    }

                    EnsureUnique(type.Fields, x => x.Name, path, "field");
                    EnsureUnique(type.Properties,
                                 x => $"{x.Name}|{Join(x.Parameters.Select(p => p.ParameterType.FullName))}",
                                 path, "property");
                    EnsureUnique(type.Events, x => x.Name, path, "event");
                    EnsureUnique(type.Methods,
                                 x => $"{x.Name}|{x.GenericParameters.Count}|{Join(x.Parameters.Select(p => p.ParameterType.FullName))}",
                                 path, "method");
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;
                        _ = method.Body.Instructions.Count;
                        _ = method.Body.ExceptionHandlers.Count;
                    }
                }
            }
        }

        if (nativeTypeBindingCount == 0)
            throw new InvalidDataException("Interop generation produced no native type bindings.");
    }

    private static bool TryGetNativeTypeBinding(TypeDefinition type, out string imageName,
                                                out string namespaceName, out string nativeName)
    {
        imageName = namespaceName = nativeName = null;
        var constructor = type.Methods.FirstOrDefault(method => method.Name == ".cctor" && method.HasBody);
        if (constructor == null)
            return false;

        var instructions = constructor.Body.Instructions.Where(instruction => instruction.OpCode != OpCodes.Nop)
                                      .Take(4).ToArray();
        if (instructions.Length != 4 || instructions[0].OpCode != OpCodes.Ldstr ||
            instructions[1].OpCode != OpCodes.Ldstr || instructions[2].OpCode != OpCodes.Ldstr ||
            instructions[3].OpCode != OpCodes.Call || instructions[3].Operand is not MethodReference method ||
            method.DeclaringType.FullName != "Il2CppInterop.Runtime.IL2CPP" ||
            method.Name != "GetIl2CppClass" || method.Parameters.Count != 3)
            return false;

        imageName = instructions[0].Operand as string;
        namespaceName = instructions[1].Operand as string;
        nativeName = instructions[2].Operand as string;
        return true;
    }

    private static void ValidateNativeTypeBinding(TypeDefinition type, string managedAssemblyName,
                                                  string imageName, string namespaceName,
                                                  string nativeName, HashSet<string> identities,
                                                  string path)
    {
        if (string.IsNullOrWhiteSpace(imageName) || namespaceName == null ||
            string.IsNullOrWhiteSpace(nativeName))
            throw new InvalidDataException($"Interop type {type.FullName} in {Path.GetFileName(path)} has an empty native identity.");

        var nativeAssemblyName = Path.GetFileNameWithoutExtension(imageName);
        var expectedNativeAssemblyName = managedAssemblyName.StartsWith("Il2Cpp", StringComparison.Ordinal)
                                             ? managedAssemblyName.Substring("Il2Cpp".Length)
                                             : managedAssemblyName;
        if (!string.Equals(nativeAssemblyName, expectedNativeAssemblyName,
                           StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Interop type {type.FullName} in {Path.GetFileName(path)} binds to native image {imageName}.");

        var identity = imageName.ToUpperInvariant() + "\u001f" + namespaceName + "\u001f" + nativeName;
        if (!identities.Add(identity))
            throw new InvalidDataException($"Interop generation produced duplicate native type identity {imageName}:{namespaceName}.{nativeName}.");

        var obfuscatedName = type.CustomAttributes.FirstOrDefault(attribute =>
            attribute.AttributeType.FullName == "Il2CppInterop.Common.Attributes.ObfuscatedNameAttribute");
        if (obfuscatedName == null || obfuscatedName.ConstructorArguments.Count != 1)
            return;

        var expectedName = string.IsNullOrEmpty(namespaceName) ? nativeName : namespaceName + "." + nativeName;
        var attributedName = obfuscatedName.ConstructorArguments[0].Value as string;
        if (!string.Equals(attributedName, expectedName, StringComparison.Ordinal))
            throw new InvalidDataException($"Interop type {type.FullName} in {Path.GetFileName(path)} has obfuscated identity {attributedName}, expected {expectedName}.");
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, Func<T, string> key,
                                        string path, string kind)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            if (!keys.Add(key(value)))
                throw new InvalidDataException($"Interop assembly {Path.GetFileName(path)} contains a duplicate {kind} {key(value)}.");
    }

    private static string Join(IEnumerable<string> values) => string.Join(",", values);
}
