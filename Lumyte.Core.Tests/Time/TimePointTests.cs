using Lumyte.Core.Time;

namespace Lumyte.Core.Tests.Time;

public sealed class TimePointTests
{
    [Fact]
    public void SubtractionProducesDuration()
    {
        TimePoint start = TimePoint.FromTicks(10);
        TimePoint end = start + Duration.FromTicks(25);

        Assert.Equal(Duration.FromTicks(25), end - start);
    }
}
