using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing;

/// <summary>Owns fixed-layout model storage and staging without per-vertex shader snapshots.</summary>
public sealed class ModelMesh : IDisposable
{
    private readonly IGraphicsBuffer<ModelVertex> _vertexUpload;
    private readonly IIndexStorage _indices;
    private int _disposeStage;

    private ModelMesh(IGraphicsBuffer<ModelVertex> vertices, IGraphicsBuffer<ModelVertex> vertexUpload, IIndexStorage indices)
    {
        Vertices = vertices;
        _vertexUpload = vertexUpload;
        _indices = indices;
    }

    /// <summary>Abstracts the index element width without expanding the uploaded stream.</summary>
    private interface IIndexStorage : IDisposable
    {
        /// <summary>Gets the logical index count.</summary>
        uint Count { get; }

        /// <summary>Gets the preserved element format.</summary>
        IndexFormat Format { get; }

        /// <summary>Records the padded storage upload.</summary>
        /// <param name="commands">The command recorder.</param>
        void RecordUpload(IGraphicsCommandBuffer commands);

        /// <summary>Binds the logical range for indexed drawing.</summary>
        /// <param name="render">The active render encoder.</param>
        void Bind(IRenderEncoder render);

        /// <summary>Imports the index allocation and returns its read declaration.</summary>
        /// <param name="graph">The parent graph.</param>
        /// <param name="scope">The allocation's actual prior scope.</param>
        /// <returns>The index-use declaration.</returns>
        Action<RenderGraphPass> Import(Graph graph, BarrierScope scope);
    }

    /// <summary>Gets raw, ABI-verified vertex storage to register through Vertices.Slice.</summary>
    public IGraphicsBuffer<ModelVertex> Vertices { get; }

    /// <summary>Gets the triangle index count.</summary>
    public uint IndexCount => _indices.Count;

    /// <summary>Gets the original sixteen- or thirty-two-bit GPU index format.</summary>
    public IndexFormat IndexFormat => _indices.Format;

    /// <summary>Allocates raw vertex storage and fills staging with one bulk copy per input stream.</summary>
    /// <param name="device">The owning device.</param>
    /// <param name="vertexData">The fixed-layout object-space vertices.</param>
    /// <param name="indexData">The in-range thirty-two-bit triangle indices.</param>
    /// <param name="vertexArtifact">The shader whose natural vertex layout is verified before allocation.</param>
    /// <returns>The mesh with filled, unmapped staging; no GPU submission is performed.</returns>
    public static Task<ModelMesh> CreateAsync(IGraphicDevice device, ReadOnlyMemory<ModelVertex> vertexData, ReadOnlyMemory<uint> indexData, ShaderArtifact vertexArtifact)
        => CreateCoreAsync(device, vertexData, default, indexData, vertexArtifact, IndexFormat.Uint32, static (render, buffer, count) => { render.SetIndexBuffer(buffer.Slice(0, count)); });

    /// <summary>Preserves sixteen-bit indices without expanding them into thirty-two-bit values.</summary>
    /// <param name="device">The owning device.</param>
    /// <param name="vertexData">The fixed-layout object-space vertices.</param>
    /// <param name="indexData">The in-range sixteen-bit triangle indices.</param>
    /// <param name="vertexArtifact">The model shader artifact.</param>
    /// <returns>The mesh with filled staging, without GPU submission.</returns>
    public static Task<ModelMesh> CreateAsync(IGraphicDevice device, ReadOnlyMemory<ModelVertex> vertexData, ReadOnlyMemory<ushort> indexData, ShaderArtifact vertexArtifact)
        => CreateCoreAsync(device, vertexData, default, indexData, vertexArtifact, IndexFormat.Uint16, static (render, buffer, count) => { render.SetIndexBuffer(buffer.Slice(0, count)); });

    /// <summary>Uploads compatible interleaved vertex bytes without allocating a converted vertex array.</summary>
    /// <param name="device">The owning device.</param>
    /// <param name="vertexBytes">Complete little-endian 64-byte vertices.</param>
    /// <param name="indexData">The in-range thirty-two-bit triangle indices.</param>
    /// <param name="vertexArtifact">The model shader artifact.</param>
    /// <param name="retainCleanup">Optionally retains retryable partial-allocation cleanup before creation; dispose after failure. Empty on success.</param>
    /// <returns>The mesh after a bulk copy into staging, without GPU submission.</returns>
    public static Task<ModelMesh> CreatePackedAsync(IGraphicDevice device, ReadOnlyMemory<byte> vertexBytes, ReadOnlyMemory<uint> indexData, ShaderArtifact vertexArtifact, Action<IDisposable>? retainCleanup = null)
        => CreateCoreAsync(device, default, vertexBytes, indexData, vertexArtifact, IndexFormat.Uint32, static (render, buffer, count) => { render.SetIndexBuffer(buffer.Slice(0, count)); }, retainCleanup);

    /// <summary>Uploads interleaved vertex bytes and preserves sixteen-bit triangle indices.</summary>
    /// <param name="device">The owning device.</param>
    /// <param name="vertexBytes">Complete little-endian 64-byte vertices.</param>
    /// <param name="indexData">The in-range sixteen-bit triangle indices.</param>
    /// <param name="vertexArtifact">The model shader artifact.</param>
    /// <param name="retainCleanup">Optionally retains retryable partial-allocation cleanup before creation; dispose after failure. Empty on success.</param>
    /// <returns>The mesh with bulk-filled staging, without GPU submission.</returns>
    public static Task<ModelMesh> CreatePackedAsync(IGraphicDevice device, ReadOnlyMemory<byte> vertexBytes, ReadOnlyMemory<ushort> indexData, ShaderArtifact vertexArtifact, Action<IDisposable>? retainCleanup = null)
        => CreateCoreAsync(device, default, vertexBytes, indexData, vertexArtifact, IndexFormat.Uint16, static (render, buffer, count) => { render.SetIndexBuffer(buffer.Slice(0, count)); }, retainCleanup);

    /// <summary>Records initial raw staging copies and host/copy/vertex/index dependencies outside a pass.</summary>
    /// <param name="commands">The recording command buffer; call once before first drawing.</param>
    public void RecordInitialUpload(IGraphicsCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        BufferSlice<ModelVertex> source = _vertexUpload.Slice(0, _vertexUpload.Count);
        BufferSlice<ModelVertex> destination = Vertices.Slice(0, Vertices.Count);
        commands.Barrier(new BufferBarrierDesc<ModelVertex> { Buffer = source, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(source, destination);
        commands.Barrier(new BufferBarrierDesc<ModelVertex> { Buffer = destination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead) });
        _indices.RecordUpload(commands);
    }

    /// <summary>Imports initialized mesh buffers once into a graph without taking ownership.</summary>
    /// <param name="graph">The parent graph.</param>
    /// <param name="vertexScope">The actual prior vertex-buffer scope.</param>
    /// <param name="indexScope">The actual prior index-buffer scope.</param>
    /// <returns>The handles used to declare every model pass's buffer dependencies.</returns>
    public ModelMeshGraphResources Import(Graph graph, BarrierScope vertexScope, BarrierScope indexScope)
    {
        ArgumentNullException.ThrowIfNull(graph);
        RenderGraphBuffer<ModelVertex> vertices = graph.ImportBuffer(Vertices, vertexScope, "model-vertices");
        return new(vertices, _indices.Import(graph, indexScope));
    }

    /// <summary>Records indexed drawing after the caller sets pipeline, arguments and render state.</summary>
    /// <param name="render">The active render encoder.</param>
    public void Draw(IRenderEncoder render)
    {
        ArgumentNullException.ThrowIfNull(render);
        _indices.Bind(render);
        render.DrawIndexed(IndexCount);
    }

    /// <summary>Releases storage and staging after registrations and all GPU use have ended.</summary>
    public void Dispose()
    {
        if (_disposeStage == 0)
        {
            _indices.Dispose();
            _disposeStage = 1;
        }

        if (_disposeStage == 1)
        {
            Vertices.Dispose();
            _disposeStage = 2;
        }

        if (_disposeStage == 2)
        {
            _vertexUpload.Dispose();
            _disposeStage = 3;
        }
    }

    private static async Task<ModelMesh> CreateCoreAsync<TIndex>(
        IGraphicDevice device,
        ReadOnlyMemory<ModelVertex> vertexData,
        ReadOnlyMemory<byte> vertexBytes,
        ReadOnlyMemory<TIndex> indexData,
        ShaderArtifact artifact,
        IndexFormat format,
        Action<IRenderEncoder, IGraphicsBuffer<TIndex>, uint> bind,
        Action<IDisposable>? retainCleanup = null)
        where TIndex : unmanaged
    {
        ArgumentNullException.ThrowIfNull(device);
        ModelVertex.ValidateShaderLayout(artifact, device.Caps.ShaderTarget);
        int vertexCount = vertexBytes.IsEmpty ? vertexData.Length : ModelVertex.AsVertices(vertexBytes.Span).Length;
        if (vertexCount == 0 || indexData.IsEmpty || indexData.Length % 3 != 0)
        {
            throw new ArgumentException("A mesh requires nonempty vertices and triangle indices.", nameof(indexData));
        }

        var cleanup = new CreationCleanup();
        retainCleanup?.Invoke(cleanup);
        try
        {
            IGraphicsBuffer<ModelVertex> vertices = cleanup.Add(device.CreateBuffer<ModelVertex>(new() { Count = (ulong)vertexCount, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
            IGraphicsBuffer<ModelVertex> vertexUpload = cleanup.Add(device.CreateBuffer<ModelVertex>(new() { Count = (ulong)vertexCount, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));

            // Pad the transfer allocation to four bytes for odd ushort counts; draw only the logical count.
            ulong indexCount = (ulong)indexData.Length;
            ulong storageCount = format == IndexFormat.Uint16 ? (indexCount + 1) & ~1UL : indexCount;
            IGraphicsBuffer<TIndex> indices = cleanup.Add(device.CreateBuffer<TIndex>(new() { Count = storageCount, Usage = BufferUsage.Index | BufferUsage.CopyDestination }));
            IGraphicsBuffer<TIndex> indexUpload = cleanup.Add(device.CreateBuffer<TIndex>(new() { Count = storageCount, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
            await vertexUpload.MapAsync();
            try
            {
                vertexUpload.CopyFrom(vertexBytes.IsEmpty ? vertexData.Span : ModelVertex.AsVertices(vertexBytes.Span));
            }
            finally
            {
                vertexUpload.Unmap();
            }

            await indexUpload.MapAsync();
            try
            {
                indexUpload.CopyFrom(indexData.Span);
                if (storageCount != indexCount)
                {
                    indexUpload.Slice(indexCount, 1).CopyFrom(new TIndex[1]);
                }
            }
            finally
            {
                indexUpload.Unmap();
            }

            var mesh = new ModelMesh(vertices, vertexUpload, new IndexStorage<TIndex>(indices, indexUpload, checked((uint)indexCount), format, bind));
            cleanup.Transfer();
            return mesh;
        }
        catch
        {
            if (retainCleanup is null)
            {
                cleanup.Dispose();
            }

            throw;
        }
    }

    private sealed class CreationCleanup : IDisposable
    {
        private readonly List<IDisposable> _resources = new(4);

        public T Add<T>(T resource)
            where T : IDisposable
        {
            _resources.Add(resource);
            return resource;
        }

        public void Transfer() => _resources.Clear();

        public void Dispose()
        {
            while (_resources.Count != 0)
            {
                _resources[^1].Dispose();
                _resources.RemoveAt(_resources.Count - 1);
            }
        }
    }

    private sealed class IndexStorage<T>(IGraphicsBuffer<T> storage, IGraphicsBuffer<T> upload, uint count, IndexFormat format, Action<IRenderEncoder, IGraphicsBuffer<T>, uint> bind) : IIndexStorage
        where T : unmanaged
    {
        public uint Count => count;

        public IndexFormat Format => format;

        public void RecordUpload(IGraphicsCommandBuffer commands)
        {
            BufferSlice<T> source = upload.Slice(0, upload.Count);
            BufferSlice<T> destination = storage.Slice(0, storage.Count);
            commands.Barrier(new BufferBarrierDesc<T> { Buffer = source, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(source, destination);
            commands.Barrier(new BufferBarrierDesc<T> { Buffer = destination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.IndexInput, ResourceAccess.IndexRead) });
        }

        public void Bind(IRenderEncoder render) => bind(render, storage, Count);

        public Action<RenderGraphPass> Import(Graph graph, BarrierScope scope)
        {
            RenderGraphBuffer<T> buffer = graph.ImportBuffer(storage, scope, "model-indices");
            return pass => pass.UseBuffer(buffer, new(PipelineStage.IndexInput, ResourceAccess.IndexRead));
        }

        public void Dispose()
        {
            storage.Dispose();
            upload.Dispose();
        }
    }
}
