using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    internal readonly record struct QueueSelection(uint MainFamily, uint? CopyFamily, uint CopyIndex);

    internal static QueueSelection? SelectQueues(ReadOnlySpan<QueueFamilyProperties> families)
    {
        const QueueFlags mainFlags = QueueFlags.GraphicsBit | QueueFlags.ComputeBit;
        uint? main = null;
        for (int i = 0; i < families.Length; i++)
        {
            if (families[i].QueueCount != 0 && (families[i].QueueFlags & mainFlags) == mainFlags)
            {
                main = checked((uint)i);
                break;
            }
        }
        if (main is not { } mainFamily) { return null; }
        for (int i = 0; i < families.Length; i++)
        {
            QueueFlags flags = families[i].QueueFlags;
            if (families[i].QueueCount != 0 && (flags & QueueFlags.TransferBit) != 0 && (flags & mainFlags) == 0)
            {
                return new(mainFamily, checked((uint)i), 0);
            }
        }
        if (families[checked((int)mainFamily)].QueueCount > 1) { return new(mainFamily, mainFamily, 1); }
        for (int i = 0; i < families.Length; i++)
        {
            if ((uint)i != mainFamily && families[i].QueueCount != 0
                && (families[i].QueueFlags & (mainFlags | QueueFlags.TransferBit)) != 0)
            {
                return new(mainFamily, checked((uint)i), 0);
            }
        }
        return new(mainFamily, null, 0);
    }
}
