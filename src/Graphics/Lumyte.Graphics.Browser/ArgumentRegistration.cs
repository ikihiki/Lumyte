using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class ArgumentRegistration(BrowserArgumentTable owner, uint slot, object resource, ulong offset, ulong size, ulong count, ShaderBufferAccess access) : IShaderReference
{
    public ShaderBufferAccess BufferAccess { get; } = access;

    public uint Slot { get; } = slot;

    public object Resource { get; } = resource;

    public ulong OffsetInBytes { get; } = offset;

    public ulong SizeInBytes { get; } = size;

    public object Table => Owner;

    public ulong Count { get; } = count;

    public ulong RegistrationOffsetInBytes => OffsetInBytes;

    public ulong RegistrationSizeInBytes => SizeInBytes;

    internal BrowserArgumentTable Owner { get; } = owner;
}
