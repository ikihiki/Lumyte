using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerEncoder : IRenderEncoder, IComputeEncoder
{
    private ShaderTargetData? _target;
    private IArgumentTable? _table;
    private Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? _prepare;
    private TestRootOwner? _selected;

    internal List<ShaderValueSnapshot> Snapshots { get; } = [];

    internal List<ShaderValueSnapshot> Executions { get; } = [];

    internal List<ShaderBindingDependencies> Bindings { get; } = [];

    internal Func<IShaderDataSource, IShaderReferenceMetadata?>? ReadShaderData { get; set; }

    internal int Draws { get; private set; }

    internal int IndexedDraws { get; private set; }

    internal int Dispatches { get; private set; }

    internal int Ends { get; private set; }

    internal Exception? SetArgumentsException { get; set; }

    internal Exception? EndException { get; init; }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => _prepare = prepare;

    public void SetArgumentTable(IArgumentTable table) => _table = table;

    public void SetArguments<T>(in T arguments)
        where T : struct, IShaderArguments
    {
        ThrowSelectionError();
        var layout = ShaderDataLayout.Root(_target!, T.RootParameter);
        TestRootOwner root = TestShaderTransfer.Create(in arguments, size => new MemoryStorage(size), Encode, diagnostic: layout, prepare: _prepare);
        _selected = root;
        RecordSelection(root, ShaderTransfer.Fields(in arguments));
    }

    public void SetArguments<T>(in T arguments, string rootParameter, string? shaderTypeName)
        where T : unmanaged
    {
        ThrowSelectionError();
        var layout = ShaderDataLayout.Root(_target!, rootParameter);
        TestRootOwner root = TestShaderTransfer.CreateUnmanaged(in arguments, rootParameter, shaderTypeName, size => new MemoryStorage(size), diagnostic: layout, prepare: _prepare);
        _selected = root;
        RecordSelection(root, ShaderTransfer.Fields<T>());
    }

    public void SetPipeline(IGraphicsPipeline pipeline) => _target = pipeline.Desc.VertexShader.Artifact.GetTarget(ShaderTarget.SpirV);

    public void SetPipeline(IGraphicsComputePipeline pipeline) => _target = pipeline.Desc.ComputeShader.Artifact.GetTarget(ShaderTarget.SpirV);

    public void SetRenderState(GraphicsRenderStateDesc state)
    {
    }

    public void SetViewport(Viewport viewport)
    {
    }

    public void SetScissor(ScissorRect scissor)
    {
    }

    public void SetBlendConstant(BlendConstant value)
    {
    }

    public void SetStencilReference(uint reference)
    {
    }

    public void SetIndexBuffer(BufferSlice<ushort> indices)
    {
    }

    public void SetIndexBuffer(BufferSlice<uint> indices)
    {
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        if (Snapshots.Count != 0)
        {
            CaptureExecution();
        }

        Draws++;
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        CaptureExecution();
        IndexedDraws++;
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
    {
        CaptureExecution();
        Draws++;
    }

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
    {
        CaptureExecution();
        IndexedDraws++;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        CaptureExecution();
        Dispatches++;
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments)
    {
        CaptureExecution();
        Dispatches++;
    }

    public void End()
    {
        Ends++;
        if (EndException is { } error)
        {
            throw error;
        }
    }

    private static byte[] Encode(ShaderValue value) => new byte[16];

    private void CaptureExecution()
    {
        if (Snapshots.Count != 0)
        {
            ShaderBindingDependencies? binding = _selected!.Metadata.References == null ? null : ShaderBindingDependencies.Collect(_selected.Metadata, _table, ReadShaderData);
            _selected!.Seal();
            if (binding != null)
            {
                Bindings.Add(binding);
            }

            Executions.Add(Snapshots.Last());
        }
    }

    private void ThrowSelectionError()
    {
        if (SetArgumentsException is { } error)
        {
            throw error;
        }
    }

    // Test observations only: read the fixture's final storage after successful selection.
    // These diagnostic bytes are never fed back into any production writing API.
    private void RecordSelection(TestRootOwner root, IReadOnlyList<ShaderTransferField> fields)
        => Snapshots.Add(new(
            root.RootParameter,
            root.ShaderTypeName,
            ((MemoryStorage)root.Storage).Bytes.AsMemory(0, root.WireSize).ToArray(),
            [],
            fields));

    private sealed class MemoryStorage(int size) : ITestShaderRootStorage
    {
        public int Size => Bytes.Length;

        internal byte[] Bytes { get; } = new byte[size];

        public void Write(ReadOnlySpan<byte> bytes, int offset) => bytes.CopyTo(Bytes.AsSpan(offset));

        public void Seal()
        {
        }

        public void Dispose()
        {
        }
    }
}
