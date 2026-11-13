using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

// Explicit CPU inspection only. Snapshots are not a native upload destination.
internal struct ShaderSnapshotWriter : IShaderValueWriter, IShaderWireSink
{
    internal byte[]? Data;
    internal List<ShaderValue>? Values;

    public void WriteWire<TWire, TEmitter>(in TWire wire, ref TEmitter emitter)
        where TWire : unmanaged
        where TEmitter : struct, IShaderReferenceEmitter
    {
        if (Data != null)
        {
            throw new InvalidOperationException("A snapshot must contain exactly one wire value.");
        }

        Data = new byte[Unsafe.SizeOf<TWire>()];
        ReadOnlySpan<byte> source = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
        var cursor = new ShaderWireCursor<ShaderSnapshotWriter>(source, ref this);
        emitter.Emit(ref cursor);
        cursor.Complete();
    }

    void IShaderWireSink.WriteRaw(ReadOnlySpan<byte> source, int offset)
        => source.CopyTo(Data!.AsSpan(offset, source.Length));

    void IShaderWireSink.WriteReference<T>(int offset, IGpuRef<T>? value)
    {
        (Values ??= []).Add(new(typeof(T), value, offset));
        Data!.AsSpan(offset, 16).Clear();
    }
}
