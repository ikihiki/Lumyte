using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

internal sealed class ShaderSnapshotWriter : IShaderValueWriter
{
    private byte[]? _numericStorage;
    private int _numericOffset;

    internal List<ShaderValue> Values { get; } = [];

    public void WriteValue<T>(string path, in T value)
        where T : unmanaged
    {
        Type type = typeof(T);
        if (type != typeof(int) && type != typeof(uint) && type != typeof(float) && type != typeof(Vector2) && type != typeof(Vector3) && type != typeof(Vector4) && type != typeof(Matrix4x4))
        {
            throw new NotSupportedException("Unsupported numeric shader member type.");
        }

        T copy = value;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref copy, 1));
        if (_numericStorage is null || bytes.Length > _numericStorage.Length - _numericOffset)
        {
            // Keep single-member captures compact. Later members share append-only CPU storage;
            // old chunks remain owned by their values and are never reused or repacked.
            int capacity = _numericStorage is null ? bytes.Length : Math.Max(128, bytes.Length);
            _numericStorage = new byte[capacity];
            _numericOffset = 0;
        }

        bytes.CopyTo(_numericStorage.AsSpan(_numericOffset));
        ReadOnlyMemory<byte> payload = _numericStorage.AsMemory(_numericOffset, bytes.Length);
        _numericOffset += bytes.Length;
        Values.Add(new(path, type, payload, null, false));
    }

    public void WriteReference<T>(string path, IGpuRef<T>? value) => Values.Add(new(path, typeof(T), default, value, true));
}
