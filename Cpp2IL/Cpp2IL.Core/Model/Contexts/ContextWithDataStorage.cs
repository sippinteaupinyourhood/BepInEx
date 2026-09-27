using System.Collections.Generic;

namespace Cpp2IL.Core.Model.Contexts;




public abstract class ContextWithDataStorage
{
    private Dictionary<string, object> _dataStorage = new();

    
    
    
    
    
    public void PutExtraData<T>(string key, T value) where T : class => _dataStorage[key] = value;

    
    
    
    
    
    
    
    public T? GetExtraData<T>(string key) where T : class => _dataStorage.TryGetValue(key, out var ret) ? ret as T : null;
}
