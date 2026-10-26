using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks explicit units, cancellation cleanup and opt-in draw initialization without a GPU.</summary>
public sealed class GraphicsApiConvenienceTests
{
    /// <summary>Checks that element slices and byte copies address different, documented units.</summary>
    [Fact]
    public void ByteCopiesPreserveElementSliceUnits()
    {
        using var buffer = new RawBuffer();
        BufferSlice<uint> slice = buffer.SliceElements(1, 2);
        Assert.Equal(4UL, slice.OffsetInBytes);
        Assert.Equal(8UL, slice.SizeInBytes);
        buffer.CopyBytesFrom(new byte[] { 7, 8 }, 5, 2);
        byte[] result = new byte[2];
        buffer.CopyBytesTo(result, 5, 2);
        Assert.Equal(new byte[] { 7, 8 }, result);
        Assert.Equal(0, buffer.Bytes[4]);
        Assert.Equal(0, buffer.Bytes[7]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ValidateByteRange(15, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SliceElements(3, 2));
    }

    /// <summary>Pre-cancellation does not begin native mapping.</summary>
    /// <returns>The completed check.</returns>
    [Fact]
    public async Task CancelledMappingDoesNotStartAsync()
    {
        using var buffer = new ShaderBuffer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => buffer.MapAsync(cancellation.Token).AsTask());
        Assert.Equal(0, buffer.MapCalls);
        Assert.False(buffer.IsMapped);
    }

    /// <summary>Cancellation waits for native completion and releases the resulting mapping.</summary>
    /// <returns>The completed check.</returns>
    [Fact]
    public async Task CancellationDuringMappingUnmapsAfterCompletionAsync()
    {
        using var buffer = new ShaderBuffer();
        using var cancellation = new CancellationTokenSource();
        Task mapping = buffer.MapAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        Assert.False(mapping.IsCompleted);
        buffer.CompleteMapping();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mapping);
        Assert.Equal(1, buffer.UnmapCalls);
        Assert.False(buffer.IsMapped);
    }

    /// <summary>Successful mapping stays active and the shader range alias preserves logical units.</summary>
    /// <returns>The completed check.</returns>
    [Fact]
    public async Task SuccessfulMappingRemainsMappedAsync()
    {
        using var buffer = new ShaderBuffer();
        Task mapping = buffer.MapAsync(CancellationToken.None).AsTask();
        buffer.CompleteMapping();
        await mapping;
        Assert.True(buffer.IsMapped);
        Assert.Equal(0, buffer.UnmapCalls);
        ShaderDataSlice<SampleData> slice = buffer.Slice(1, 2);
        Assert.Equal(1UL, slice.Offset);
        Assert.Equal(2UL, slice.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(3, 2));
    }

    /// <summary>Native mapping failures are preserved without attempting to unmap nonexistent memory.</summary>
    /// <returns>The completed check.</returns>
    [Fact]
    public async Task MappingFailureDoesNotUnmapAsync()
    {
        using var buffer = new ShaderBuffer();
        Task mapping = buffer.MapAsync(CancellationToken.None).AsTask();
        buffer.FailMapping();
        await Assert.ThrowsAsync<InvalidOperationException>(() => mapping);
        Assert.Equal(0, buffer.UnmapCalls);
    }

    /// <summary>The explicit helper initializes every required dynamic state and preserves overrides.</summary>
    [Fact]
    public void DynamicStateUsesExplicitValuesAndDefaults()
    {
        var encoder = new RecordingEncoder();
        var viewport = new Viewport(2, 3, 20, 10, 0, 1);
        var scissor = new ScissorRect(3, 4, 4, 5);
        encoder.SetDynamicState(viewport, scissor);
        Assert.Equal(viewport, encoder.Viewport);
        Assert.Equal(scissor, encoder.Scissor);
        Assert.Equal(default, encoder.Blend);
        Assert.Equal(0U, encoder.Stencil);
        var blend = new BlendConstant(1, 0.5f, 0.25f, 1);
        encoder.SetDynamicState(viewport, scissor, blend, 7);
        Assert.Equal(blend, encoder.Blend);
        Assert.Equal(7U, encoder.Stencil);
        Assert.Equal(8, encoder.SetterCalls);
    }

    /// <summary>Color state creation supports explicit color counts and depth-only passes.</summary>
    [Fact]
    public void ColorTargetDefaultsMatchRequestedPassShape()
    {
        Assert.Empty(GraphicsRenderStateDesc.ForColorTargets(0).ColorTargets);
        var state = GraphicsRenderStateDesc.ForColorTargets(2);
        Assert.Equal(2, state.ColorTargets.Count);
        Assert.All(state.ColorTargets, target => Assert.Equal(ColorWriteMask.All, target.WriteMask));
        Assert.NotSame(state.ColorTargets[0], state.ColorTargets[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphicsRenderStateDesc.ForColorTargets(-1));
    }

    private readonly record struct SampleData(uint Value) : IShaderData
    {
        public static string ShaderTypeName => nameof(SampleData);

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteValue(nameof(Value), Value);
    }

    private sealed class ShaderBuffer : IGraphicsShaderDataBuffer<SampleData>
    {
        private readonly TaskCompletionSource _mapping = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ulong Count => 4;

        public ulong SizeInBytes => 64;

        public ulong ShaderElementStrideInBytes => 16;

        public MemoryPreference Memory => MemoryPreference.Upload;

        public bool IsMapped { get; private set; }

        public int MapCalls { get; private set; }

        public int UnmapCalls { get; private set; }

        public async ValueTask MapAsync()
        {
            MapCalls++;
            await _mapping.Task;
            IsMapped = true;
        }

        public void CompleteMapping() => _mapping.SetResult();

        public void FailMapping() => _mapping.SetException(new InvalidOperationException("Native mapping failed."));

        public void Unmap()
        {
            UnmapCalls++;
            IsMapped = false;
        }

        public ShaderDataSlice<SampleData> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<SampleData> source, ulong elementOffset = 0) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class RawBuffer : IGraphicsBuffer<uint>
    {
        public byte[] Bytes { get; } = new byte[16];

        public ulong Count => 4;

        public ulong SizeInBytes => 16;

        public BufferLayout<uint> Layout => new(4, 4, 1, 1);

        public BufferUsage Usage => BufferUsage.CopySource;

        public MemoryPreference Memory => MemoryPreference.Upload;

        public bool IsMapped => true;

        public void ValidateRange(ulong offset, ulong length)
        {
            if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
        }

        public void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
        {
            ValidateRange(offset, length);
            source.CopyTo(Bytes.AsSpan(checked((int)offset), checked((int)length)));
        }

        public void CopyTo(Span<byte> destination, ulong offset, ulong length)
        {
            ValidateRange(offset, length);
            Bytes.AsSpan(checked((int)offset), checked((int)length)).CopyTo(destination);
        }

        public BufferSlice<uint> Slice(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<uint> source) => MemoryMarshal.AsBytes(source).CopyTo(Bytes);

        public void CopyTo(Span<uint> destination) => Bytes.CopyTo(MemoryMarshal.AsBytes(destination));

        public ValueTask MapAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Unmap()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingEncoder : IRenderEncoder
    {
        public Viewport Viewport { get; private set; }

        public ScissorRect Scissor { get; private set; }

        public BlendConstant Blend { get; private set; }

        public uint Stencil { get; private set; }

        public int SetterCalls { get; private set; }

        public void SetViewport(Viewport viewport)
        {
            Viewport = viewport;
            SetterCalls++;
        }

        public void SetScissor(ScissorRect scissor)
        {
            Scissor = scissor;
            SetterCalls++;
        }

        public void SetBlendConstant(BlendConstant value)
        {
            Blend = value;
            SetterCalls++;
        }

        public void SetStencilReference(uint reference)
        {
            Stencil = reference;
            SetterCalls++;
        }

        public void SetArgumentTable(IArgumentTable table) => throw new NotSupportedException();

        public void SetArguments<T>(in T arguments)
            where T : struct, IShaderArguments => throw new NotSupportedException();

        public void SetPipeline(IGraphicsPipeline pipeline) => throw new NotSupportedException();

        public void SetRenderState(GraphicsRenderStateDesc state) => throw new NotSupportedException();

        public void SetIndexBuffer(BufferSlice<ushort> indices) => throw new NotSupportedException();

        public void SetIndexBuffer(BufferSlice<uint> indices) => throw new NotSupportedException();

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => throw new NotSupportedException();

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => throw new NotSupportedException();

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => throw new NotSupportedException();

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => throw new NotSupportedException();

        public void End() => throw new NotSupportedException();
    }
}
