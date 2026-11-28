namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Returns prepared allocations through the registered owner.</summary>
internal sealed class PreparedOwner(PreparedGraphicsExecution value) : IAsyncDisposable
{
    internal PreparedGraphicsExecution Value => value;

    public ValueTask DisposeAsync() => value.RetireAsync();
}
