#nullable disable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
namespace MouseMaster
{
    public class FastRng
    {
        private ulong _s0, _s1, _s2, _s3;
        public FastRng(ulong seed)
        {
            _s0 = SplitMix64(ref seed);
            _s1 = SplitMix64(ref seed);
            _s2 = SplitMix64(ref seed);
            _s3 = SplitMix64(ref seed);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SplitMix64(ref ulong state)
        {
            state += 0x9e3779b97f4a7c15u;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9u;
            z = (z ^ (z >> 27)) * 0x94d049bb133111ebu;
            return z ^ (z >> 31);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Next(int max)
        {
            if (max <= 0) return 0;
            return (int)(NextU64() % (uint)max);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Next(int min, int max)
        {
            if (min >= max) return min;
            return min + Next(max - min);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double NextDouble()
        {
            return (NextU64() >> 11) * (1.0 / (1ul << 53));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextU64()
        {
            ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = BitOperations.RotateLeft(_s3, 45);
            return result;
        }
    }
}