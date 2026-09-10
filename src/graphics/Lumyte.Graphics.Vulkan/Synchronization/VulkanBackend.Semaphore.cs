using Lumyte.Graphics.Native;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private sealed class SemaphoreRecord(QueueRecord queue, VkSemaphore semaphore) : NativeGpuSemaphore
    {
        public QueueRecord Queue { get; } = queue;
        public VkSemaphore Semaphore { get; } = semaphore;
        public bool Disposed { get; private set; }

        public override void Dispose()
        {
            if (Disposed) { return; }
            Queue.Owner.VerifyNotDisposed();
            Queue.Owner.vk.DestroySemaphore(Queue.Owner.device, Semaphore, null);
            Disposed = true;
        }
    }
}
