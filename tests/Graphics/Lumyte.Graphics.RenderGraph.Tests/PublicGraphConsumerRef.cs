using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerRef<T>(object table, object resource, uint slot, ulong count, ulong offset = 0, ulong size = 0, ulong registrationOffset = 0, ulong registrationSize = 0) : IGpuRef<T>, IShaderReference
{
    public object Table => table;

    public object Resource => resource;

    public uint Slot => slot;

    public ulong Count => count;

    public ulong OffsetInBytes => offset;

    public ulong SizeInBytes => size;

    public ulong RegistrationOffsetInBytes => registrationOffset;

    public ulong RegistrationSizeInBytes => registrationSize;

    public IGpuRef<T> GetElement(ulong index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
        ulong stride = size / count;
        return new PublicGraphConsumerRef<T>(table, resource, slot, 1, offset + (index * stride), stride, registrationOffset, registrationSize);
    }
}
