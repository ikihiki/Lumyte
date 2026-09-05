namespace Lumyte.Core.Random;

/// <summary>A small allocation-free deterministic pseudo-random number generator.</summary>
public struct XorShift64
{
    private const ulong DefaultSeed = 0x9E3779B97F4A7C15;
    private ulong state;

    public XorShift64(ulong seed) => state = seed == 0 ? DefaultSeed : seed;

    public ulong NextUInt64()
    {
        ulong value = state == 0 ? DefaultSeed : state;
        value ^= value << 13;
        value ^= value >> 7;
        value ^= value << 17;
        state = value;
        return value;
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public float NextSingle() => (float)NextDouble();

    public float NextSingle(float minimum, float maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum must not exceed maximum.");
        }

        return minimum + ((maximum - minimum) * NextSingle());
    }
}
