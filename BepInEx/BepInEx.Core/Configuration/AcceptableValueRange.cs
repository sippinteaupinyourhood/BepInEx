using System;

namespace BepInEx.Configuration;




public class AcceptableValueRange<T> : AcceptableValueBase where T : IComparable
{
    
    
    public AcceptableValueRange(T minValue, T maxValue) : base(typeof(T))
    {
        if (maxValue == null)
            throw new ArgumentNullException(nameof(maxValue));
        if (minValue == null)
            throw new ArgumentNullException(nameof(minValue));
        if (minValue.CompareTo(maxValue) >= 0)
            throw new ArgumentException($"{nameof(minValue)} has to be lower than {nameof(maxValue)}");

        MinValue = minValue;
        MaxValue = maxValue;
    }

    
    
    
    public virtual T MinValue { get; }

    
    
    
    public virtual T MaxValue { get; }

    
    public override object Clamp(object value)
    {
        if (MinValue.CompareTo(value) > 0)
            return MinValue;

        if (MaxValue.CompareTo(value) < 0)
            return MaxValue;

        return value;
    }

    
    public override bool IsValid(object value) => MinValue.CompareTo(value) <= 0 && MaxValue.CompareTo(value) >= 0;

    
    public override string ToDescriptionString() => $"# Acceptable value range: From {MinValue} to {MaxValue}";
}
