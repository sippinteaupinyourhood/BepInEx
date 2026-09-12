using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Il2CppInterop.Common.XrefScans;
using TerraFX.Interop.Windows;

namespace Il2CppInterop.Runtime;

internal class MemoryUtils
{
    public static unsafe nint FindSignatureInModule(ProcessModule module, SignatureDefinition sigDef)
    {
        if (sigDef.pattern is null || sigDef.mask is null) throw new ArgumentException("Missing native signature pattern.");
        ValidatePattern(sigDef.pattern.ToCharArray(), sigDef.mask.ToCharArray());
        // On newer Unity (6000.x) the loaded GameAssembly maps some pages PAGE_NOACCESS / guard pages; the raw
        // linear byte walk in FindSignatureInBlock dereferences them and throws a fatal AccessViolationException.
        // Use VirtualQuery to enumerate the module's regions and scan only the readable committed ones, skipping
        // the rest -- without ever modifying page protections. VirtualQuery and the TerraFX MEMORY_BASIC_INFORMATION
        // fields are Windows-only (the struct is annotated for Windows 6.1+); elsewhere (where this guard-page issue
        // does not arise) fall back to the plain whole-module scan.
        nint ptr = 0;
        if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            const uint pageReadable = PAGE.PAGE_EXECUTE_READ | PAGE.PAGE_EXECUTE_READWRITE | PAGE.PAGE_EXECUTE_WRITECOPY;
            var regions = GetModuleRegions(module);
            foreach (var region in regions)
            {
                if (region.State != MEM.MEM_COMMIT || (region.Protect & PAGE.PAGE_GUARD) != 0 ||
                    (region.Protect & pageReadable) == 0)
                    continue;
                nint candidate = FindSignatureInBlock((nint)region.BaseAddress, (long)region.RegionSize,
                    sigDef.pattern, sigDef.mask, sigDef.offset);
                if (candidate == 0) continue;
                if (ptr != 0) throw new AmbiguousMatchException("Native signature matched multiple executable regions.");
                ptr = candidate;
            }
        }
        else
        {
            ptr = FindSignatureInBlock(module.BaseAddress, module.ModuleMemorySize,
                sigDef.pattern, sigDef.mask, sigDef.offset);
        }

        if (ptr != 0 && sigDef.xref)
            ptr = XrefScannerLowLevel.JumpTargets(ptr).Distinct().SingleOrDefault();
        return ptr;
    }

    public static nint FindSignatureInBlock(nint block, long blockSize, string pattern, string mask, long sigOffset = 0)
    {
        return FindSignatureInBlock(block, blockSize, pattern.ToCharArray(), mask.ToCharArray(), sigOffset);
    }

    public static unsafe nint FindSignatureInBlock(nint block, long blockSize, char[] pattern, char[] mask,
        long sigOffset = 0)
    {
        ValidatePattern(pattern, mask);
        if (block == 0 || blockSize < 0 || (ulong)blockSize > (ulong)nuint.MaxValue - (ulong)(nuint)block)
            throw new ArgumentOutOfRangeException(nameof(blockSize));
        nint result = 0;
        // Stop at blockSize - mask.Length so the inner read (block + address + mask.Length - 1) never passes the
        // end of this block. When the caller scans per-region (Unity 6 readable regions interleaved with guard /
        // PAGE_NOACCESS pages), an overread off the tail would fault the adjacent page -> a fatal
        // AccessViolationException that aborts the chainloader. If the block is smaller than the mask, scan nothing.
        for (long address = 0; address <= blockSize - mask.Length; address++)
        {
            var found = true;
            for (uint offset = 0; offset < mask.Length; offset++)
                if (*(byte*)(address + block + offset) != (byte)pattern[offset] && mask[offset] != '?')
                {
                    found = false;
                    break;
                }

            if (found)
            {
                if (result != 0) throw new AmbiguousMatchException("Native signature matched multiple locations.");
                long target = checked(address + sigOffset);
                if (target < 0 || target >= blockSize)
                    throw new ArgumentOutOfRangeException(nameof(sigOffset));
                result = (nint)((nuint)block + (nuint)target);
            }
        }

        return result;
    }

    private static void ValidatePattern(char[] pattern, char[] mask)
    {
        if (pattern is null || mask is null || pattern.Length == 0 || pattern.Length > 512 ||
            pattern.Length != mask.Length || pattern.Any(value => value > byte.MaxValue) ||
            mask.Any(value => value != 'x' && value != '?') || !mask.Contains('x'))
            throw new ArgumentException("Invalid native signature pattern.");
    }

    /// <summary>
    /// Walks the module's address space via <c>VirtualQuery</c>, collecting each memory region so the scan can pick
    /// the readable committed ones. Stops at the first <c>VirtualQuery</c> failure or once the module end is reached.
    /// </summary>
    [SupportedOSPlatform("windows6.1")]
    internal static unsafe List<MEMORY_BASIC_INFORMATION> GetModuleRegions(ProcessModule module)
    {
        var regions = new List<MEMORY_BASIC_INFORMATION>();
        var moduleEndAddress = (long)module.BaseAddress + module.ModuleMemorySize;
        var currentAddress = (long)module.BaseAddress;
        while (currentAddress < moduleEndAddress)
        {
            MEMORY_BASIC_INFORMATION memoryInfo = default;
            var result = Windows.VirtualQuery((void*)currentAddress, &memoryInfo, (nuint)sizeof(MEMORY_BASIC_INFORMATION));
            if (result == 0)
                break; // error, or reached the end of the module's mapped memory

            long regionStart = (long)memoryInfo.BaseAddress;
            long regionEnd = checked(regionStart + (long)memoryInfo.RegionSize);
            if (regionEnd <= currentAddress) throw new InvalidOperationException("Memory region enumeration did not advance.");
            long clippedStart = Math.Max(currentAddress, regionStart);
            long clippedEnd = Math.Min(moduleEndAddress, regionEnd);
            if (clippedEnd > clippedStart)
            {
                memoryInfo.BaseAddress = (void*)clippedStart;
                memoryInfo.RegionSize = (nuint)(clippedEnd - clippedStart);
                regions.Add(memoryInfo);
            }
            currentAddress = regionEnd;
        }

        return regions;
    }

    public struct SignatureDefinition
    {
        public string pattern;
        public string mask;
        public int offset;
        public bool xref;
    }
}
