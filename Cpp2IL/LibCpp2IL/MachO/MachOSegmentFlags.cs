using System;

namespace LibCpp2IL.MachO;

[Flags]
public enum MachOSegmentFlags
{
    SG_NONE = 0x0,

    SG_HIGHVM = 0x1, 
    SG_FVMLIB = 0x2, 
    SG_NORELOC = 0x4, 
    SG_PROTECTED_VERSION_1 = 0x8, 
}
