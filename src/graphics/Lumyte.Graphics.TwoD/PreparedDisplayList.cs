namespace Lumyte.Graphics.TwoD;

/// <summary>GPU-ready immutable data. Keep it alive until graph execution completes.</summary>
public sealed class PreparedDisplayList : IDisposable, IPreparedDrawing
{
    private readonly object sync = new();
    private OwnedBuffer? primitiveBuffer;
    private OwnedBuffer? polygonBuffer;
    private OwnedBuffer? pathBuffer;
    private OwnedBuffer? layerBuffer;
    private bool disposed;
    private int leaseCount;
    private bool disposeRequested;

    internal PreparedDisplayList(
        Renderer owner,
        GpuTextureDescription targetDescription,
        int commandCount,
        PreparedBatch[] batches,
        PreparedImage[] images,
        PreparedLayer[] layers,
        OwnedBuffer? primitiveBuffer,
        OwnedBuffer? polygonBuffer,
        OwnedBuffer? pathBuffer,
        OwnedBuffer? layerBuffer)
    {
        Owner = owner;
        TargetDescription = targetDescription;
        CommandCount = commandCount;
        Batches = batches;
        Images = images;
        Layers = layers;
        this.primitiveBuffer = primitiveBuffer;
        this.polygonBuffer = polygonBuffer;
        this.pathBuffer = pathBuffer;
        this.layerBuffer = layerBuffer;
    }

    public int CommandCount { get; }
    public bool IsEmpty => CommandCount == 0;
    public GpuTextureDescription TargetDescription { get; }

    internal Renderer Owner { get; }
    internal IReadOnlyList<PreparedBatch> Batches { get; }
    internal IReadOnlyList<PreparedImage> Images { get; }
    internal IReadOnlyList<PreparedLayer> Layers { get; }
    internal OwnedBuffer? PrimitiveBuffer => RequireAlive(primitiveBuffer);
    internal OwnedBuffer? PolygonBuffer => RequireAlive(polygonBuffer);
    internal OwnedBuffer? PathBuffer => RequireAlive(pathBuffer);
    internal OwnedBuffer? LayerBuffer => RequireAlive(layerBuffer);

    Renderer IPreparedDrawing.Owner => Owner;
    IReadOnlyList<PreparedBatch> IPreparedDrawing.Batches => Batches;
    IReadOnlyList<PreparedImage> IPreparedDrawing.Images => Images;
    IReadOnlyList<PreparedLayer> IPreparedDrawing.Layers => Layers;
    OwnedBuffer? IPreparedDrawing.PrimitiveBuffer => PrimitiveBuffer;
    OwnedBuffer? IPreparedDrawing.PolygonBuffer => PolygonBuffer;
    OwnedBuffer? IPreparedDrawing.PathBuffer => PathBuffer;
    OwnedBuffer? IPreparedDrawing.LayerBuffer => LayerBuffer;

    public void Dispose()
    {
        lock (sync)
        {
            if (disposeRequested) { return; }
            disposeRequested = true;
            if (leaseCount != 0) { return; }
            DestroyBuffers();
        }
    }

    public IDisposable AcquireLease()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposeRequested, this);
            leaseCount++;
            return new Lease(this);
        }
    }

    private void ReleaseLease()
    {
        lock (sync)
        {
            if (leaseCount <= 0) { throw new InvalidOperationException("Prepared drawing lease was released more than once."); }
            leaseCount--;
            if (leaseCount == 0 && disposeRequested) { DestroyBuffers(); }
        }
    }

    private void DestroyBuffers()
    {
        if (disposed) { return; }
        pathBuffer?.Dispose();
        layerBuffer?.Dispose();
        polygonBuffer?.Dispose();
        primitiveBuffer?.Dispose();
        polygonBuffer = null;
        pathBuffer = null;
        primitiveBuffer = null;
        layerBuffer = null;
        disposed = true;
    }

    internal void VerifyAlive() => ObjectDisposedException.ThrowIf(disposed, this);

    void IPreparedDrawing.VerifyAlive() => VerifyAlive();

    private T? RequireAlive<T>(T? value) where T : class
    {
        VerifyAlive();
        return value;
    }

    private sealed class Lease(PreparedDisplayList owner) : IDisposable
    {
        private PreparedDisplayList? owner = owner;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.ReleaseLease();
    }
}
