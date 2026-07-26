using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonNightSDK.Utility
{
    // Generic weighted random table: Add(value, weight) any number of times, Roll() picks one
    // proportionally to its weight relative to the total. Works for anything — item keys, loot
    // tiers, dialogue variants — the weights don't need to sum to 100, they're just relative.
    public sealed class WeightedDropTable<T>
    {
        private struct Entry
        {
            public T Value;
            public float Weight;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private float _totalWeight;

        public void Add(T value, float weight)
        {
            _entries.Add(new Entry { Value = value, Weight = weight });
            _totalWeight += weight;
        }

        public T Roll()
        {
            if (_entries.Count == 0)
                throw new InvalidOperationException("WeightedDropTable.Roll: no entries added — call Add() at least once first.");

            var roll = UnityEngine.Random.Range(0f, _totalWeight);
            var cumulative = 0f;
            foreach (var entry in _entries)
            {
                cumulative += entry.Weight;
                if (roll < cumulative)
                    return entry.Value;
            }
            // Float rounding at the very top of the range — last entry is the correct fallback.
            return _entries[_entries.Count - 1].Value;
        }
    }
}
