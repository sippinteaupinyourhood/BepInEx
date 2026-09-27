using BepInEx.Configuration;
using BepInEx.Logging;

namespace BepInEx.Preloader.Core.Patching;




public abstract class BasePatcher
{
    protected BasePatcher()
    {
        Info = PatcherPluginInfoAttribute.FromType(GetType());

        Log = Logger.CreateLogSource(Info.Name);

        Config = new ConfigFile(Utility.CombinePaths(Paths.ConfigPath, Info.GUID + ".cfg"), false,
                                new BepInPlugin(Info.GUID, Info.Name, Info.Version.ToString()));
    }

    
    
    
    public ManualLogSource Log { get; }

    
    
    
    
    public ConfigFile Config { get; }

    
    
    
    public PatcherPluginInfoAttribute Info { get; }

    
    
    
    public PatcherContext Context { get; set; }

    
    
    
    public virtual void Initialize() { }

    
    
    
    public virtual void Finalizer() { }
}
