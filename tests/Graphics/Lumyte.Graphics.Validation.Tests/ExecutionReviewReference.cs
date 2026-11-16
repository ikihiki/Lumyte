using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class ExecutionReviewReference<T>(IShaderReference source, ShaderBufferAccess access) : IGpuRef<T>, IShaderReference
{
    public object Table => source.Table;

    public object Resource => source.Resource;

    public ShaderBufferAccess BufferAccess => access;

    public uint Slot => source.Slot;

    public ulong OffsetInBytes => source.OffsetInBytes;

    public ulong SizeInBytes => source.SizeInBytes;

    public ulong RegistrationOffsetInBytes => source.RegistrationOffsetInBytes;

    public ulong RegistrationSizeInBytes => source.RegistrationSizeInBytes;

    public ulong Count => source.Count;

    public IGpuRef<T> GetElement(ulong index) => throw new NotSupportedException();
}
