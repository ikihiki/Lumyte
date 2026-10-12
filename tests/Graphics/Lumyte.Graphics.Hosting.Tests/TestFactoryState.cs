namespace Lumyte.Graphics.Hosting.Tests;

internal sealed class TestFactoryState
{
    internal List<TestDevice> Devices { get; } = [];

    internal TaskCompletionSource? CreationWait { get; set; }

    internal int Failures { get; set; }

    internal int Calls { get; set; }

    internal bool ReturnLost { get; set; }
}
