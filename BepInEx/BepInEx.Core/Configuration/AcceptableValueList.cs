using System;
using System.Linq;

namespace BepInEx.Configuration;




public class AcceptableValueList<T> : AcceptableValueBase where T : IEquatable<T>
{
    
    
    
    
    public AcceptableValueList(params T[] acceptableValues) : base(typeof(T))
    {
        if (acceptableValues == null) throw new ArgumentNullException(nameof(acceptableValues));
        if (acceptableValues.Length == 0)
            throw new ArgumentException("At least one acceptable value is needed", nameof(acceptableValues));

        AcceptableValues = acceptableValues;
    }

    
    
    
    public virtual T[] AcceptableValues { get; }

    
    public override object Clamp(object value)
    {
        if (IsValid(value))
            return value;

        return AcceptableValues[0];
    }

    
    public override bool IsValid(object value) => value is T v && AcceptableValues.Any(x => x.Equals(v));

    
    public override string ToDescriptionString() =>
        "# Acceptable values: " + string.Join(", ", AcceptableValues.Select(x => x.ToString()).ToArray());
}
