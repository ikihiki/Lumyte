namespace Lumyte.Graphics;

/// <summary>
/// Records explicit transfers, compute work, and scoped render passes.
/// </summary>
public sealed class CommandEncoder : IDisposable
{
    private readonly IGraphicsDriver _driver;
    private readonly object _handle;

    internal CommandEncoder(IGraphicsDriver driver, object handle)
    {
        (_driver, _handle) = (driver, handle);
    }

    /// <summary>
    /// Begins a render pass; transfers and nested passes are forbidden until it ends.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The active render recording scope, which must end before more parent commands.</returns>
    public RenderEncoder BeginRenderPass(RenderPassDesc desc) => new(_driver, _driver.BeginRenderPass(_handle, desc));

    /// <summary>
    /// Records a compute dispatch with matching pipeline arguments; does not submit.
    /// </summary>
    /// <param name="pipeline">The matching pipeline from the same device.</param>
    /// <param name="arguments">The leased arguments created for the selected pipeline.</param>
    /// <param name="x">The X workgroup count.</param>
    /// <param name="y">The Y workgroup count.</param>
    /// <param name="z">The Z workgroup count.</param>
    public void Dispatch(ComputePipeline pipeline, ShaderArguments arguments, uint x, uint y = 1, uint z = 1) => _driver.Dispatch(_handle, pipeline, arguments, x, y, z);

    /// <summary>
    /// Records an aligned GPU copy between distinct buffers; requires explicit Finish and Submit.
    /// </summary>
    /// <typeparam name="TSource">The source unmanaged element type.</typeparam>
    /// <typeparam name="TDestination">The destination unmanaged element type.</typeparam>
    /// <param name="source">The caller-owned data or GPU source range.</param>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    public void RecordCopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
        => _driver.RecordCopyBuffer(_handle, source.Range, destination.Range);

    /// <summary>
    /// Records an RGBA8 image upload from a CopySource range with a 256-byte-aligned row pitch.
    /// </summary>
    /// <param name="source">The caller-owned data or GPU source range.</param>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    /// <param name="bytesPerRow">The buffer row pitch in bytes, a multiple of 256 with sufficient row storage.</param>
    public void RecordCopyBufferToTexture(BufferSlice<byte> source, IGraphicsTexture destination, uint bytesPerRow) => _driver.RecordCopyBufferToTexture(_handle, source.Range, destination, bytesPerRow);

    /// <summary>
    /// Records an RGBA8 image copy into caller-provided CopyDestination storage with aligned row pitch.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
    /// <param name="source">The caller-owned data or GPU source range.</param>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    /// <param name="bytesPerRow">The buffer row pitch in bytes, a multiple of 256 with sufficient row storage.</param>
    public void RecordCopyTextureToBuffer<T>(IGraphicsTexture source, IGraphicsBuffer<T> destination, uint bytesPerRow)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(destination);
        _driver.RecordCopyTextureToBuffer(_handle, source, destination.Slice(0, destination.Count).Range, bytesPerRow);
    }

    /// <summary>
    /// Finishes recording and transfers retained resources to a single-use command buffer.
    /// </summary>
    /// <returns>The owned, single-use finished command buffer.</returns>
    public CommandBuffer Finish() => new(_driver, _driver.Finish(_handle));

    /// <summary>
    /// Discards an unfinished encoder and releases its leases and pending material registrations.
    /// </summary>
    public void Dispose() => _driver.DisposeHandle(_handle);
}
