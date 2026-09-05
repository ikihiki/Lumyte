namespace Lumyte.Graphics.TwoD;

internal interface IPreparedDrawing
{
    Renderer Owner { get; }
    GpuTextureDescription TargetDescription { get; }
    IReadOnlyList<PreparedBatch> Batches { get; }
    IReadOnlyList<PreparedImage> Images { get; }
    OwnedBuffer? PrimitiveBuffer { get; }
    OwnedBuffer? PolygonBuffer { get; }
    OwnedBuffer? PathBuffer { get; }
    void VerifyAlive();
}
