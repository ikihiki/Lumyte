using Lumyte.Core.Errors;

namespace Lumyte.Core.Tests.Errors;

public sealed class CoreErrorTests
{
    [Fact]
    public void RequiresCodeAndMessage()
    {
        Assert.Throws<ArgumentException>(() => new CoreError("", "message"));
        Assert.Throws<ArgumentException>(() => new CoreError("code", ""));
    }
}
