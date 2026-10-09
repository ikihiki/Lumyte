using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class GpuReference<T> : IGpuRef<T>
{
    private readonly ArgumentRegistration _registration;
    private readonly ulong _stride;

    internal GpuReference(ArgumentRegistration registration, ulong count, ulong stride, ulong offset, ulong size)
    {
        (_registration, Count, _stride, OffsetInBytes, SizeInBytes) = (registration, count, stride, offset, size);
    }

    public ulong Count { get; }

    internal ulong OffsetInBytes { get; }

    internal ulong SizeInBytes { get; }

    public IGpuRef<T> GetElement(ulong index)
    {
        _registration.Check();
        if (index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (Count == 1)
        {
            return this;
        }

        ulong offset = checked(OffsetInBytes + (index * _stride));
        return new GpuReference<T>(_registration, 1, _stride, offset, _stride);
    }

    internal object Resolve()
    {
        _registration.Check();
        return _registration.Resource;
    }
}
