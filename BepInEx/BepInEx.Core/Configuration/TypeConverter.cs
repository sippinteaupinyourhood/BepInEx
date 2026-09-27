using System;

namespace BepInEx.Configuration;




public class TypeConverter
{
    
    
    
    
    public Func<object, Type, string> ConvertToString { get; set; }

    
    
    
    
    public Func<string, Type, object> ConvertToObject { get; set; }
}
