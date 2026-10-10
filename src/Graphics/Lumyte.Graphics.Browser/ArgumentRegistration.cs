namespace Lumyte.Graphics.Browser;

internal sealed class ArgumentRegistration(BrowserArgumentTable owner, uint slot, object resource, ulong offset, ulong size)
{
    internal BrowserArgumentTable Owner { get; } = owner;

    internal uint Slot { get; } = slot;

    internal object Resource { get; } = resource;

    internal ulong OffsetInBytes { get; } = offset;

    internal ulong SizeInBytes { get; } = size;
}
