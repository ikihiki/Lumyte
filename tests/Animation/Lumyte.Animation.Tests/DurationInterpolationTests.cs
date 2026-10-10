using Lumyte.Core.Time;
using Xunit;

namespace Lumyte.Animation.Tests;

/// <summary>Verifies tick rounding through the animation duration adapter.</summary>
public sealed class DurationInterpolationTests
{
    /// <summary>Uses exact binary weights when a duration lies halfway between ticks.</summary>
    /// <param name="from">The starting tick count.</param>
    /// <param name="to">The ending tick count.</param>
    /// <param name="amount">The binary floating-point weight.</param>
    /// <param name="expected">The correctly rounded tick count.</param>
    [Theory]
    [InlineData(0, 4194304, 0.9f, 3774874)]
    [InlineData(1, 67108865, 0.1f, 6710888)]
    [InlineData(-1, -67108865, 0.1f, -6710888)]
    public void DurationRoundsExactBinaryWeights(long from, long to, float amount, long expected) => Assert.Equal(Duration.FromTicks(expected), AnimationInterpolators.Duration.Interpolate(Duration.FromTicks(from), Duration.FromTicks(to), amount));
}
