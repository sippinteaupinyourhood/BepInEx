using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BepInEx.Unity.IL2CPP;

internal static class InteropAssemblyValidator
{
    internal static int NormalizeDuplicateTypeNames(string outputDirectory)
    {
        var renamed = 0;
        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var temporaryPath = path + ".normalized";
            var changed = false;
            var renamedBeforeAssembly = renamed;
            using (var resolver = new DefaultAssemblyResolver())
            using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
                   {
                       ReadingMode = ReadingMode.Immediate,
                       InMemory = true,
                       AssemblyResolver = resolver
                   }))
            {
                resolver.AddSearchDirectory(outputDirectory);
                foreach (var module in assembly.Modules)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var type in module.GetTypes())
                    {
                        if (names.Add(type.FullName))
                            continue;

                        var originalName = type.Name;
                        var suffix = type.MetadataToken.RID.ToString("X8");
                        var candidate = $"{originalName}__{suffix}";
                        var sequence = 0;
                        type.Name = candidate;
                        while (!names.Add(type.FullName))
                            type.Name = $"{candidate}_{++sequence}";

                        renamed++;
                        changed = true;
                    }

                    foreach (var type in module.GetTypes())
                    {
                        renamed += RenameDuplicates(type.Fields,
                                                    field => field.Name,
                                                    field => field.Name,
                                                    (field, name) => field.Name = name);
                        renamed += RenameDuplicates(type.Properties,
                                                    property => $"{property.Name}|{string.Join(",", property.Parameters.Select(parameter => parameter.ParameterType.FullName))}",
                                                    property => property.Name,
                                                    (property, name) => property.Name = name);
                        renamed += RenameDuplicates(type.Events,
                                                    eventDefinition => eventDefinition.Name,
                                                    eventDefinition => eventDefinition.Name,
                                                    (eventDefinition, name) => eventDefinition.Name = name);
                        renamed += RenameDuplicates(type.Methods,
                                                    method => $"{method.Name}|{method.GenericParameters.Count}|{string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName))}",
                                                    method => method.Name,
                                                    (method, name) => method.Name = name);
                    }

                    renamed += RepairCompilerRequiredAttributes(module);
                    renamed += RepairUnityWebRequestAudio(module);
                }

                changed |= renamed > renamedBeforeAssembly;

                if (changed)
                    assembly.Write(temporaryPath);
            }

            if (!changed)
                continue;

            File.Move(temporaryPath, path, true);
        }

        return renamed;
    }

    private static int RepairUnityWebRequestAudio(ModuleDefinition module)
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
        if (type.Methods.Any(method => method.IsConstructor &&
                                      method.Parameters.Count == 2 &&
                                      method.Parameters[0].ParameterType.FullName == stringType.FullName &&
                                      method.Parameters[1].ParameterType.FullName == audioType.FullName))
            return 0;

        var baseType = type.BaseType.Resolve();
        var pointerConstructor = baseType?.Methods.FirstOrDefault(method => method.IsConstructor &&
                                                                  method.Parameters.Count == 1 &&
                                                                  method.Parameters[0].ParameterType.MetadataType == MetadataType.IntPtr);
        if (pointerConstructor == null)
            return 0;

        var constructor = new MethodDefinition(".ctor",
                                               MethodAttributes.Public |
                                               MethodAttributes.HideBySig |
                                               MethodAttributes.SpecialName |
                                               MethodAttributes.RTSpecialName,
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
        return 1;
    }

    private static int RepairCompilerRequiredAttributes(ModuleDefinition module)
    {
        var nullableAttribute = module.GetType("System.Runtime.CompilerServices.NullableAttribute");
        var repaired = 0;
        if (nullableAttribute != null &&
            !nullableAttribute.Methods.Any(method => method.IsConstructor &&
                                                     method.Parameters.Count == 1 &&
                                                     method.Parameters[0].ParameterType.MetadataType == MetadataType.Byte))
        {
            AddAttributeConstructor(nullableAttribute, module.TypeSystem.Byte);
            repaired++;
        }

        if (nullableAttribute != null &&
            !nullableAttribute.Methods.Any(method => method.IsConstructor &&
                                                     method.Parameters.Count == 1 &&
                                                     method.Parameters[0].ParameterType is ArrayType array &&
                                                     array.ElementType.MetadataType == MetadataType.Byte))
        {
            AddAttributeConstructor(nullableAttribute, new ArrayType(module.TypeSystem.Byte));
            repaired++;
        }

        var isUnmanagedAttribute = module.GetType("System.Runtime.CompilerServices.IsUnmanagedAttribute");
        if (isUnmanagedAttribute != null &&
            !isUnmanagedAttribute.Methods.Any(method => method.IsConstructor && method.Parameters.Count == 0))
        {
            AddAttributeConstructor(isUnmanagedAttribute);
            repaired++;
        }

        return repaired;
    }

    private static void AddAttributeConstructor(TypeDefinition attributeType, params TypeReference[] parameterTypes)
    {
        var module = attributeType.Module;
        var constructor = new MethodDefinition(".ctor",
                                               MethodAttributes.Public |
                                               MethodAttributes.HideBySig |
                                               MethodAttributes.SpecialName |
                                               MethodAttributes.RTSpecialName,
                                               module.TypeSystem.Void);
        for (var index = 0; index < parameterTypes.Length; index++)
            constructor.Parameters.Add(new ParameterDefinition($"value{index}", ParameterAttributes.None, parameterTypes[index]));
        var baseConstructor = typeof(Attribute).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                                                               null,
                                                               Type.EmptyTypes,
                                                               null) ?? throw new InvalidDataException("System.Attribute constructor was not found.");
        var il = constructor.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(baseConstructor)));
        il.Append(il.Create(OpCodes.Ret));
        attributeType.Methods.Add(constructor);
    }

    private static int RenameDuplicates<T>(IEnumerable<T> members,
                                           Func<T, string> keySelector,
                                           Func<T, string> nameSelector,
                                           Action<T, string> nameSetter)
        where T : IMetadataTokenProvider
    {
        var renamed = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (keys.Add(keySelector(member)))
                continue;

            var candidate = $"{nameSelector(member)}__{member.MetadataToken.RID:X8}";
            var sequence = 0;
            nameSetter(member, candidate);
            while (!keys.Add(keySelector(member)))
                nameSetter(member, $"{candidate}_{++sequence}");
            renamed++;
        }

        return renamed;
    }

    internal static void Validate(string outputDirectory, int sourceAssemblyCount)
    {
        var generatedPaths = Directory.EnumerateFiles(outputDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                                      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                                      .ToArray();
        if (generatedPaths.Length < sourceAssemblyCount)
            throw new InvalidDataException($"Interop generation produced only {generatedPaths.Length} assemblies for {sourceAssemblyCount} source assemblies.");

        foreach (var requiredAssembly in new[] { "Il2Cppmscorlib.dll", "UnityEngine.CoreModule.dll", "Assembly-CSharp.dll" })
        {
            var path = Path.Combine(outputDirectory, requiredAssembly);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidDataException($"Interop generation did not produce required assembly {requiredAssembly}.");
        }

        foreach (var path in generatedPaths)
        {
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(outputDirectory);
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
            {
                ReadingMode = ReadingMode.Immediate,
                InMemory = true,
                AssemblyResolver = resolver
            });

            foreach (var module in assembly.Modules)
            {
                ValidateCustomAttributes(assembly.CustomAttributes, path, assembly.Name.Name);
                ValidateCustomAttributes(module.CustomAttributes, path, module.Name);
                var typeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var type in module.GetTypes())
                {
                    if (!typeNames.Add(type.FullName))
                        throw new InvalidDataException($"Interop assembly {Path.GetFileName(path)} contains duplicate type {type.FullName}.");

                    _ = type.Methods.Count;
                    _ = type.Fields.Count;
                    _ = type.Properties.Count;
                    _ = type.Events.Count;

                    EnsureUnique(type.Fields,
                                 field => field.Name,
                                 path,
                                 type.FullName,
                                 "field");
                    EnsureUnique(type.Properties,
                                 property => $"{property.Name}|{string.Join(",", property.Parameters.Select(parameter => parameter.ParameterType.FullName))}",
                                 path,
                                 type.FullName,
                                 "property");
                    EnsureUnique(type.Events,
                                 eventDefinition => eventDefinition.Name,
                                 path,
                                 type.FullName,
                                 "event");
                    EnsureUnique(type.Methods,
                                 method => $"{method.Name}|{method.GenericParameters.Count}|{string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName))}",
                                 path,
                                 type.FullName,
                                 "method signature");

                    foreach (var method in type.Methods)
                    {
                        _ = method.ReturnType.FullName;
                        ValidateCustomAttributes(method.MethodReturnType.CustomAttributes, path, method.FullName);
                        foreach (var genericParameter in method.GenericParameters)
                        {
                            ValidateCustomAttributes(genericParameter.CustomAttributes, path, method.FullName);
                            foreach (var constraint in genericParameter.Constraints)
                                _ = constraint.ConstraintType.FullName;
                        }
                        foreach (var parameter in method.Parameters)
                            _ = parameter.ParameterType.FullName;
                        if (method.HasBody)
                        {
                            _ = method.Body.Instructions.Count;
                            _ = method.Body.ExceptionHandlers.Count;
                            _ = method.Body.Variables.Count;
                        }
                    }

                    foreach (var field in type.Fields)
                    {
                        _ = field.FieldType.FullName;
                        ValidateCustomAttributes(field.CustomAttributes, path, field.FullName);
                        if (type.IsEnum && field.Name != "value__" && !field.HasConstant)
                            throw new InvalidDataException($"Interop enum {type.FullName} contains a value without a constant.");
                    }

                    foreach (var property in type.Properties)
                    {
                        _ = property.PropertyType.FullName;
                        ValidateCustomAttributes(property.CustomAttributes, path, property.FullName);
                    }

                    foreach (var eventDefinition in type.Events)
                        ValidateCustomAttributes(eventDefinition.CustomAttributes, path, eventDefinition.FullName);

                    foreach (var genericParameter in type.GenericParameters)
                    {
                        ValidateCustomAttributes(genericParameter.CustomAttributes, path, type.FullName);
                        foreach (var constraint in genericParameter.Constraints)
                            _ = constraint.ConstraintType.FullName;
                    }

                    ValidateCustomAttributes(type.CustomAttributes, path, type.FullName);
                    foreach (var method in type.Methods)
                    {
                        ValidateCustomAttributes(method.CustomAttributes, path, method.FullName);
                        foreach (var parameter in method.Parameters)
                            ValidateCustomAttributes(parameter.CustomAttributes, path, method.FullName);
                    }
                }
            }
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> members,
                                        Func<T, string> keySelector,
                                        string assemblyPath,
                                        string owner,
                                        string memberKind)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var key = keySelector(member);
            if (!keys.Add(key))
                throw new InvalidDataException($"Interop assembly {Path.GetFileName(assemblyPath)} contains duplicate {memberKind} {key} on {owner}.");
        }
    }

    private static void ValidateCustomAttributes(IEnumerable<CustomAttribute> attributes, string assemblyPath, string owner)
    {
        foreach (var attribute in attributes)
        {
            try
            {
                _ = attribute.ConstructorArguments.Count;
                _ = attribute.Fields.Count;
                _ = attribute.Properties.Count;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"Interop assembly {Path.GetFileName(assemblyPath)} contains an invalid attribute on {owner}.", exception);
            }
        }
    }
}
