namespace Lumyte.Graphics;

/// <summary>
/// Records drawing within an active render pass.
/// </summary>
public sealed class RenderEncoder : IDisposable
{
    private readonly IGraphicsDriver _driver;
    private readonly object _handle;

    internal RenderEncoder(IGraphicsDriver driver, object handle)
    {
        (_driver, _handle) = (driver, handle);
    }

    /// <summary>
    /// Selects a graphics pipeline and clears previously selected material arguments.
    /// </summary>
    /// <param name="pipeline">The matching pipeline from the same device.</param>
    public void SetPipeline(GraphicsPipeline pipeline) => _driver.SetPipeline(_handle, pipeline);

    /// <summary>
    /// Sets a finite viewport contained in the target, with a depth range within zero and one.
    /// </summary>
    /// <param name="viewport">The viewport contained in the target image.</param>
    public void SetViewport(Viewport viewport) => _driver.SetViewport(_handle, viewport);

    /// <summary>
    /// Sets a scissor rectangle contained in the render target.
    /// </summary>
    /// <param name="scissor">The scissor rectangle contained in the target image.</param>
    public void SetScissor(Scissor scissor) => _driver.SetScissor(_handle, scissor);

    /// <summary>
    /// Selects an index range whose element type and alignment match the chosen format.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
    /// <param name="indices">The non-owning typed index-buffer range.</param>
    /// <param name="format">The representation matching the index element type.</param>
    public void SetIndexBuffer<T>(BufferSlice<T> indices, IndexFormat format)
        where T : unmanaged
    {
        if ((format == IndexFormat.Uint16 && typeof(T) != typeof(ushort)) || (format == IndexFormat.Uint32 && typeof(T) != typeof(uint)))
        {
            throw new ArgumentException("Index element type must match the selected format.", nameof(indices));
        }

        _driver.SetIndexBuffer(_handle, indices.Range, format);
    }

    /// <summary>
    /// Records a draw; material pipelines require valid matching arguments and cannot sample the active target.
    /// </summary>
    /// <param name="vertexCount">The number of vertices per instance.</param>
    /// <param name="instanceCount">The number of instances.</param>
    public void Draw(uint vertexCount, uint instanceCount = 1) => Draw(new DrawDesc { VertexCount = vertexCount, InstanceCount = instanceCount });

    /// <summary>
    /// Records a draw; material pipelines require valid matching arguments and cannot sample the active target.
    /// </summary>
    /// <param name="arguments">The leased arguments created for the selected pipeline.</param>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    public void Draw(ShaderArguments arguments, DrawDesc desc) => _driver.DrawWithArguments(_handle, arguments, desc);

    /// <summary>
    /// Records a draw; material pipelines require valid matching arguments and cannot sample the active target.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    public void Draw(DrawDesc desc) => _driver.Draw(_handle, desc);

    /// <summary>
    /// Records an indexed draw within the selected index range and pipeline.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    public void DrawIndexed(IndexedDrawDesc desc) => _driver.DrawIndexed(_handle, desc);

    /// <summary>
    /// Ends the pass so the parent encoder can record more commands.
    /// </summary>
    public void End() => _driver.End(_handle);

    /// <summary>
    /// Ends an active pass; repeated disposal is harmless.
    /// </summary>
    public void Dispose() => _driver.DisposeHandle(_handle);
}
