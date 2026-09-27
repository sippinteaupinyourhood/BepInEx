using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using StableNameDotNet;

namespace Cpp2IL.Core.ProcessingLayers;




public class StableRenamingProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "Stable (Unhollower-Style) Renaming";
    public override string Id => "stablenamer";

    private DeobfuscationMapProcessingLayer? _deobfuscationMapProcessingLayer;

    public override void PreProcess(ApplicationAnalysisContext context, List<Cpp2IlProcessingLayer> layers)
    {
        for (var i = 0; i < layers.Count; i++)
        {
            if (layers[i] is not DeobfuscationMapProcessingLayer deobf)
                continue;

            
            Logger.InfoNewline("Found DeobfuscationMapProcessingLayer. It will be run at the correct time", "StableRenamingProcessingLayer");
            _deobfuscationMapProcessingLayer = deobf;
            layers.RemoveAt(i);
            return;
        }
    }

    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        var stableNameStemCounts = new Dictionary<string, int>();

        var typesToProcess = appContext.AllTypes.Where(t => t is not InjectedTypeAnalysisContext).ToArray();

        
        foreach (var typeAnalysisContext in typesToProcess)
        {
            var stableName = StableNameGenerator.GetStableNameForTypeIfNeeded(typeAnalysisContext, false);

            if (stableName == null)
                
                continue;

            stableNameStemCounts[stableName] = stableNameStemCounts.GetOrCreate(stableName, () => 0) + 1;
            typeAnalysisContext.OverrideName = stableName;
        }

        StableNameGenerator.RenamedTypes.Clear();

        
        foreach (var typeAnalysisContext in typesToProcess)
        {
            if (typeAnalysisContext.OverrideName == null)
                
                continue;

            var dupeCount = stableNameStemCounts[typeAnalysisContext.OverrideName];
            if (dupeCount > 1)
                
                typeAnalysisContext.OverrideName = null;
            else
                
                StableNameGenerator.RenamedTypes[typeAnalysisContext] = typeAnalysisContext.OverrideName;
        }

        
        foreach (var typeAnalysisContext in typesToProcess)
        {
            if (typeAnalysisContext.OverrideName != null)
                
                continue;

            var stableName = StableNameGenerator.GetStableNameForTypeIfNeeded(typeAnalysisContext, true);

            if (stableName == null)
                
                continue;

            stableNameStemCounts[stableName] = stableNameStemCounts.GetOrCreate(stableName, () => 0) + 1;
            typeAnalysisContext.OverrideName = stableName;
        }

        
        var stableNameRenameCount = new Dictionary<string, int>();

        foreach (var typeAnalysisContext in typesToProcess)
        {
            if (typeAnalysisContext.OverrideName == null || !stableNameStemCounts.TryGetValue(typeAnalysisContext.OverrideName, out var count))
                continue;

            
            string? backTickSuffix = null;
            if (typeAnalysisContext.OverrideName.Length > 2 && typeAnalysisContext.OverrideName[^2] == '`')
            {
                backTickSuffix = typeAnalysisContext.OverrideName[^2..];
                typeAnalysisContext.OverrideName = typeAnalysisContext.OverrideName[..^2];
            }

            if (count == 1)
                typeAnalysisContext.OverrideName += "Unique";
            else
            {
                var thisCount = stableNameRenameCount[typeAnalysisContext.OverrideName] = stableNameRenameCount.GetOrCreate(typeAnalysisContext.OverrideName, () => -1) + 1;
                typeAnalysisContext.OverrideName += thisCount;
            }

            if (backTickSuffix != null)
                typeAnalysisContext.OverrideName += backTickSuffix;
        }

        
        foreach (var type in typesToProcess)
        {
            if (!type.IsEnumType)
                continue;

            
            foreach (var field in type.Fields)
            {
                if (!field.IsStatic || !field.Attributes.HasFlag(FieldAttributes.HasDefault))
                    continue;

                if (!StableNameGenerator.IsObfuscated(field.Name))
                    continue;

                field.OverrideName = $"EnumValue" + field.BackingData!.DefaultValue;
            }
        }

        
        if (_deobfuscationMapProcessingLayer != null)
        {
            Logger.InfoNewline("Running Deobfuscation Map Processing Layer Now...", "StableRenamingProcessingLayer");
            _deobfuscationMapProcessingLayer.Process(appContext);
            Logger.InfoNewline("Deobfuscation Map Processing Layer Finished.", "StableRenamingProcessingLayer");
        }

        
        foreach (var typeAnalysisContext in typesToProcess)
        {
            var typeMethodNames = new Dictionary<string, int>();
            foreach (var methodAnalysisContext in typeAnalysisContext.Methods)
            {
                if (methodAnalysisContext is InjectedMethodAnalysisContext)
                    continue;

                var stableName = StableNameGenerator.GetStableNameForMethodIfNeeded(methodAnalysisContext);

                
                for (var i = 0; i < methodAnalysisContext.Parameters.Count; i++)
                {
                    var param = methodAnalysisContext.Parameters[i];

                    if (StableNameGenerator.IsObfuscated(param.Name))
                        param.OverrideName = $"param_{i}";
                }

                if (stableName == null)
                    
                    continue;

                var occurenceCount = typeMethodNames.GetOrCreate(stableName, () => 0);
                typeMethodNames[stableName]++;
                methodAnalysisContext.OverrideName = $"{stableName}_{occurenceCount}";
            }
        }

        
        foreach (var typeAnalysisContext in typesToProcess)
        {
            var typeFieldNames = new Dictionary<string, int>();
            foreach (var fieldAnalysisContext in typeAnalysisContext.Fields)
            {
                if (fieldAnalysisContext is InjectedFieldAnalysisContext)
                    continue;

                var stableName = StableNameGenerator.GetStableNameForFieldIfNeeded(fieldAnalysisContext);

                if (stableName == null)
                    
                    continue;

                var occurenceCount = typeFieldNames.GetOrCreate(stableName, () => 0);
                typeFieldNames[stableName]++;
                fieldAnalysisContext.OverrideName = $"{stableName}_{occurenceCount}";
            }
        }

        
        foreach (var typeAnalysisContext in typesToProcess)
        {
            var typePropNames = new Dictionary<string, int>();
            foreach (var propAnalysisContext in typeAnalysisContext.Properties)
            {
                var stableName = StableNameGenerator.GetStableNameForPropertyIfNeeded(propAnalysisContext);

                if (stableName == null)
                    
                    continue;

                var occurenceCount = typePropNames.GetOrCreate(stableName, () => 0);
                typePropNames[stableName]++;
                propAnalysisContext.OverrideName = $"{stableName}_{occurenceCount}";
            }
        }

        
        foreach (var typeAnalysisContext in typesToProcess)
        {
            var typeEventNames = new Dictionary<string, int>();
            foreach (var eventAnalysisContext in typeAnalysisContext.Events)
            {
                var stableName = StableNameGenerator.GetStableNameForEventIfNeeded(eventAnalysisContext);

                if (stableName == null)
                    
                    continue;

                var occurenceCount = typeEventNames.GetOrCreate(stableName, () => 0);
                typeEventNames[stableName]++;
                eventAnalysisContext.OverrideName = $"{stableName}_{occurenceCount}";
            }
        }
    }
}
