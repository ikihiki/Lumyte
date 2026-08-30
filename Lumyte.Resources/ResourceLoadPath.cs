namespace Lumyte.Resources;

internal sealed class ResourceLoadPath
{
    private readonly uint slot;
    private readonly ResourceLoadPath? parent;

    internal ResourceLoadPath(uint slot, ResourceLoadPath? parent)
    {
        this.slot = slot;
        this.parent = parent;
    }

    internal ResourceLoadPath Add(uint childSlot) => new(childSlot, this);

    internal bool Contains(uint candidate)
    {
        for (ResourceLoadPath? current = this; current is not null; current = current.parent)
        {
            if (current.slot == candidate)
            {
                return true;
            }
        }

        return false;
    }
}
