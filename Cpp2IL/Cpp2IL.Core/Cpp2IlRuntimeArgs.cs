using System.Collections.Generic;
using AssetRipper.Primitives;
using Cpp2IL.Core.Api;

namespace Cpp2IL.Core;

public class Cpp2IlRuntimeArgs
{
    
    public bool Valid;

    
    public UnityVersion UnityVersion;
    public string PathToAssembly = null!;
    public string PathToMetadata = null!;

    public string? WasmFrameworkJsFile;

    public List<Cpp2IlProcessingLayer> ProcessingLayersToRun = [];
    public readonly Dictionary<string, string> ProcessingLayerConfigurationOptions = new();

    public Cpp2IlOutputFormat? OutputFormat;
    public string OutputRootDirectory = null!;

    public bool LowMemoryMode;
}
