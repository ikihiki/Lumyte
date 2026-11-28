using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Retains exact table/allocation/range metadata for deterministic typed reference assertions.</summary>
internal sealed class ManagedReference<T>(ulong count, object? table = null, object? resource = null, uint slot = 0, ulong offset = 0, ulong stride = 1, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly, ulong? registrationOffset = null, ulong? registrationSize = null) : IGpuRef<T>, IShaderReference
{
    public ulong Count => count;

    public object Table => table ?? this;

    public object Resource => resource ?? this;

    public ShaderBufferAccess BufferAccess => access;

    public uint Slot => slot;

    public ulong OffsetInBytes => offset;

    public ulong SizeInBytes => checked(count * stride);

    public ulong RegistrationOffsetInBytes => registrationOffset ?? offset;

    public ulong RegistrationSizeInBytes => registrationSize ?? SizeInBytes;

    public IGpuRef<T> GetElement(ulong index)
        => index < count ? new ManagedReference<T>(1, Table, Resource, slot, checked(offset + (index * stride)), stride, access, RegistrationOffsetInBytes, RegistrationSizeInBytes) : throw new ArgumentOutOfRangeException(nameof(index));
}
