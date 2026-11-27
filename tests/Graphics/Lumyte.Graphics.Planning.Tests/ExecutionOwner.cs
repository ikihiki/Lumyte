namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Returns command memory through the registered owner.</summary>
internal sealed class ExecutionOwner(GraphicsExecution value) : IAsyncDisposable
{
    internal GraphicsExecution Value => value;

    public ValueTask DisposeAsync() => value.RetireAsync();
}
