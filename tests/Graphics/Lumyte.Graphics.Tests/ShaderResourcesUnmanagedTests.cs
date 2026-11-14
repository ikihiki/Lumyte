using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks that ordinary resource helpers select final-storage unmanaged encoder overloads.</summary>
public sealed class ShaderResourcesUnmanagedTests
{
    /// <summary>Each helper forwards raw values and execution arguments without a compatibility snapshot.</summary>
    /// <param name="operation">Draw, indexed draw, or compute dispatch.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HelpersSelectUnmanagedOverloadAndCopyAtCall(int operation)
    {
        using var plan = new ArgumentTablePlan(new NumericDevice());
        ShaderResources resources = plan.CreateShaderResources();
        plan.ConfigureExecution(resources, operation == 2 ? 0u : 1u);
        plan.MaterializeResources([resources]);
        Assert.Empty(plan.Tables);
        using var encoder = new RawEncoder();
        var value = new Color(0x12345678u);
        Invoke(resources, encoder, in value, operation);
        value = new Color(0xabcdef01u);

        Assert.Equal(1, encoder.RawSetCount);
        Assert.Equal(0, encoder.TableSetCount);
        Assert.NotNull(encoder.Root);
        Assert.Equal("scene", encoder.Root.RootParameter);
        Assert.Equal("PixelColor", encoder.Root.ShaderTypeName);
        Assert.Equal(4, encoder.Root.WireSize);
        Assert.Empty(encoder.Root.References.References);
        Assert.Equal(0x12345678u, MemoryMarshal.Read<uint>(encoder.Storage!.Bytes));
        Assert.Equal(1, encoder.Storage.Writes);
        Assert.Equal(operation == 0 ? 1 : 0, encoder.DrawCount);
        Assert.Equal(operation == 1 ? 1 : 0, encoder.IndexedDrawCount);
        Assert.Equal(operation == 2 ? 1 : 0, encoder.DispatchCount);
        Assert.Equal(operation == 0 ? (7u, 3u, 2u, 5u) : default, encoder.DrawArguments);
        Assert.Equal(operation == 1 ? (11u, 4u, 6u, -2, 8u) : default, encoder.IndexedArguments);
        Assert.Equal(operation == 2 ? (9u, 2u, 3u) : default, encoder.DispatchArguments);
    }

    /// <summary>Collection, execution-kind, and expired-generation checks run before encoder selection.</summary>
    /// <param name="operation">Draw, indexed draw, or compute dispatch.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidScopesRejectBeforeCallingEncoder(int operation)
    {
        using var plan = new ArgumentTablePlan(new NumericDevice());
        ShaderResources resources = plan.CreateShaderResources();
        using var encoder = new RawEncoder();
        var value = new Color(17);
        Assert.Throws<InvalidOperationException>(() => Invoke(resources, encoder, in value, operation));
        plan.ConfigureExecution(resources, operation == 2 ? 1u : 0u);
        plan.MaterializeResources([resources]);
        Assert.Throws<InvalidOperationException>(() => Invoke(resources, encoder, in value, operation));
        plan.Dispose();
        Assert.Throws<InvalidOperationException>(() => Invoke(resources, encoder, in value, operation));
        Assert.Equal(0, encoder.RawSetCount);
        Assert.Equal(0, encoder.TableSetCount);
        Assert.Equal(0, encoder.DrawCount + encoder.IndexedDrawCount + encoder.DispatchCount);
        Assert.Null(encoder.Storage);
    }

    private static void Invoke(ShaderResources resources, RawEncoder encoder, in Color value, int operation)
    {
        switch (operation)
        {
            case 0:
                resources.Draw(encoder, in value, 7, 3, 2, 5, "scene", "PixelColor");
                break;
            case 1:
                resources.DrawIndexed(encoder, in value, 11, 4, 6, -2, 8, "scene", "PixelColor");
                break;
            case 2:
                resources.Dispatch(encoder, in value, 9, 2, 3, "scene", "PixelColor");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private readonly record struct Color(uint Value);

    // This is the fake backend's final root allocation, not a captured CPU snapshot.
    private sealed class RootStorage(int size) : IShaderRootStorage
    {
        public int Size => Bytes.Length;

        internal byte[] Bytes { get; } = new byte[size];

        internal int Writes { get; private set; }

        public void Write(ReadOnlySpan<byte> bytes, int offset)
        {
            bytes.CopyTo(Bytes.AsSpan(offset));
            Writes++;
        }

        public void Seal()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RawEncoder : IRenderEncoder, IComputeEncoder, IDisposable
    {
        internal int RawSetCount { get; private set; }

        internal int TableSetCount { get; private set; }

        internal int DrawCount { get; private set; }

        internal int IndexedDrawCount { get; private set; }

        internal int DispatchCount { get; private set; }

        internal (uint VertexCount, uint InstanceCount, uint FirstVertex, uint FirstInstance) DrawArguments { get; private set; }

        internal (uint IndexCount, uint InstanceCount, uint FirstIndex, int BaseVertex, uint FirstInstance) IndexedArguments { get; private set; }

        internal (uint GroupCountX, uint GroupCountY, uint GroupCountZ) DispatchArguments { get; private set; }

        internal ShaderRootArguments? Root { get; private set; }

        internal RootStorage? Storage { get; private set; }

        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where T : unmanaged
        {
            RawSetCount++;
            Root = ShaderRootArguments.CreateUnmanaged(in arguments, rootParameter, shaderTypeName, size => Storage = new RootStorage(size));
        }

        public void SetArguments<T>(in T arguments)
            where T : IShaderArguments => throw new InvalidOperationException("Unexpected interface serializer.");

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
        {
        }

        public void SetArgumentTable(IArgumentTable table)
        {
            TableSetCount++;
            throw new InvalidOperationException("Numeric-only resources must not allocate a table.");
        }

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            Assert.NotNull(Root);
            DrawCount++;
            DrawArguments = (vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        {
            Assert.NotNull(Root);
            IndexedDrawCount++;
            IndexedArguments = (indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
        }

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
        {
            Assert.NotNull(Root);
            DispatchCount++;
            DispatchArguments = (groupCountX, groupCountY, groupCountZ);
        }

        public void SetPipeline(IGraphicsPipeline pipeline) => throw new NotSupportedException();

        public void SetPipeline(IGraphicsComputePipeline pipeline) => throw new NotSupportedException();

        public void SetRenderState(GraphicsRenderStateDesc state) => throw new NotSupportedException();

        public void SetViewport(Viewport viewport) => throw new NotSupportedException();

        public void SetScissor(ScissorRect scissor) => throw new NotSupportedException();

        public void SetBlendConstant(BlendConstant value) => throw new NotSupportedException();

        public void SetStencilReference(uint reference) => throw new NotSupportedException();

        public void SetIndexBuffer(BufferSlice<ushort> indices) => throw new NotSupportedException();

        public void SetIndexBuffer(BufferSlice<uint> indices) => throw new NotSupportedException();

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => throw new NotSupportedException();

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => throw new NotSupportedException();

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments) => throw new NotSupportedException();

        public void End() => throw new NotSupportedException();

        public void Dispose() => Root?.Dispose();
    }

    private sealed class NumericDevice : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add
            {
            }

            remove
            {
            }
        }

        public GraphicsDeviceStatus Status => throw new NotSupportedException();

        public GraphicsDeviceLoss? Loss => null;

        public DeviceCaps Caps { get; } = new();

        public IGraphicsQueue Queue => throw new NotSupportedException();

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => throw new NotSupportedException();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged => throw new NotSupportedException();

        public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new NotSupportedException();

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new InvalidOperationException("Numeric-only scopes need no table.");

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

        public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw new NotSupportedException();

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : IShaderArguments => throw new NotSupportedException();
    }
}
