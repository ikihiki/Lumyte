using System.Buffers.Binary;
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
    public int UntrackedDependencyBuffers { get; private set; }
    readonly List<ShaderValueSnapshot> snapshots = [];
    public void Barrier(MemoryBarrierDesc barrier) => native.Barrier(barrier);
    public void Barrier<T>(BufferBarrierDesc<T> barrier) where T : unmanaged => native.Barrier(barrier);
    public void Barrier(TextureBarrierDesc barrier) => native.Barrier(barrier);
    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier) where T : struct, IShaderArguments => native.Barrier(barrier);
    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination) where TSource : unmanaged where TDestination : unmanaged
    { native.CopyBuffer(source, destination); Copies++; CopyBytes += checked((long)source.SizeInBytes); }
    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination) where T : struct, IShaderArguments
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
        UntrackedDependencyBuffers = 0;
        foreach (var snapshot in snapshots) Visit(snapshot.Values.Select(value => value.Reference as IShaderReference
            ?? throw new InvalidOperationException("A diagnostic reference is missing metadata.")));
        return (backing.Count, bound, checked((long)backing.Values.Aggregate(0ul, (sum, value) => checked(sum + value))));
        void Visit(IEnumerable<IShaderReference> references)
        {
            foreach (var reference in references)
            {
                bool buffer = reference.Resource is IShaderDataSource || reference.Resource is IGraphicsBuffer<System.Numerics.Vector4>;
                if (!buffer || !ranges.Add((reference.Resource, reference.OffsetInBytes, reference.SizeInBytes))) continue;
                bound += checked((long)reference.SizeInBytes);
                if (reference.Resource is IShaderDataSource source)
                {
                    backing.TryAdd(reference.Resource, source.SizeInBytes);
                    if (source.TryGetDependencies(out IShaderReferenceMetadata? dependencies)) Visit(dependencies.References);
                    else UntrackedDependencyBuffers++;
                }
                else if (reference.Resource is IGraphicsBuffer<System.Numerics.Vector4> raw)
                    backing.TryAdd(reference.Resource, raw.SizeInBytes);
            }
        }
    }

    readonly record struct SelectedRangeArguments<T>(T Value, uint First, uint Count, int FirstOffset, int CountOffset) : IShaderArguments
        where T : struct, IShaderArguments
    {
        public static string RootParameter => T.RootParameter;
        public static string ShaderTypeName => T.ShaderTypeName;
        public static int TransferSize => T.TransferSize;
        public static int ReferenceCount => T.ReferenceCount;
        public static IReadOnlyList<ShaderTransferField> TransferFields => T.TransferFields;
        public void Write<TWriter>(ref TWriter writer) where TWriter : IShaderValueWriter, allows ref struct
        {
            var selected = new RangeWriter<TWriter>(writer, First, Count, FirstOffset, CountOffset);
            Value.Write(ref selected);
            writer = selected.Destination;
        }
    }

    ref struct RangeWriter<TWriter>(TWriter writer, uint first, uint count, int firstOffset, int countOffset) : IShaderValueWriter
        where TWriter : IShaderValueWriter, allows ref struct
    {
        public TWriter Destination = writer;
        int position;
        public void WriteValue(ReadOnlySpan<byte> value)
        {
            // Bound stack use independently of the producer's numeric chunk size.
            byte[] bytes = new byte[256];
            Span<byte> firstBytes = stackalloc byte[4];
            Span<byte> countBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(firstBytes, first);
            BinaryPrimitives.WriteUInt32LittleEndian(countBytes, count);
            while (!value.IsEmpty)
            {
                int length = Math.Min(value.Length, bytes.Length);
                Span<byte> chunk = bytes.AsSpan(0, length);
                value[..length].CopyTo(chunk);
                for (int index = 0; index < length; index++)
                {
                    int firstIndex = position + index - firstOffset;
                    int countIndex = position + index - countOffset;
                    if ((uint)firstIndex < 4) chunk[index] = firstBytes[firstIndex];
                    if ((uint)countIndex < 4) chunk[index] = countBytes[countIndex];
                }
                Destination.WriteValue(chunk);
                position += length;
                value = value[length..];
            }
        }
        public void WriteReference<TElement>(IGpuRef<TElement>? value)
        {
            Destination.WriteReference(value);
            position += 16;
        }
    }

    sealed class Encoder(RecordDiagnosticCommands owner, IRenderEncoder native, (uint First, uint Count)? selection) : IRenderEncoder
    {
        public void SetArgumentTable(IArgumentTable table) => native.SetArgumentTable(table);
        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => native.SetArgumentValidation(prepare);
        public void SetArguments<T>(in T arguments) where T : struct, IShaderArguments
        {
            owner.Roots.Add(T.ShaderTypeName);
            ShaderValueSnapshot snapshot = IShaderArguments.Capture(in arguments);
            owner.snapshots.Add(snapshot);
            if (selection is { } selected && T.ShaderTypeName == "DrawRangeArguments2D")
            {
                if (owner.selectedPage) { owner.skip = true; return; }
                // Explicit diagnostic C# metadata, never numeric ShaderValue entries.
                ShaderTransferField first = snapshot.GetDiagnosticFields().Single(field => field.Path == "FirstRecord");
                ShaderTransferField count = snapshot.GetDiagnosticFields().Single(field => field.Path == "RecordCount");
                if (first.IsReference || count.IsReference || first.Size != 4 || count.Size != 4 || first.ValueType != typeof(uint) || count.ValueType != typeof(uint)
                    || first.Offset < 0 || count.Offset < 0 || first.Offset > snapshot.Data.Length - 4 || count.Offset > snapshot.Data.Length - 4
                    || Math.Abs((long)first.Offset - count.Offset) < 4)
                    throw new InvalidOperationException("Unexpected draw range wire layout.");
                uint available = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Data.Span.Slice(count.Offset, 4));
                if (selected.First > available || selected.Count > available - selected.First) throw new ArgumentOutOfRangeException(nameof(selection));
                var range = new SelectedRangeArguments<T>(arguments, selected.First, selected.Count, first.Offset, count.Offset);
                native.SetArguments(in range);
                owner.selectedPage = true;
                return;
            }
            native.SetArguments(in arguments);
        }
        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null) where T : unmanaged
        {
            ShaderValueSnapshot snapshot = ShaderTransfer.Capture(in arguments, rootParameter, shaderTypeName);
            owner.Roots.Add(snapshot.ShaderTypeName);
            owner.snapshots.Add(snapshot);
            native.SetArguments(in arguments, rootParameter, shaderTypeName);
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
