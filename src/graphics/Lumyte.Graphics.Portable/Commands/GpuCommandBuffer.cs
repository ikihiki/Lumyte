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

    /// <summary>Releases an unsubmitted recording, or ends the caller's ownership without cancelling submitted GPU work.</summary>
    public abstract void Dispose();
}
