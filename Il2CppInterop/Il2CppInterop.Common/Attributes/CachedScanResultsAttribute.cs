namespace Il2CppInterop.Common.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class CachedScanResultsAttribute : Attribute
{
    
    public long MetadataInitFlagRva;
    public long MetadataInitTokenRva;
    public int RefRangeEnd;

    
    public int RefRangeStart;

    public int XrefRangeEnd;

    
    public int XrefRangeStart;
}
