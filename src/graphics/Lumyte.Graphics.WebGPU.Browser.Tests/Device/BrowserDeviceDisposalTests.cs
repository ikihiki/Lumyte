namespace Lumyte.Graphics.WebGPU.Browser.Tests;

public sealed class BrowserDeviceDisposalTests
{
    [Fact]
    public void FailedInteropDestructionRetainsPendingCommandStorage()
    {
        var expected = new InvalidOperationException("device.destroy did not reach the runtime.");
        bool released = false;

        var failure = Assert.Throws<InvalidOperationException>(() => BrowserDeviceDisposal.Destroy(
            () => throw expected, () => released = true));

        Assert.Same(expected, failure);
        Assert.False(released);
    }
}
