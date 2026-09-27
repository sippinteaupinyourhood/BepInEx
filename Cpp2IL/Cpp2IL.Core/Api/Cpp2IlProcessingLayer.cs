using System;
using System.Collections.Generic;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlProcessingLayer
{
    
    
    
    public abstract string Name { get; }

    
    
    
    public abstract string Id { get; }

    
    
    
    
    
    public virtual void PreProcess(ApplicationAnalysisContext context, List<Cpp2IlProcessingLayer> layers)
    {
    }

    
    
    
    
    
    
    
    
    public abstract void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null);
}
