using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BepInEx.Unity.Mono;




public abstract class BaseUnityPlugin : MonoBehaviour
{
    
    
    
    
    protected BaseUnityPlugin()
    {
        var metadata = MetadataHelper.GetMetadata(this);
        if (metadata == null)
            throw new InvalidOperationException("Can't create an instance of " + GetType().FullName +
                                                " because it inherits from BaseUnityPlugin and the BepInPlugin attribute is missing.");

        Info = new PluginInfo
        {
            Metadata = metadata,
            Instance = this,
            Dependencies = MetadataHelper.GetDependencies(GetType()),
            Processes = MetadataHelper.GetAttributes<BepInProcess>(GetType()),
            Location = GetType().Assembly.Location
        };

        Logger = BepInEx.Logging.Logger.CreateLogSource(metadata.Name);

        Config = new ConfigFile(Utility.CombinePaths(Paths.ConfigPath, metadata.GUID + ".cfg"), false, metadata);
    }

    
    
    
    public PluginInfo Info { get; }

    
    
    
    protected ManualLogSource Logger { get; }

    
    
    
    
    public ConfigFile Config { get; }
}
