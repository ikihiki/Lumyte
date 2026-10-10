using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class GpuReference<T>(ArgumentRegistration registration, ulong count, ulong stride, ulong offset, ulong size) : IGpuRef<T>, IShaderReference
{
    public ulong Count { get; } = count;

    public ulong OffsetInBytes { get; } = offset;

    public ulong SizeInBytes { get; } = size;

    public ulong RegistrationOffsetInBytes => registration.OffsetInBytes;

    public ulong RegistrationSizeInBytes => registration.SizeInBytes;

    public object Table => registration.Owner;

    public object Resource => Resolve();

    public uint Slot => registration.Slot;

    public void Validate() => registration.Check();

    public IGpuRef<T> GetElement(ulong index)
    {
        registration.Check();
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

    internal object Resolve()
    {
        registration.Check();
        return registration.Resource;
    }
}
