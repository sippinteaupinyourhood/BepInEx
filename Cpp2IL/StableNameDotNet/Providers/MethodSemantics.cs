using System;

namespace StableNameDotNet.Providers;




[Flags]
public enum MethodSemantics
{
    
    
    
    
    Setter = 1,

    
    
    
    
    Getter = 2,

    
    Other = 4,

    
    
    
    
    AddOn = 8,

    
    
    
    
    RemoveOn = 16, 

    
    
    
    
    Fire = 32, 
}
