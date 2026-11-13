using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

internal struct ShaderSnapshotWriter : IShaderValueWriter
{
    internal byte[]? Data;
    internal List<ShaderValue>? Values;

    public readonly bool SupportsWireCursor => false;

    public void WriteBytes<T>(in T value)
        where T : unmanaged
    {
        if (Data != null)
        {
            throw new InvalidOperationException("A transfer must contain exactly one bulk numeric write.");
        }

        Data = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)).ToArray();
    }

    public void WriteReference<T>(int offset, IGpuRef<T>? value)
        => (Values ??= []).Add(new(typeof(T), value, offset));
}
