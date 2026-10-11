using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class GpuReference<T>(ArgumentRegistration registration, ulong count, ulong stride, ulong offset, ulong size) : IGpuRef<T>, IShaderReference
{
    public ulong Count { get; } = count;

    public ulong OffsetInBytes { get; } = offset;

    public ulong SizeInBytes { get; } = size;

    public ulong RegistrationOffsetInBytes => registration.OffsetInBytes;

    public ulong RegistrationSizeInBytes => registration.SizeInBytes;

    public object Table => registration.Owner;

    public object Resource => registration.Resource;

    public uint Slot => registration.Slot;

    public IGpuRef<T> GetElement(ulong index)
    {
        if (index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (Count == 1)
        {
            return this;
        }

        ulong elementOffset = checked(OffsetInBytes + (index * stride));
        return new GpuReference<T>(registration, 1, stride, elementOffset, stride);
    }
}
