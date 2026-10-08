namespace Lumyte.Graphics.Wgpu;

internal sealed record RenderPassDesc
{
    public required TextureView Target { get; init; }

    public LoadOp Load { get; init; } = LoadOp.Clear;

    public StoreOp Store { get; init; } = StoreOp.Store;

    public Color4 ClearValue { get; init; }
}
