namespace Lumyte.Graphics.Native;

/// <summary>Caller-owned descriptor storage with caller-selected slots.</summary>
/// <remarks>
/// The backend does not allocate slots or keep referenced resources alive. The caller resolves
/// recorded references and GPU use before overwriting slots or destroying this heap.
/// Native slot placement remains part of the backend's shader ABI, not a CPU or GPU pointer.
/// </remarks>
public abstract class NativeGpuDescriptorHeap
{
    protected NativeGpuDescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity)
    {
        Kind = kind;
        Capacity = capacity;
    }

    public NativeGpuDescriptorHeapKind Kind { get; }
    public uint Capacity { get; }
}
