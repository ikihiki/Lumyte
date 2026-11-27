namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Returns frame leases through the registered owner.</summary>
internal sealed class FrameOwner(GraphicsFrameScope value) : IAsyncDisposable
{
    internal GraphicsFrameScope Value => value;

    internal GraphicsPlanBuilder Plan => value.Plan;

    public static implicit operator GraphicsFrameScope(FrameOwner owner) => owner.Value;

    public ValueTask DisposeAsync() => value.RetireAsync();

    internal ValueTask<CompiledGraphicsPlan> CompileAsync() => value.CompileAsync();
}
