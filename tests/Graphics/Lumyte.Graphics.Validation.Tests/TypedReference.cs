using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class TypedReference<T>(object table, object resource, uint slot, ulong offset, ulong size, ulong count, ulong stride, ulong? registrationOffset = null, ulong? registrationSize = null) : IGpuRef<T>, IShaderReference
{
    public object Table => table;

    public object Resource => resource;

    public ShaderBufferAccess BufferAccess => ShaderBufferAccess.ReadOnly;

    public uint Slot => slot;

    public ulong OffsetInBytes => offset;

    public ulong SizeInBytes => size;

    public ulong RegistrationOffsetInBytes => registrationOffset ?? offset;

    public ulong RegistrationSizeInBytes => registrationSize ?? size;

    public ulong Count => count;

    public IGpuRef<T> GetElement(ulong index) => new TypedReference<T>(table, resource, slot, offset + (index * stride), stride, 1, stride, RegistrationOffsetInBytes, RegistrationSizeInBytes);
}
