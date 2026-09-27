using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StableNameDotNet;





public class UniqueIdentifierGenerator
{
    
    
    
    public static int NumCharsToTakeFromEachInput = 2;

    public static int MaxInputs = 10;

    private readonly object _lock = new();

    private readonly Dictionary<string, int> _inputOccurrenceCounts = new();
    private readonly SortedSet<Input> _resultSet = [];

    
    
    
    public bool IsFull => _inputOccurrenceCounts.Count >= MaxInputs;

    
    
    
    
    
    public bool PushInput(string input)
    {
        lock (_lock)
        {
            
            
            

            if (input.ContainsAnyInvalidSourceCodeChars())
                return true; 

            var key = input.Substring(0, Math.Min(input.Length, NumCharsToTakeFromEachInput));
            var numTimesKeyOccurred = _inputOccurrenceCounts[key] = _inputOccurrenceCounts.GetOrCreate(key, () => 0) + 1;
            var numKeysSeen = _inputOccurrenceCounts.Count;
            var chronologicalOrderFactor = _resultSet.Count / 100f;

            var weight = numKeysSeen + numTimesKeyOccurred * 2 + chronologicalOrderFactor;

            _resultSet.Add(new(key, weight));

            return true;
        }
    }

    
    
    
    
    
    public bool PushInputs(List<string> inputs) => inputs.All(PushInput);

    
    
    
    public string GenerateUniqueName()
        => string.Join("", _resultSet.Take(MaxInputs).Select(x => x.Value));

    private struct Input(string value, float weight) : IComparable<Input>
    {
        public string Value = value;
        public float Weight = weight;

        public int CompareTo(Input other)
        {
            return Weight.CompareTo(other.Weight);
        }

        public override string ToString()
        {
            return $"{{{Value}, {Weight}}}";
        }
    }
}
