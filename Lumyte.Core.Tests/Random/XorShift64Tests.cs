using Lumyte.Core.Random;

namespace Lumyte.Core.Tests.Random;

public sealed class XorShift64Tests
{
    [Fact]
    public void EqualSeedsProduceEqualSequences()
    {
        var first = new XorShift64(42);
        var second = new XorShift64(42);

        for (int index = 0; index < 16; index++)
        {
            Assert.Equal(first.NextUInt64(), second.NextUInt64());
        }
    }

    [Fact]
    public void DefaultValueStillProducesASequence()
    {
        XorShift64 random = default;

        Assert.NotEqual(0UL, random.NextUInt64());
    }

    [Fact]
    public void NextDoubleStaysInsideUnitInterval()
    {
        var random = new XorShift64(1);

        for (int index = 0; index < 1_000; index++)
        {
            Assert.InRange(random.NextDouble(), 0, double.BitDecrement(1));
        }
    }
}
