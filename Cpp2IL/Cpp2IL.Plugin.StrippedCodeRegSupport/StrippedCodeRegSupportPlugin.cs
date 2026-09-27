using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Plugin.StrippedCodeRegSupport;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

[assembly: RegisterCpp2IlPlugin(typeof(StrippedCodeRegSupportPlugin))]

namespace Cpp2IL.Plugin.StrippedCodeRegSupport;

public class StrippedCodeRegSupportPlugin : Cpp2IlPlugin
{
    private const float MinSupportedMetadataVersion = 27f;

    public override string Name => "Stripped CodeReg Support";
    public override string Description => "Aims to help support games with CodeRegistration structs that have been inlined away by the compiler.";

    public override void OnLoad()
    {
        RegisterBinaryRegistrationFuncFallbackHandler(OnReadFail);
    }

    private void OnReadFail(Il2CppBinary binary, Il2CppMetadata metadata, ref Il2CppCodeRegistration? codereg, ref Il2CppMetadataRegistration? metareg)
    {
        if (codereg != null)
            
            return;

        
        Logger.InfoNewline("Received read failure for CodeRegistration, implying it may have been stripped. Attempting to work around...");

        if (metadata.MetadataVersion < MinSupportedMetadataVersion)
        {
            Logger.ErrorNewline($"This game's metadata version is too old to support this plugin (it needs to be at least Metadata {MinSupportedMetadataVersion:F1}, i.e. Unity 2020.2 or newer).");
            return;
        }

        
        
        var searcher = new BinarySearcher(binary, metadata.MethodDefinitionCount, metadata.TypeDefinitionCount);

        var mscorlibs = searcher.FindAllStrings("mscorlib.dll\0").Select(binary.MapRawAddressToVirtual).ToList();

        Logger.VerboseNewline($"Found {mscorlibs.Count} occurrences of mscorlib.dll: [{string.Join(", ", mscorlibs.Select(p => p.ToString("X")))}]");

        var pMscorlibCodegenModule = searcher.FindAllMappedWords(mscorlibs).ToList(); 

        Logger.VerboseNewline($"Found {pMscorlibCodegenModule.Count} potential codegen modules for mscorlib: [{string.Join(", ", pMscorlibCodegenModule.Select(p => p.ToString("X")))}]");

        
        var pMscorlibCodegenEntryInCodegenModulesList = searcher.FindAllMappedWords(pMscorlibCodegenModule).ToList(); 

        var moduleCount = (ulong)metadata.AssemblyDefinitions.Length;

        for (var i = 0; i < pMscorlibCodegenEntryInCodegenModulesList.Count; i++)
        {
            var pSomewhereInCodegenModules = pMscorlibCodegenEntryInCodegenModulesList[i];

            
            if (!TryReadCodeGenModuleFromPointer(binary, pSomewhereInCodegenModules))
                continue;

            
            if (!TryFindCodeGenModulesListFromMscorlibGoingForward(binary, pSomewhereInCodegenModules, moduleCount,
                    out var startOfCodegenModulesList, out var endOfCodegenModulesList))
            {
                continue;
            }

            Logger.VerboseNewline($"Found potential CodeGenModules list at 0x{startOfCodegenModulesList:X} (ending at 0x{endOfCodegenModulesList:X}). Returning dummy code reg struct now!");

            
            codereg = new() { codeGenModulesCount = (uint)moduleCount, addrCodeGenModulePtrs = startOfCodegenModulesList };

            return;
        }
    }

    private bool TryFindCodeGenModulesListFromMscorlibGoingForward(Il2CppBinary binary, ulong pSomewhereInCodegenModules, ulong moduleCount,
        out ulong startOfCodegenModulesList, out ulong endOfCodegenModulesList)
    {
        
        
        var pointerSize = binary.PointerSize;
        endOfCodegenModulesList = pSomewhereInCodegenModules;

        
        for (var i = 0UL; i <= moduleCount; i++)
        {
            if (!TryReadCodeGenModuleFromPointer(binary, endOfCodegenModulesList))
            {
                
                endOfCodegenModulesList -= pointerSize;

                
                startOfCodegenModulesList = endOfCodegenModulesList - ((moduleCount - 1) * pointerSize);
                return true;
            }
            endOfCodegenModulesList += pointerSize;
        }

        startOfCodegenModulesList = 0;
        return false;
    }

    private bool TryReadCodeGenModuleFromPointer(Il2CppBinary binary, ulong pointerToPossibleCodeGenEntry)
    {
        
        var firstModulePtr = binary.ReadPointerAtVirtualAddress(pointerToPossibleCodeGenEntry);
        if (firstModulePtr == 0)
        {
            Logger.VerboseNewline($"Discarding possible CodeGenModule entry at 0x{pointerToPossibleCodeGenEntry:X} because it is null.");
            return false;
        }

        
        Il2CppCodeGenModule? codeGenModule;
        try
        {
            codeGenModule = binary.ReadReadableAtVirtualAddress<Il2CppCodeGenModule>(firstModulePtr);
            if (codeGenModule.Name is not { } name || string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAscii(c)))
            {
                Logger.VerboseNewline($"Discarding possible CodeGenModule entry at 0x{pointerToPossibleCodeGenEntry:X} because module name is invalid.");
                return false;
            }
        }
        catch (Exception e)
        {
            Logger.VerboseNewline($"Discarding possible CodeGenModule at 0x{pointerToPossibleCodeGenEntry:X} because we hit a {e.GetType().Name} when trying to read it as a CodeGenModule.");
            return false;
        }

        Logger.VerboseNewline($"Accepting 0x{pointerToPossibleCodeGenEntry:X} as a CodeGenModule entry with valid module name {codeGenModule.Name}.");
        return true;
    }
}
