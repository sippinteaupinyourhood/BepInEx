using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Mono.Cecil;
using NativeApi = Il2CppInterop.Runtime.IL2CPP;

namespace BepInEx.Unity.IL2CPP;

internal static unsafe class RuntimeEnumRepair
{
    internal static int ApplyPending(string interopDirectory)
    {
        if (!Directory.Exists(interopDirectory))
            return 0;

        var applied = 0;
        foreach (var pendingPath in Directory.EnumerateFiles(interopDirectory, "*.enum-repair"))
        {
            var targetPath = pendingPath[..^".enum-repair".Length];
            File.SetAttributes(targetPath, FileAttributes.Normal);
            File.Move(pendingPath, targetPath, true);
            applied++;
        }
        return applied;
    }

    internal static int Repair(string interopDirectory)
    {
        if (!Directory.Exists(interopDirectory))
            return 0;

        var changed = 0;
        nuint assemblyCount = 0;
        var assemblies = NativeApi.il2cpp_domain_get_assemblies(NativeApi.il2cpp_domain_get(), ref assemblyCount);
        for (nuint assemblyIndex = 0; assemblyIndex < assemblyCount; assemblyIndex++)
        {
            var image = NativeApi.il2cpp_assembly_get_image(assemblies[assemblyIndex]);
            var imageName = Marshal.PtrToStringUTF8(NativeApi.il2cpp_image_get_name(image));
            if (string.IsNullOrEmpty(imageName))
                continue;

            var path = Path.Combine(interopDirectory, Path.GetFileName(imageName));
            if (!File.Exists(path) && string.Equals(Path.GetFileName(imageName), "mscorlib.dll",
                                                     StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(interopDirectory, "Il2Cppmscorlib.dll");
            if (!File.Exists(path))
                continue;

            changed += RepairAssembly(path, image);
        }
        return changed;
    }

    private static int RepairAssembly(string path, IntPtr image)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(path));
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
        {
            ReadingMode = ReadingMode.Immediate,
            InMemory = true,
            AssemblyResolver = resolver
        });

        var changed = 0;
        var classCount = NativeApi.il2cpp_image_get_class_count(image);
        for (uint classIndex = 0; classIndex < classCount; classIndex++)
        {
            var klass = NativeApi.il2cpp_image_get_class(image, classIndex);
            if (klass == IntPtr.Zero || !NativeApi.il2cpp_class_is_enum(klass))
                continue;

            var type = assembly.MainModule.GetType(GetFullName(klass));
            if (type == null || !type.IsEnum)
                continue;

            var underlyingType = type.Fields.FirstOrDefault(field => field.Name == "value__")?.FieldType.MetadataType;
            if (underlyingType == null)
                continue;

            var fields = type.Fields.Where(field => field.IsLiteral).ToArray();
            var nativeFields = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            var iterator = IntPtr.Zero;
            IntPtr nativeField;
            while ((nativeField = NativeApi.il2cpp_class_get_fields(klass, ref iterator)) != IntPtr.Zero)
            {
                const uint fieldAttributeLiteral = 0x0040;
                if ((NativeApi.il2cpp_field_get_flags(nativeField) & fieldAttributeLiteral) == 0)
                    continue;

                var nativeName = Marshal.PtrToStringUTF8(NativeApi.il2cpp_field_get_name(nativeField));
                if (string.IsNullOrEmpty(nativeName) || !nativeFields.TryAdd(nativeName, nativeField))
                    throw new InvalidDataException("Invalid native enum field metadata for " + type.FullName + ".");
            }

            if (nativeFields.Count != fields.Length)
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture,
                    "Native enum field count mismatch for {0}: native={1}, generated={2}.",
                    type.FullName, nativeFields.Count, fields.Length));

            foreach (var managedField in fields)
            {
                if (!nativeFields.TryGetValue(managedField.Name, out var matchedNativeField))
                    throw new InvalidDataException("Native enum field was not found for " + managedField.FullName + ".");

                ulong storage = 0;
                NativeApi.il2cpp_field_static_get_value(matchedNativeField, &storage);
                var value = ReadConstant((IntPtr)(&storage), underlyingType.Value);
                if (managedField.HasConstant && Equals(managedField.Constant, value))
                    continue;

                managedField.Constant = value;
                changed++;
            }
        }

        if (changed == 0)
            return 0;

        var temporaryPath = path + ".enum-repair";
        assembly.Write(temporaryPath);
        assembly.Dispose();
        resolver.Dispose();
        return changed;
    }

    private static string GetFullName(IntPtr klass)
    {
        var name = Marshal.PtrToStringUTF8(NativeApi.il2cpp_class_get_name(klass)) ?? string.Empty;
        var declaringType = NativeApi.il2cpp_class_get_declaring_type(klass);
        if (declaringType != IntPtr.Zero)
            return GetFullName(declaringType) + "/" + name;

        var namespaze = Marshal.PtrToStringUTF8(NativeApi.il2cpp_class_get_namespace(klass)) ?? string.Empty;
        return string.IsNullOrEmpty(namespaze) ? name : namespaze + "." + name;
    }

    private static object ReadConstant(IntPtr value, MetadataType type) => type switch
    {
        MetadataType.Boolean => Marshal.ReadByte(value) != 0,
        MetadataType.SByte => unchecked((sbyte)Marshal.ReadByte(value)),
        MetadataType.Byte => Marshal.ReadByte(value),
        MetadataType.Int16 => Marshal.ReadInt16(value),
        MetadataType.UInt16 => unchecked((ushort)Marshal.ReadInt16(value)),
        MetadataType.Int32 => Marshal.ReadInt32(value),
        MetadataType.UInt32 => unchecked((uint)Marshal.ReadInt32(value)),
        MetadataType.Int64 => Marshal.ReadInt64(value),
        MetadataType.UInt64 => unchecked((ulong)Marshal.ReadInt64(value)),
        _ => throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture,
            "Unsupported enum storage type {0}.", type))
    };
}
