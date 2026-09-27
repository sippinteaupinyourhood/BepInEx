using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Il2CppApiFunctions;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public abstract class BaseKeyFunctionAddresses
{
    public ulong il2cpp_codegen_initialize_method; 
    public ulong il2cpp_codegen_initialize_runtime_metadata; 
    public ulong il2cpp_vm_metadatacache_initializemethodmetadata; 
    public ulong il2cpp_runtime_class_init_export; 
    public ulong il2cpp_runtime_class_init_actual; 
    public ulong il2cpp_object_new; 
    public ulong il2cpp_vm_object_new; 
    public ulong il2cpp_codegen_object_new; 
    public ulong il2cpp_array_new_specific; 
    public ulong il2cpp_vm_array_new_specific; 
    public ulong SzArrayNew; 
    public ulong il2cpp_type_get_object; 
    public ulong il2cpp_vm_reflection_get_type_object; 
    public ulong il2cpp_resolve_icall; 
    public ulong InternalCalls_Resolve; 

    public ulong il2cpp_string_new; 
    public ulong il2cpp_vm_string_new; 
    public ulong il2cpp_string_new_wrapper; 
    public ulong il2cpp_vm_string_newWrapper; 
    public ulong il2cpp_codegen_string_new_wrapper; 

    public ulong il2cpp_value_box; 
    public ulong il2cpp_vm_object_box; 

    public ulong il2cpp_object_unbox; 
    public ulong il2cpp_vm_object_unbox; 

    public ulong il2cpp_raise_exception; 
    public ulong il2cpp_vm_exception_raise; 
    public ulong il2cpp_codegen_raise_exception; 

    public ulong il2cpp_vm_object_is_inst; 

    public ulong AddrPInvokeLookup; 

    public IEnumerable<KeyValuePair<string, ulong>> Pairs => resolvedAddressMap;

    private ApplicationAnalysisContext _appContext = null!; 

    private readonly Dictionary<string, ulong> resolvedAddressMap = [];
    private readonly HashSet<ulong> resolvedAddressSet = [];

    public bool IsKeyFunctionAddress(ulong address)
    {
        return address != 0 && resolvedAddressSet.Contains(address);
    }

    private void FindExport(string name, out ulong ptr)
    {
        Logger.Verbose($"\tLooking for Exported {name} function...");
        ptr = _appContext.Binary.GetVirtualAddressOfExportedFunctionByName(name);

        Logger.VerboseNewline(ptr == 0 ? "Not found" : $"Found at 0x{ptr:X}");
    }

    public virtual void Find(ApplicationAnalysisContext applicationAnalysisContext)
    {
        _appContext = applicationAnalysisContext;
        Init(applicationAnalysisContext);

        
        if (applicationAnalysisContext.Binary.InstructionSetId == DefaultInstructionSets.X86_32 || applicationAnalysisContext.Binary.InstructionSetId == DefaultInstructionSets.X86_64)
            
            TryGetInitMetadataFromException();

        
        FindExport("il2cpp_object_new", out il2cpp_object_new);

        
        FindExport("il2cpp_type_get_object", out il2cpp_type_get_object);

        
        FindExport("il2cpp_resolve_icall", out il2cpp_resolve_icall);

        
        FindExport("il2cpp_string_new", out il2cpp_string_new);

        
        FindExport("il2cpp_string_new_wrapper", out il2cpp_string_new_wrapper);

        
        FindExport("il2cpp_value_box", out il2cpp_value_box);

        
        FindExport("il2cpp_object_unbox", out il2cpp_object_unbox);

        
        FindExport("il2cpp_raise_exception", out il2cpp_raise_exception);

        
        FindExport("il2cpp_runtime_class_init", out il2cpp_runtime_class_init_export);

        
        FindExport("il2cpp_array_new_specific", out il2cpp_array_new_specific);

        
        il2cpp_vm_object_is_inst = GetObjectIsInstFromSystemType();

        AttemptInstructionAnalysisToFillGaps();

        FindThunks();
        InitializeResolvedAddresses();
    }

    protected void TryGetInitMetadataFromException()
    {
        
        Logger.VerboseNewline("\tLooking for Type System.Exception, Method get_Message...");

        var type = LibCpp2IlReflection.GetType("Exception", "System")!;
        Logger.VerboseNewline("\t\tType Located. Ensuring method exists...");
        var targetMethod = type.Methods!.FirstOrDefault(m => m.Name == "get_Message");
        if (targetMethod != null) 
        {
            Logger.VerboseNewline($"\t\tTarget Method Located at {targetMethod.MethodPointer}. Taking first CALL as the (version-specific) metadata initialization function...");

            var disasm = X86Utils.GetMethodBodyAtVirtAddressNew(targetMethod.MethodPointer, false);
            var calls = disasm.Where(i => i.Mnemonic == Mnemonic.Call).ToList();

            if (calls.Count == 0)
            {
                Logger.WarnNewline("Couldn't find any call instructions in the method body. This is not expected. Will not have metadata initialization function.");
                return;
            }

            if (_appContext.MetadataVersion < 27)
            {
                il2cpp_codegen_initialize_method = calls.First().NearBranchTarget;
                Logger.VerboseNewline($"\t\til2cpp_codegen_initialize_method => 0x{il2cpp_codegen_initialize_method:X}");
            }
            else
            {
                il2cpp_codegen_initialize_runtime_metadata = calls.First().NearBranchTarget;
                Logger.VerboseNewline($"\t\til2cpp_codegen_initialize_runtime_metadata => 0x{il2cpp_codegen_initialize_runtime_metadata:X}");
            }
        }
    }

    protected virtual void AttemptInstructionAnalysisToFillGaps()
    {
    }

    private void FindThunks()
    {
        if (il2cpp_object_new != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_object_new to vm::Object::New...");
            il2cpp_vm_object_new = FindFunctionThisIsAThunkOf(il2cpp_object_new, true);
            Logger.VerboseNewline($"Found at 0x{il2cpp_vm_object_new:X}");
        }

        if (il2cpp_vm_object_new != 0)
        {
            Logger.Verbose("\t\tLooking for il2cpp_codegen_object_new as a thunk of vm::Object::New...");

            var potentialThunks = FindAllThunkFunctions(il2cpp_vm_object_new, 16);

            
            var list = potentialThunks.Select(ptr => (ptr, count: GetCallerCount(ptr))).ToList();
            list.SortByExtractedKey(pair => pair.count);

            
            list.Reverse();

            
            il2cpp_codegen_object_new = list.FirstOrDefault().ptr;

            Logger.VerboseNewline($"Found at 0x{il2cpp_codegen_object_new:X}");
        }

        if (il2cpp_type_get_object != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_resolve_icall to Reflection::GetTypeObject...");
            il2cpp_vm_reflection_get_type_object = FindFunctionThisIsAThunkOf(il2cpp_type_get_object);
            Logger.VerboseNewline($"Found at 0x{il2cpp_vm_reflection_get_type_object:X}");
        }

        if (il2cpp_resolve_icall != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_resolve_icall to InternalCalls::Resolve...");
            InternalCalls_Resolve = FindFunctionThisIsAThunkOf(il2cpp_resolve_icall);
            Logger.VerboseNewline($"Found at 0x{InternalCalls_Resolve:X}");
        }

        if (il2cpp_string_new != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_string_new to String::New...");
            il2cpp_vm_string_new = FindFunctionThisIsAThunkOf(il2cpp_string_new);
            Logger.VerboseNewline($"Found at 0x{il2cpp_vm_string_new:X}");
        }

        if (il2cpp_string_new_wrapper != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_string_new_wrapper to String::NewWrapper...");
            il2cpp_vm_string_newWrapper = FindFunctionThisIsAThunkOf(il2cpp_string_new_wrapper);
            Logger.VerboseNewline($"Found at 0x{il2cpp_vm_string_newWrapper:X}");
        }

        if (il2cpp_vm_string_newWrapper != 0)
        {
            Logger.Verbose("\t\tMapping String::NewWrapper to il2cpp_codegen_string_new_wrapper...");
            il2cpp_codegen_string_new_wrapper = FindAllThunkFunctions(il2cpp_vm_string_newWrapper, 0, il2cpp_string_new_wrapper).FirstOrDefault();
            Logger.VerboseNewline($"Found at 0x{il2cpp_codegen_string_new_wrapper:X}");
        }

        if (il2cpp_value_box != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_value_box to Object::Box...");
            il2cpp_vm_object_box = FindFunctionThisIsAThunkOf(il2cpp_value_box);
            Logger.VerboseNewline($"Found at 0x{il2cpp_vm_object_box:X}");
        }

        if (il2cpp_object_unbox != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_object_unbox to Object::Unbox...");
            il2cpp_vm_object_unbox = FindFunctionThisIsAThunkOf(il2cpp_object_unbox);
            Logger.VerboseNewline($"Found at 0x{il2cpp_vm_object_unbox:X}");
        }

        if (il2cpp_raise_exception != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_raise_exception to il2cpp::vm::Exception::Raise...");
            il2cpp_vm_exception_raise = FindFunctionThisIsAThunkOf(il2cpp_raise_exception, true);
            Logger.VerboseNewline($"Found at 0x{il2cpp_vm_exception_raise:X}");
        }

        if (il2cpp_vm_exception_raise != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp::vm::Exception::Raise to il2cpp_codegen_raise_exception...");
            il2cpp_codegen_raise_exception = FindAllThunkFunctions(il2cpp_vm_exception_raise, 4, il2cpp_raise_exception).FirstOrDefault();
            Logger.VerboseNewline($"Found at 0x{il2cpp_codegen_raise_exception:X}");
        }

        if (il2cpp_runtime_class_init_export != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_runtime_class_init to il2cpp:vm::Runtime::ClassInit...");
            il2cpp_runtime_class_init_actual = FindFunctionThisIsAThunkOf(il2cpp_runtime_class_init_export);
            Logger.VerboseNewline($"Found at 0x{il2cpp_runtime_class_init_actual:X}");
        }

        if (il2cpp_array_new_specific != 0)
        {
            Logger.Verbose("\t\tMapping il2cpp_array_new_specific to vm::Array::NewSpecific...");
            il2cpp_vm_array_new_specific = FindFunctionThisIsAThunkOf(il2cpp_array_new_specific);
            Logger.VerboseNewline($"Found at 0x{il2cpp_vm_array_new_specific:X}");
        }

        if (il2cpp_vm_array_new_specific != 0)
        {
            Logger.Verbose("\t\tLooking for SzArrayNew as a thunk function proxying Array::NewSpecific...");
            SzArrayNew = FindAllThunkFunctions(il2cpp_vm_array_new_specific, 4, il2cpp_array_new_specific).FirstOrDefault();
            Logger.VerboseNewline($"Found at 0x{SzArrayNew:X}");
        }
    }

    protected abstract ulong GetObjectIsInstFromSystemType();

    
    
    
    
    
    
    
    protected abstract IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore);

    
    
    
    
    
    
    
    protected abstract ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false);

    protected abstract int GetCallerCount(ulong toWhere);

    protected virtual void Init(ApplicationAnalysisContext context)
    {
    }

    private void InitializeResolvedAddresses()
    {
        resolvedAddressMap.Clear();
        resolvedAddressSet.Clear();

        AddResolved(il2cpp_codegen_initialize_method);
        AddResolved(il2cpp_codegen_initialize_runtime_metadata);
        AddResolved(il2cpp_vm_metadatacache_initializemethodmetadata);
        AddResolved(il2cpp_runtime_class_init_export);
        AddResolved(il2cpp_runtime_class_init_actual);
        AddResolved(il2cpp_object_new);
        AddResolved(il2cpp_vm_object_new);
        AddResolved(il2cpp_codegen_object_new);
        AddResolved(il2cpp_array_new_specific);
        AddResolved(il2cpp_vm_array_new_specific);
        AddResolved(SzArrayNew);
        AddResolved(il2cpp_type_get_object);
        AddResolved(il2cpp_vm_reflection_get_type_object);
        AddResolved(il2cpp_resolve_icall);
        AddResolved(InternalCalls_Resolve);

        AddResolved(il2cpp_string_new);
        AddResolved(il2cpp_vm_string_new);
        AddResolved(il2cpp_string_new_wrapper);
        AddResolved(il2cpp_vm_string_newWrapper);
        AddResolved(il2cpp_codegen_string_new_wrapper);

        AddResolved(il2cpp_value_box);
        AddResolved(il2cpp_vm_object_box);

        AddResolved(il2cpp_object_unbox);
        AddResolved(il2cpp_vm_object_unbox);

        AddResolved(il2cpp_raise_exception);
        AddResolved(il2cpp_vm_exception_raise);
        AddResolved(il2cpp_codegen_raise_exception);

        AddResolved(il2cpp_vm_object_is_inst);

        AddResolved(AddrPInvokeLookup);

        void AddResolved(ulong address, [CallerArgumentExpression(nameof(address))] string name = "")
        {
            resolvedAddressSet.Add(address);
            resolvedAddressMap[name] = address;
        }
    }
}
