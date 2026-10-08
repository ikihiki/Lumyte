namespace Lumyte.Graphics.Wgpu;

/// <summary>A non-owning data reference. No native address, binding slot or serialization API is exposed.</summary>
internal readonly struct GpuReference<T>
    where T : unmanaged
{
    internal GpuReference(BufferSlice data)
    {
        Data = data;
    }

    internal BufferSlice Data { get; }

    public override string ToString() => $"GpuReference<{typeof(T).Name}>";
}
