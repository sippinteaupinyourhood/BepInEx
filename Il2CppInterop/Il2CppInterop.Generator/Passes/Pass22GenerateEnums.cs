using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.Extensions;
using Il2CppInterop.Generator.Utils;

namespace Il2CppInterop.Generator.Passes;

public static class Pass22GenerateEnums
{
    public static void DoPass(RewriteGlobalContext context)
    {
        foreach (var assemblyContext in context.Assemblies)
            foreach (var typeContext in assemblyContext.Types)
            {
                if (!typeContext.OriginalType.IsEnum) continue;

                var type = typeContext.OriginalType;
                var newType = typeContext.NewType;

                if (type.Namespace.NameShouldBePrefixed(context.Options))
                {
                    newType.CustomAttributes.Add(new CustomAttribute(
                        (ICustomAttributeType)assemblyContext.Imports.OriginalNameAttributector.Value,
                        new CustomAttributeSignature(
                            new CustomAttributeArgument(assemblyContext.Imports.Module.String(), type.DeclaringModule?.Name ?? ""),
                            new CustomAttributeArgument(assemblyContext.Imports.Module.String(), type.Namespace ?? ""),
                            new CustomAttributeArgument(assemblyContext.Imports.Module.String(), type.Name ?? ""))));
                }

                if (type.CustomAttributes.Any(it => it.Constructor?.DeclaringType?.FullName == "System.FlagsAttribute"))
                    newType.CustomAttributes.Add(new CustomAttribute(assemblyContext.Imports.Module.FlagsAttributeCtor()));

                foreach (var fieldDefinition in type.Fields)
                {
                    var fieldName = fieldDefinition.Name!;
                    if (!context.Options.PassthroughNames && fieldName.IsObfuscated(context.Options))
                        fieldName = GetUnmangledName(fieldDefinition);

                    var originalTypeName = typeContext.OriginalType.GetNamespacePrefix() + "." + typeContext.OriginalType.Name;
                    var newTypeName = typeContext.NewType.GetNamespacePrefix() + "." + typeContext.NewType.Name;
                    if (context.Options.RenameMap.TryGetValue(originalTypeName + "::" + fieldName, out var newName) ||
                        context.Options.RenameMap.TryGetValue(newTypeName + "::" + fieldName, out newName))
                        fieldName = newName;

                    var newDef = new FieldDefinition(fieldName, fieldDefinition.Attributes | FieldAttributes.HasDefault,
                        assemblyContext.RewriteTypeRef(fieldDefinition.Signature!.FieldType));
                    newType.Fields.Add(newDef);

                    newDef.Constant = fieldDefinition.Constant;
                }
            }
    }

    public static string GetUnmangledName(FieldDefinition field)
    {
        return "EnumValue" + field.Constant;
    }
}
