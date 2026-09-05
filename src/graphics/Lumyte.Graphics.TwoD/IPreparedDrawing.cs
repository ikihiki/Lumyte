namespace Lumyte.Graphics.TwoD;

internal interface IPreparedDrawing
{
    Renderer Owner { get; }
    GpuTextureDescription TargetDescription { get; }
    IReadOnlyList<PreparedBatch> Batches { get; }
    IReadOnlyList<PreparedImage> Images { get; }
    IReadOnlyList<PreparedLayer> Layers { get; }
    OwnedBuffer? PrimitiveBuffer { get; }
    OwnedBuffer? PolygonBuffer { get; }
    OwnedBuffer? PathBuffer { get; }
    OwnedBuffer? LayerBuffer { get; }
    void VerifyAlive();
}
