using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class ArgumentRegistration(BrowserArgumentTable owner, uint slot, object resource, ulong offset, ulong size, ShaderBufferAccess access)
{
    internal BrowserArgumentTable Owner { get; } = owner;

    internal ShaderBufferAccess BufferAccess { get; } = access;

    internal uint Slot { get; } = slot;

    internal object Resource { get; } = resource;

    internal ulong OffsetInBytes { get; } = offset;

    internal ulong SizeInBytes { get; } = size;
}
