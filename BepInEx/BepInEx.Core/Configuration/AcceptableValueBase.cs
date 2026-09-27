using System;

namespace BepInEx.Configuration;




public abstract class AcceptableValueBase
{
    
    protected AcceptableValueBase(Type valueType)
    {
        ValueType = valueType;
    }

    
    
    
    public Type ValueType { get; }

    
    
    
    public abstract object Clamp(object value);

    
    
    
    public abstract bool IsValid(object value);

    
    
    
    public abstract string ToDescriptionString();
}
