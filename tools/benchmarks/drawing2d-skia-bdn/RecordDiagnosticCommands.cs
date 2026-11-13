using Lumyte.Graphics.Abstractions;

// Diagnostic-only observer; native queues still receive the original command.
sealed class RecordDiagnosticCommands(IGraphicsCommandBuffer native, (uint First, uint Count)? selection = null) : IGraphicsCommandBuffer
{
    bool selectedPage;
    bool skip;
    public CommandBufferState State => native.State;
    public long CopyBytes { get; private set; }
    public int Copies { get; private set; }
    public int Draws { get; private set; }
    public int RenderPasses { get; private set; }
    public HashSet<IGraphicsPipeline> Pipelines { get; } = new(ReferenceEqualityComparer.Instance);
    public long Instances { get; private set; }
    public List<string> Roots { get; } = [];
    readonly List<ShaderValueSnapshot> snapshots = [];
    public void Barrier(MemoryBarrierDesc barrier) => native.Barrier(barrier);
    public void Barrier<T>(BufferBarrierDesc<T> barrier) where T : unmanaged => native.Barrier(barrier);
    public void Barrier(TextureBarrierDesc barrier) => native.Barrier(barrier);
    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier) where T : struct, IShaderData => native.Barrier(barrier);
    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination) where TSource : unmanaged where TDestination : unmanaged
    { native.CopyBuffer(source, destination); Copies++; CopyBytes += checked((long)source.SizeInBytes); }
    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination) where T : struct, IShaderData
    { native.CopyBuffer(source, destination); Copies++; CopyBytes += checked((long)(source.Count * source.Buffer.ShaderElementStrideInBytes)); }
    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => native.CopyTexture(source, destination);
    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => native.CopyBufferToTexture(source, destination);
    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => native.CopyTextureToBuffer(source, destination);
    public IRenderEncoder BeginRenderPass(RenderPassDesc desc) { var render = native.BeginRenderPass(desc); RenderPasses++; return new Encoder(this, render, selection); }
    public IComputeEncoder BeginComputePass(ComputePassDesc desc) => native.BeginComputePass(desc);
    public void Finish() => native.Finish();
    public void Dispose() => native.Dispose();

    public (int Buffers, long BoundBytes, long BackingBytes) Inspect()
    {
        var backing = new Dictionary<object, ulong>(ReferenceEqualityComparer.Instance);
        var ranges = new HashSet<(object Resource, ulong Offset, ulong Bytes)>();
        long bound = 0;
        foreach (var snapshot in snapshots) Visit(snapshot);
        return (backing.Count, bound, checked((long)backing.Values.Aggregate(0ul, (sum, value) => checked(sum + value))));
        void Visit(ShaderValueSnapshot snapshot)
        {
            foreach (var value in snapshot.Values.Where(value => value.IsReference))
            {
                if (value.Reference is not IShaderReference reference) throw new InvalidOperationException("A diagnostic reference is missing metadata.");
                bool buffer = reference.Resource is IShaderDataSource || reference.Resource is IGraphicsBuffer<System.Numerics.Vector4>;
                if (!buffer || !ranges.Add((reference.Resource, reference.OffsetInBytes, reference.SizeInBytes))) continue;
                bound += checked((long)reference.SizeInBytes);
                if (reference.Resource is IShaderDataSource source)
                {
                    backing.TryAdd(reference.Resource, source.SizeInBytes);
                    ulong stride = checked((ulong)source.Layout.Size);
                    for (ulong i = 0; i < reference.Count; i++) Visit(source.Read(reference.OffsetInBytes / stride + i));
                }
                else if (reference.Resource is IGraphicsBuffer<System.Numerics.Vector4> raw)
                    backing.TryAdd(reference.Resource, raw.SizeInBytes);
            }
        }
    }

    sealed class Encoder(RecordDiagnosticCommands owner, IRenderEncoder native, (uint First, uint Count)? selection) : IRenderEncoder
    {
        public void SetArgumentTable(IArgumentTable table) => native.SetArgumentTable(table);
        public void SetArguments<T>(in T arguments) where T : struct, IShaderArguments
        { if (selection is not null && T.ShaderTypeName == "DrawRangeArguments2D") { SetArguments(IShaderArguments.Capture(arguments)); return; } owner.Roots.Add(T.ShaderTypeName); owner.snapshots.Add(IShaderArguments.Capture(arguments)); native.SetArguments(in arguments); }
        public void SetArguments(ShaderValueSnapshot arguments)
        {
            owner.Roots.Add(arguments.ShaderTypeName);
            owner.snapshots.Add(arguments);
            if (selection is { } selected && arguments.ShaderTypeName == "DrawRangeArguments2D")
            {
                if (owner.selectedPage) { owner.skip = true; return; }
                uint available = BitConverter.ToUInt32(arguments.Values.Single(value => value.Path == "RecordCount").Data.Span);
                if (selected.First > available || selected.Count > available - selected.First) throw new ArgumentOutOfRangeException(nameof(selection));
                owner.selectedPage = true;
                arguments = arguments with { Values = arguments.Values.Select(value => value.Path == "FirstRecord" ? value with { Data = BitConverter.GetBytes(selected.First) } : value.Path == "RecordCount" ? value with { Data = BitConverter.GetBytes(selected.Count) } : value).ToArray() };
            }
            native.SetArguments(arguments);
        }
        public void SetPipeline(IGraphicsPipeline pipeline) { owner.Pipelines.Add(pipeline); native.SetPipeline(pipeline); }
        public void SetRenderState(GraphicsRenderStateDesc state) => native.SetRenderState(state);
        public void SetViewport(Viewport viewport) => native.SetViewport(viewport);
        public void SetScissor(ScissorRect scissor) => native.SetScissor(scissor);
        public void SetBlendConstant(BlendConstant value) => native.SetBlendConstant(value);
        public void SetStencilReference(uint reference) => native.SetStencilReference(reference);
        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        { if (owner.skip) return; if (selection is { } selected && owner.selectedPage) { instanceCount = selected.Count; firstInstance = 0; } if (instanceCount == 0) return; native.Draw(vertexCount, instanceCount, firstVertex, firstInstance); owner.Draws++; owner.Instances += instanceCount; }
        public void SetIndexBuffer(BufferSlice<ushort> indices) => native.SetIndexBuffer(indices);
        public void SetIndexBuffer(BufferSlice<uint> indices) => native.SetIndexBuffer(indices);
        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => native.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => native.DrawIndirect(arguments);
        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => native.DrawIndexedIndirect(arguments);
        public void End() => native.End();
    }
}
