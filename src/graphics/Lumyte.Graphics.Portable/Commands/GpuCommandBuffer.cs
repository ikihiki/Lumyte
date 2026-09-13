using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Portable;

/// <summary>A one-shot Portable recording borrowing resources, bindings and pipelines from the caller.</summary>
/// <remarks>
/// The caller serializes recording, submission and disposal of this object, closes scopes before submission,
/// and keeps referenced objects alive through all recorded and GPU uses. Disposal never cancels submitted work.
/// Backend-owned recording memory is retained until completion even if the public recording is disposed.
/// </remarks>
public abstract class GpuCommandBuffer : IDisposable
{
    protected GpuCommandBuffer() { }

    /// <summary>Snapshots non-owning attachments and begins a render scope, without overlapping another pass.</summary>
    public abstract void BeginRendering(ReadOnlySpan<GpuColorAttachment> colors, GpuDepthStencilAttachment? depthStencil = null);
    public abstract void EndRendering();
    public abstract void SetPipeline(GpuRasterPipelineHandle pipeline);
    public abstract void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor);
    public abstract void SetStencilReference(uint reference);
    public abstract void SetBlendConstant(GpuClearColor color);

    /// <summary>Snapshots dynamic offsets in ascending binding-number order for one render binding group.</summary>
    public abstract void SetBindings(uint group, GpuBindingsHandle bindings, ReadOnlySpan<uint> dynamicOffsets = default);

    /// <summary>Snapshots the complete raster program's immediate bytes without interpreting or uploading Parameter Data.</summary>
    public abstract void SetRootData(ReadOnlySpan<byte> bytes);
    public void SetRootData<T>(in T value) where T : unmanaged
        => SetRootData(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));

    public abstract void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);
    public abstract void DrawIndexed(GpuBufferRange indices, GpuIndexFormat format, uint indexCount,
        uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0);
    /// <summary>Draws once using four uint32 arguments at the beginning of the range.</summary>
    public abstract void DrawIndirect(GpuBufferRange arguments);
    /// <summary>Draws once using five 32-bit arguments, including a signed base vertex, at the beginning of the range.</summary>
    public abstract void DrawIndexedIndirect(GpuBufferRange indices, GpuIndexFormat format, GpuBufferRange arguments);

    public abstract void BeginCompute();
    public abstract void EndCompute();
    public abstract void SetComputePipeline(GpuComputePipelineHandle pipeline);

    /// <summary>Records one group and copies dynamic offsets before returning, in ascending dynamic binding order.</summary>
    public abstract void SetComputeBindings(uint group, GpuBindingsHandle bindings, ReadOnlySpan<uint> dynamicOffsets = default);

    /// <summary>Copies the complete direct root byte sequence before returning. Trailing bytes are not zero-filled.</summary>
    /// <remarks>Bytes are not interpreted to select resources or upload Parameter Data, and are never redirected to a buffer.</remarks>
    public abstract void SetComputeRootData(ReadOnlySpan<byte> bytes);

    /// <summary>Records the exact unmanaged representation of a caller-supplied Portable root structure.</summary>
    public void SetComputeRootData<T>(in T value) where T : unmanaged
        => SetComputeRootData(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));

    public abstract void Dispatch(uint x, uint y = 1, uint z = 1);

    /// <summary>Dispatches once using three uint32 group counts at the beginning of the buffer range.</summary>
    public abstract void DispatchIndirect(GpuBufferRange arguments);

    /// <summary>Copies equally sized buffer-relative byte ranges outside any compute or render scope.</summary>
    public abstract void CopyBuffer(GpuBufferRange source, GpuBufferRange destination);

    public abstract void CopyBufferToTexture(GpuBufferRange source, GpuTextureHandle texture, GpuTextureCopyFootprint footprint);
    public abstract void CopyTextureToBuffer(GpuTextureHandle texture, GpuTextureCopyFootprint footprint, GpuBufferRange destination);
    public abstract void CopyTexture(GpuTextureHandle source, GpuTextureCopyFootprint sourceFootprint,
        GpuTextureHandle destination, GpuTextureCopyFootprint destinationFootprint);

    /// <summary>Releases an unsubmitted recording, or ends the caller's ownership without cancelling submitted GPU work.</summary>
    public abstract void Dispose();
}
