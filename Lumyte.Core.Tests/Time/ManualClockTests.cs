using Lumyte.Core.Time;

namespace Lumyte.Core.Tests.Time;

public sealed class ManualClockTests
{
    [Fact]
    public void AdvanceMovesClockForward()
    {
        var clock = new ManualClock();

        clock.Advance(Duration.FromSeconds(0.25));

        Assert.Equal(Duration.FromSeconds(0.25), clock.Now - TimePoint.Zero);
    }

    [Fact]
    public void AdvanceRejectsNegativeDuration()
    {
        var clock = new ManualClock();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(Duration.FromTicks(-1)));
    }
}
