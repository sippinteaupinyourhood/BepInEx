using System;
using System.Collections.Generic;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlInstructionSet
{
    
    
    
    
    
    
    public abstract Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator);

    
    
    
    
    
    
    public virtual ulong GetPointerForMethod(MethodAnalysisContext context) => context.UnderlyingPointer;

    
    
    
    
    
    
    
    public abstract List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context);

    
    
    
    
    public abstract BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance();

    
    
    
    
    
    public abstract string PrintAssembly(MethodAnalysisContext context);
}
