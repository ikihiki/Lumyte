using Lumyte.Core.Time;

namespace Lumyte.Core.Tests.Time;

public sealed class DurationTests
{
    [Fact]
    public void ArithmeticPreservesTicks()
    {
        Duration first = Duration.FromSeconds(1.25);
        Duration second = Duration.FromSeconds(0.5);

        Assert.Equal(Duration.FromSeconds(1.75), first + second);
        Assert.Equal(Duration.FromSeconds(0.75), first - second);
    }

    [Fact]
    public void ConvertsToAndFromTimeSpan()
    {
        TimeSpan source = TimeSpan.FromMilliseconds(125);

        Assert.Equal(source, Duration.FromTimeSpan(source).ToTimeSpan());
    }
}
