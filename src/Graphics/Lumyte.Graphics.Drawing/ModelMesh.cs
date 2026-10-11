using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Owns model vertex/index storage and staging; upload, submission and lifetime remain explicit.</summary>
public sealed class ModelMesh : IDisposable
{
    private readonly IGraphicsShaderDataBuffer<ModelVertex> _vertexUpload;
    private readonly IGraphicsBuffer<uint> _indexUpload;

    private ModelMesh(IGraphicsShaderDataBuffer<ModelVertex> vertices, IGraphicsShaderDataBuffer<ModelVertex> vertexUpload, IGraphicsBuffer<uint> indices, IGraphicsBuffer<uint> indexUpload)
    {
        Vertices = vertices;
        Indices = indices;
        _vertexUpload = vertexUpload;
        _indexUpload = indexUpload;
    }

    /// <summary>Gets shader vertex storage to register in an argument table.</summary>
    public IGraphicsShaderDataBuffer<ModelVertex> Vertices { get; }

    /// <summary>Gets the GPU triangle index storage.</summary>
    public IGraphicsBuffer<uint> Indices { get; }

    /// <summary>Allocates storage and fills upload staging without recording or submitting GPU commands.</summary>
    /// <param name="device">The owning device.</param>
    /// <param name="vertexData">The object-space vertices in the common model layout.</param>
    /// <param name="indexData">The in-range thirty-two-bit triangle-list indices.</param>
    /// <param name="vertexArtifact">The model vertex shader artifact.</param>
    /// <returns>The mesh with filled, unmapped staging.</returns>
    public static async Task<ModelMesh> CreateAsync(IGraphicDevice device, ReadOnlyMemory<ModelVertex> vertexData, ReadOnlyMemory<uint> indexData, ShaderArtifact vertexArtifact)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(vertexArtifact);
        if (vertexData.Length == 0 || indexData.Length == 0 || indexData.Length % 3 != 0 || ContainsInvalidIndex(indexData.Span, vertexData.Length))
        {
            throw new ArgumentException("A mesh requires in-range triangle indices and vertices.", nameof(indexData));
        }

        IGraphicsShaderDataBuffer<ModelVertex>? vertices = null;
        IGraphicsShaderDataBuffer<ModelVertex>? vertexUpload = null;
        IGraphicsBuffer<uint>? indices = null;
        IGraphicsBuffer<uint>? indexUpload = null;
        try
        {
            ulong vertexCount = (ulong)vertexData.Length;
            ulong indexCount = (ulong)indexData.Length;
            vertices = device.CreateBuffer<ModelVertex>(vertexArtifact, vertexCount);
            vertexUpload = device.CreateBuffer<ModelVertex>(vertexArtifact, vertexCount, MemoryPreference.Upload);
            indices = device.CreateBuffer<uint>(new() { Count = indexCount, Usage = BufferUsage.Index | BufferUsage.CopyDestination });
            indexUpload = device.CreateBuffer<uint>(new() { Count = indexCount, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
            await vertexUpload.MapAsync().ConfigureAwait(false);
            try
            {
                vertexUpload.CopyFrom(vertexData.Span);
            }
            finally
            {
                vertexUpload.Unmap();
            }

            await indexUpload.MapAsync().ConfigureAwait(false);
            try
            {
                indexUpload.Slice(0, indexCount).CopyFrom(indexData.Span);
            }
            finally
            {
                indexUpload.Unmap();
            }

            return new(vertices, vertexUpload, indices, indexUpload);
        }
        catch
        {
            indexUpload?.Dispose();
            indices?.Dispose();
            vertexUpload?.Dispose();
            vertices?.Dispose();
            throw;
        }
    }

    /// <summary>Records the initial staging copies and dependencies outside a pass, without submitting.</summary>
    /// <param name="commands">The recording command buffer; call once before first drawing.</param>
    public void RecordInitialUpload(IGraphicsCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ShaderDataSlice<ModelVertex> source = _vertexUpload.SliceElements(0, _vertexUpload.Count);
        ShaderDataSlice<ModelVertex> destination = Vertices.SliceElements(0, Vertices.Count);
        BufferSlice<uint> indexSource = _indexUpload.Slice(0, _indexUpload.Count);
        BufferSlice<uint> indexDestination = Indices.Slice(0, Indices.Count);
        commands.Barrier(new ShaderDataBufferBarrierDesc<ModelVertex> { Buffer = source, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = indexSource, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(source, destination);
        commands.CopyBuffer(indexSource, indexDestination);
        commands.Barrier(new ShaderDataBufferBarrierDesc<ModelVertex> { Buffer = destination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead) });
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = indexDestination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.IndexInput, ResourceAccess.IndexRead) });
    }

    /// <summary>Records the indexed mesh after the caller sets pipeline, arguments and explicit render state.</summary>
    /// <param name="render">The active render encoder.</param>
    public void Draw(IRenderEncoder render)
    {
        ArgumentNullException.ThrowIfNull(render);
        render.SetIndexBuffer(Indices.Slice(0, Indices.Count));
        render.DrawIndexed(checked((uint)Indices.Count));
    }

    /// <summary>Releases storage and staging after registrations and all GPU use have ended.</summary>
    public void Dispose()
    {
        Indices.Dispose();
        Vertices.Dispose();
        _indexUpload.Dispose();
        _vertexUpload.Dispose();
    }

    private static bool ContainsInvalidIndex(ReadOnlySpan<uint> indices, int vertexCount)
    {
        foreach (uint index in indices)
        {
            if (index >= vertexCount)
            {
                return true;
            }
        }

        return false;
    }
}
