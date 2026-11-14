using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class ArgumentRegistration(VulkanArgumentTable owner, uint slot, object resource, ulong offset, ulong size, ulong count, ShaderBufferAccess access) : IShaderReference
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

    internal VulkanArgumentTable Owner { get; } = owner;
}
