using System;
using System.Collections.Generic;
using System.IO;
using Cpp2IL.Core.Logging;
using LibCpp2IL;

namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlPlugin
{
    protected readonly PluginLogger Logger;

    private readonly List<string> temporaryFilePaths = [];

    protected Cpp2IlPlugin()
    {
        Logger = new(this);
    }

    public string GetTemporaryFilePath()
    {
        var ret = Path.GetTempFileName();
        File.Delete(ret);
        temporaryFilePaths.Add(ret);
        return ret;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }

    public abstract void OnLoad();

    protected void RegisterBinaryFormat<T>(string name, Func<byte[], bool> isValid, Func<MemoryStream, T> factory) where T : Il2CppBinary =>
        LibCpp2IlBinaryRegistry.Register(name, Name, isValid, factory);

    protected void RegisterBinaryRegistrationFuncFallbackHandler(Il2CppBinary.RegistrationStructLocationFailureHandler handler) =>
        Il2CppBinary.OnRegistrationStructLocationFailure += handler;

    
    
    
    
    
    
    public virtual bool HandleGamePath(string gamePath, ref Cpp2IlRuntimeArgs args) => false;

    internal void CallOnFinish()
    {
        
        foreach (var file in temporaryFilePaths)
        {
            File.Delete(file);
        }

        OnFinish();
    }

    
    
    
    protected virtual void OnFinish()
    {
    }
}
