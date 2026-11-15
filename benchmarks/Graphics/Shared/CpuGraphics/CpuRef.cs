using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuRef<T>(CpuTable table, object resource, int kind, uint slot, ulong count, ulong offset, ulong size, ulong registrationOffset, ulong registrationSize, ShaderBufferAccess access, ulong revision) : IGpuRef<T>, IShaderReference
{
    public object Table => table;

    public object Resource => resource;

    public uint Slot => slot;

    public ulong Count => count;

    public ulong OffsetInBytes => offset;

    public ulong SizeInBytes => size;

    public ulong RegistrationOffsetInBytes => registrationOffset;

    public ulong RegistrationSizeInBytes => registrationSize;

    public ShaderBufferAccess BufferAccess => access;

    public IGpuRef<T> GetElement(ulong index)
    {
        Validate();
        if (index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        ulong stride = size / count;
        return new CpuRef<T>(table, resource, kind, slot, 1, checked(offset + (index * stride)), stride, registrationOffset, registrationSize, access, revision);
    }

    internal void Validate() => table.Validate(this, kind, revision);
}
