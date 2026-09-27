using System.Reflection;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.Extensions;
using Il2CppInterop.Generator.Utils;

namespace Il2CppInterop.Generator.Passes;

public static class Pass70GenerateProperties
{
    public static void DoPass(RewriteGlobalContext context)
    {
        foreach (var assemblyContext in context.Assemblies)
            foreach (var typeContext in assemblyContext.Types)
            {
                var type = typeContext.OriginalType;
                var propertyCountsByName = new Dictionary<string, int>();

                foreach (var oldProperty in type.Properties)
                {
                    FixPropertyDefinitionParameters(oldProperty);

                    var unmangledPropertyName = UnmanglePropertyName(assemblyContext, oldProperty, typeContext.OriginalType,
                        typeContext.NewType,
                        propertyCountsByName);

                    var propertyType = assemblyContext.RewriteTypeRef(oldProperty.Signature!.ReturnType);
                    var signature = oldProperty.Signature.HasThis
                        ? PropertySignature.CreateInstance(propertyType)
                        : PropertySignature.CreateStatic(propertyType);
                    foreach (var oldParameter in oldProperty.Signature.ParameterTypes)
                        signature.ParameterTypes.Add(assemblyContext.RewriteTypeRef(oldParameter));

                    var property = new PropertyDefinition(unmangledPropertyName, oldProperty.Attributes, signature);

                    typeContext.NewType.Properties.Add(property);

                    property.SetSemanticMethods(
                        oldProperty.GetMethod is null ? null : typeContext.GetMethodByOldMethod(oldProperty.GetMethod).NewMethod,
                        oldProperty.SetMethod is null ? null : typeContext.GetMethodByOldMethod(oldProperty.SetMethod).NewMethod);
                }

                string? defaultMemberName = null;
                if (type.CustomAttributes.FirstOrDefault(IsDefaultMemberAttributeFake) != null)
                {
                    defaultMemberName = "Item";
                }
                else
                {
                    var realDefaultMemberAttribute = type.CustomAttributes.FirstOrDefault(IsDefaultMemberAttributeReal);
                    if (realDefaultMemberAttribute != null)
                        defaultMemberName = realDefaultMemberAttribute.Signature?.FixedArguments[0].Element?.ToString() ?? "Item";
                }

                if (defaultMemberName != null)
                    typeContext.NewType.CustomAttributes.Add(new CustomAttribute(
                        ReferenceCreator.CreateInstanceMethodReference(".ctor", assemblyContext.Imports.Module.Void(),
                            assemblyContext.Imports.Module.DefaultMemberAttribute().ToTypeDefOrRef(), assemblyContext.Imports.Module.String()),
                        new CustomAttributeSignature(new CustomAttributeArgument(assemblyContext.Imports.Module.String(), defaultMemberName))));
            }

        static bool IsDefaultMemberAttributeFake(CustomAttribute attribute)
        {
            return attribute.AttributeType()?.Name == "AttributeAttribute" && attribute.Signature!.NamedArguments.Any(it =>
            {
                
                return (it.MemberName == "Type" && it.Argument.Element is ITypeDescriptor { Namespace: "System.Reflection", Name: nameof(DefaultMemberAttribute) })
                    || (it.MemberName == "Name" && it.Argument.GetElementAsString() == nameof(DefaultMemberAttribute));
            });
        }

        static bool IsDefaultMemberAttributeReal(CustomAttribute attribute)
        {
            return attribute.AttributeType() is { Namespace.Value: "System.Reflection", Name.Value: nameof(DefaultMemberAttribute) };
        }
    }

    private static string UnmanglePropertyName(AssemblyRewriteContext assemblyContext, PropertyDefinition prop,
        ITypeDefOrRef originalDeclaringType, ITypeDefOrRef newDeclaringType, Dictionary<string, int> countsByBaseName)
    {
        if (assemblyContext.GlobalContext.Options.PassthroughNames ||
            (!prop.Name.IsObfuscated(assemblyContext.GlobalContext.Options) &&
             !prop.Name.ToString().StartsWith("prop_", StringComparison.Ordinal))) return prop.Name!;

        var baseName = "prop_" + assemblyContext.RewriteTypeRef(prop.Signature!.ReturnType).GetUnmangledName(prop.DeclaringType);

        countsByBaseName.TryGetValue(baseName, out var index);
        countsByBaseName[baseName] = index + 1;

        var unmanglePropertyName = baseName + "_" + index;

        var renameMap = assemblyContext.GlobalContext.Options.RenameMap;
        var originalTypeName = originalDeclaringType.GetNamespacePrefix() + "." + originalDeclaringType.Name;
        var newTypeName = newDeclaringType.GetNamespacePrefix() + "." + newDeclaringType.Name;
        if (renameMap.TryGetValue(originalTypeName + "::" + unmanglePropertyName, out var newNameByType) ||
            renameMap.TryGetValue(newTypeName + "::" + unmanglePropertyName, out newNameByType))
        {
            unmanglePropertyName = newNameByType;
        }
        else if (renameMap.TryGetValue(
                     originalDeclaringType.GetNamespacePrefix() + "::" + unmanglePropertyName, out var newName) ||
                 renameMap.TryGetValue(
                     newDeclaringType.GetNamespacePrefix() + "::" + unmanglePropertyName, out newName))
        {
            unmanglePropertyName = newName;
        }


        return unmanglePropertyName;
    }

    private static void FixPropertyDefinitionParameters(PropertyDefinition property)
    {
        

        if (property.Signature is null or { ParameterTypes.Count: > 0 })
            return;

        var getMethod = property.GetMethod;
        if (getMethod?.Signature is not null)
        {
            if (getMethod.Signature.ParameterTypes.Count > 0)
            {
                foreach (var parameter in getMethod.Signature.ParameterTypes)
                {
                    property.Signature.ParameterTypes.Add(parameter);
                }
            }

            return;
        }

        var setMethod = property.SetMethod;
        if (setMethod?.Signature is not null)
        {
            if (setMethod.Signature.ParameterTypes.Count > 1)
            {
                foreach (var parameter in setMethod.Signature.ParameterTypes.Take(setMethod.Signature.ParameterTypes.Count - 1))
                {
                    property.Signature.ParameterTypes.Add(parameter);
                }
            }
        }
    }
}
