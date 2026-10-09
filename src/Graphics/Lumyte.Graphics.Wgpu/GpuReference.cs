using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class GpuReference<T>(ArgumentRegistration registration, ulong count, ulong stride, ulong offset, ulong size) : IGpuRef<T>
{
    public ulong Count { get; } = count;

    internal ulong OffsetInBytes { get; } = offset;

    internal ulong SizeInBytes { get; } = size;

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
