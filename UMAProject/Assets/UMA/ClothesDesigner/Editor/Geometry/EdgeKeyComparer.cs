using System.Collections.Generic;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Hashing for edge keys (two vertex indices packed in a long). .NET hashes a long by XOR-ing its two
    /// halves, so the edges (a, b) and (c, d) collide whenever a ^ b == c ^ d, which on a fine mesh is
    /// thousands at a time: a 23,000-vertex cloth took 3.5 s to list its edges. This mixes the bits first.
    /// </summary>
    public sealed class EdgeKeyComparer : IEqualityComparer<long>
    {
        public static readonly EdgeKeyComparer Instance = new EdgeKeyComparer();

        public bool Equals(long x, long y) => x == y;

        public int GetHashCode(long key)
        {
            ulong z = (ulong)key + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (int)(z ^ (z >> 31));
        }
    }
}
