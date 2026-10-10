using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanSemaphore : IGraphicsSemaphore
{
    private bool _disposed;

    internal VulkanSemaphore(VulkanDevice owner)
    {
        Owner = owner;
        var info = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        VulkanPresentation.Check(owner.Api.CreateSemaphore(owner.NativeDevice, &info, null, out Semaphore native), "CreateSemaphore");
        Native = native;
    }

    internal VulkanDevice Owner { get; }

    internal Semaphore Native { get; }

    internal BinarySemaphoreState State { get; } = new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        State.ValidateDispose();
        Owner.Api.DestroySemaphore(Owner.NativeDevice, Native, null);
        State.MarkDisposed();
        _disposed = true;
        Owner.ReleaseSemaphore();
    }
}
