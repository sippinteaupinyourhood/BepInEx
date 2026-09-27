using System.Collections.Generic;
using System.Reflection;

namespace StableNameDotNet.Providers;

public interface ITypeInfoProvider
{
    public ITypeInfoProvider? DeclaringTypeInfoProvider { get; }

    
    
    
    
    public IEnumerable<ITypeInfoProvider> GetBaseTypeHierarchy();

    
    
    
    public IEnumerable<ITypeInfoProvider> Interfaces { get; }

    
    
    
    public TypeAttributes TypeAttributes { get; }

    
    
    
    public string OriginalTypeName { get; }

    public string RewrittenTypeName { get; }

    public string TypeNamespace { get; }

    
    
    
    public bool IsGenericInstance { get; }

    
    
    
    public bool IsValueType { get; }

    
    
    
    public bool IsEnumType { get; }

    
    
    
    public int GenericParameterCount { get; }

    
    
    
    public IEnumerable<ITypeInfoProvider> GenericArgumentInfoProviders { get; }

    public IEnumerable<IFieldInfoProvider> FieldInfoProviders { get; }

    public IEnumerable<IMethodInfoProvider> MethodInfoProviders { get; }

    public IEnumerable<IPropertyInfoProvider> PropertyInfoProviders { get; }
}
