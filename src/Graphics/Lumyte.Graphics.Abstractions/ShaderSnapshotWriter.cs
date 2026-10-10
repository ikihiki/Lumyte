using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

internal readonly struct ShaderSnapshotWriter() : IShaderValueWriter
{
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
        byte[] bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref copy, 1)).ToArray();
        Values.Add(new(path, type, bytes, null, false));
    }

    public void WriteReference<T>(string path, IGpuRef<T>? value) => Values.Add(new(path, typeof(T), default, value, true));
}
