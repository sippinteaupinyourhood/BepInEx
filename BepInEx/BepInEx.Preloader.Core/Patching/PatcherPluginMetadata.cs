using System.IO;
using BepInEx.Bootstrap;

namespace BepInEx.Preloader.Core.Patching;




internal class PatcherPluginMetadata : ICacheable
{
    
    
    
    public string TypeName { get; set; } = string.Empty;

    
    public void Save(BinaryWriter bw) => bw.Write(TypeName);

    
    public void Load(BinaryReader br) => TypeName = br.ReadString();
}
