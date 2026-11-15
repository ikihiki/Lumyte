using System.Runtime.InteropServices;

using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuEncoder(CpuCommands commands) : IRenderEncoder, IComputeEncoder
{
    private bool _ended;

    private CpuResource? _pipeline;

    private CpuTable? _table;

    private ulong _indexCount;

    private byte[] _wire = [];

    public void SetArgumentTable(IArgumentTable value)
    {
        Check();
        _table = (CpuTable)commands.Owner.Own(value);
    }

    public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
    {
        Check();
        if (prepare != null)
        {
            throw new NotSupportedException("Use the public validation device wrapper for dependency diagnostics.");
        }
    }

    public void SetArguments<T>(in T arguments)

        where T : struct, IShaderArguments
    {
        Check();
        if (_wire.Length != T.TransferSize)
        {
            _wire = new byte[T.TransferSize];
        }

        var writer = new CpuWriter(commands.Owner, _wire);
        arguments.Write(ref writer);
        writer.Complete();
        if (writer.Table != null && _table != writer.Table)
        {
            throw new ArgumentException("Select the argument _table that owns the root references.");
        }
    }

    public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)

        where T : unmanaged
    {
        Check();
        commands.Owner.Consume(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in arguments, 1)));
    }

    public void SetPipeline(IGraphicsPipeline value)
    {
        Check();
        _pipeline = commands.Owner.Own(value);
    }

    public void SetPipeline(IGraphicsComputePipeline value)
    {
        Check();
        _pipeline = commands.Owner.Own(value);
    }

    public void SetRenderState(GraphicsRenderStateDesc state)
    {
        Check();
        ArgumentNullException.ThrowIfNull(state);
    }

    public void SetViewport(Viewport viewport)
    {
        Check();
        if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) || !float.IsFinite(viewport.Width) || !float.IsFinite(viewport.Height) || viewport.Width <= 0 || viewport.Height <= 0 || !float.IsFinite(viewport.MinDepth) || !float.IsFinite(viewport.MaxDepth) || viewport.MinDepth < 0 || viewport.MaxDepth > 1 || viewport.MinDepth > viewport.MaxDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport));
        }

        commands.Owner.Mix((ulong)BitConverter.SingleToUInt32Bits(viewport.Width));
    }

    public void SetScissor(ScissorRect scissor)
    {
        Check();
        if (scissor.Width == 0 || scissor.Height == 0 || (ulong)scissor.X + scissor.Width > commands.PassWidth || (ulong)scissor.Y + scissor.Height > commands.PassHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(scissor));
        }

        commands.Owner.Mix(((ulong)scissor.Width << 32) | scissor.Height);
    }

    public void SetBlendConstant(BlendConstant value)
    {
        Check();
        if (!float.IsFinite(value.Red) || !float.IsFinite(value.Green) || !float.IsFinite(value.Blue) || !float.IsFinite(value.Alpha))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        commands.Owner.Mix(BitConverter.SingleToUInt32Bits(value.Alpha));
    }

    public void SetStencilReference(uint reference)
    {
        Check();
        if (reference > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        Execute();
        if (commands.IsCompute || _pipeline is not CpuPipeline || vertexCount == 0 || instanceCount == 0)
        {
            throw new InvalidOperationException("Invalid draw.");
        }

        commands.Owner.DrawCount++;
        commands.Owner.Mix(((ulong)vertexCount << 32) | instanceCount);
    }

    public void SetIndexBuffer(BufferSlice<ushort> indices) => Index(indices);

    public void SetIndexBuffer(BufferSlice<uint> indices) => Index(indices);

    public void DrawIndexed(uint count, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        if ((ulong)firstIndex + count > _indexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Draw(count, instanceCount, firstIndex, firstInstance);
    }

    public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => throw new NotSupportedException("Indirect GPU execution is not advertised.");

    public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => throw new NotSupportedException("Indirect GPU execution is not advertised.");

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        Execute();
        if (!commands.IsCompute || _pipeline is not CpuComputePipeline || groupCountX == 0 || groupCountY == 0 || groupCountZ == 0 || Math.Max(groupCountX, Math.Max(groupCountY, groupCountZ)) > commands.Owner.Caps.MaxComputeWorkgroupsPerDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }

        commands.Owner.DispatchCount++;
        commands.Owner.Mix(groupCountX);
        commands.Owner.Mix(groupCountY);
        commands.Owner.Mix(groupCountZ);
    }

    public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments) => throw new NotSupportedException("Indirect GPU execution is not modeled.");

    public void End()
    {
        Check();
        commands.EndPass();
        _ended = true;
    }

    private void Check()
    {
        if (_ended)
        {
            throw new InvalidOperationException("Pass _ended.");
        }

        commands.Recording(true);
    }

    private void Execute()
    {
        Check();
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a _pipeline.");
        }

        _pipeline.Check();
        _table?.Check();
    }

    private void Index<T>(BufferSlice<T> indices)

        where T : unmanaged
    {
        Check();
        commands.Owner.Own(indices.Buffer);
        indices.Buffer.ValidateByteRange(indices.OffsetInBytes, indices.SizeInBytes);
        if (indices.Buffer.IsMapped || (indices.Buffer.Usage & BufferUsage.Index) == 0)
        {
            throw new ArgumentException("Invalid index buffer.");
        }

        _indexCount = indices.Count;
    }
}
