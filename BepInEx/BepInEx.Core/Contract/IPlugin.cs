using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace BepInEx.Contract
{
  public interface IPlugin
  {
	  
	  
	  
	  PluginInfo Info { get; }

	  
	  
	  
	  ManualLogSource Logger { get; }

	  
	  
	  
	  
	  ConfigFile Config { get; }
  }
}
