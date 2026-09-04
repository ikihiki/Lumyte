namespace Lumyte.Graphics.TwoD;

/// <summary>GPU-ready immutable data. Keep it alive until graph execution completes.</summary>
public sealed class PreparedDisplayList : IDisposable, IPreparedDrawing
{
    private OwnedBuffer? primitiveBuffer;
    private OwnedBuffer? polygonBuffer;
    private bool disposed;

    internal PreparedDisplayList(
        Renderer owner,
        GpuTextureDescription targetDescription,
        int commandCount,
        PreparedBatch[] batches,
        PreparedImage[] images,
        OwnedBuffer? primitiveBuffer,
        OwnedBuffer? polygonBuffer)
    {
        Owner = owner;
        TargetDescription = targetDescription;
        CommandCount = commandCount;
        Batches = batches;
        Images = images;
        this.primitiveBuffer = primitiveBuffer;
        this.polygonBuffer = polygonBuffer;
    }

    public int CommandCount { get; }
    public bool IsEmpty => CommandCount == 0;
    public GpuTextureDescription TargetDescription { get; }

    internal Renderer Owner { get; }
    internal IReadOnlyList<PreparedBatch> Batches { get; }
    internal IReadOnlyList<PreparedImage> Images { get; }
    internal OwnedBuffer? PrimitiveBuffer => RequireAlive(primitiveBuffer);
    internal OwnedBuffer? PolygonBuffer => RequireAlive(polygonBuffer);

    Renderer IPreparedDrawing.Owner => Owner;
    IReadOnlyList<PreparedBatch> IPreparedDrawing.Batches => Batches;
    IReadOnlyList<PreparedImage> IPreparedDrawing.Images => Images;
    OwnedBuffer? IPreparedDrawing.PrimitiveBuffer => PrimitiveBuffer;
    OwnedBuffer? IPreparedDrawing.PolygonBuffer => PolygonBuffer;

    public void Dispose()
    {
        if (disposed) { return; }
        polygonBuffer?.Dispose();
        primitiveBuffer?.Dispose();
        polygonBuffer = null;
        primitiveBuffer = null;
        disposed = true;
    }

    internal void VerifyAlive() => ObjectDisposedException.ThrowIf(disposed, this);

    void IPreparedDrawing.VerifyAlive() => VerifyAlive();

    private T? RequireAlive<T>(T? value) where T : class
    {
        VerifyAlive();
        return value;
    }
}
