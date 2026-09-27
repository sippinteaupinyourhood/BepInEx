using System;

namespace BepInEx.Configuration;





public sealed class SettingChangedEventArgs : EventArgs
{
    
    public SettingChangedEventArgs(ConfigEntryBase changedSetting)
    {
        ChangedSetting = changedSetting;
    }

    
    
    
    public ConfigEntryBase ChangedSetting { get; }
}
