using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    public NativeGpuSemaphore CreateSemaphore(ulong initialValue = 0)
    {
        VerifyAvailable();
        VkSemaphore semaphore = CreateTimeline(initialValue);
        try { return new SemaphoreRecord(this, semaphore); }
        catch { vk.DestroySemaphore(device, semaphore, null); throw; }
    }

    private VkSemaphore CreateTimeline(ulong initialValue)
    {
        SemaphoreTypeCreateInfo timeline = new()
        {
            SType = StructureType.SemaphoreTypeCreateInfo, SemaphoreType = SemaphoreType.Timeline, InitialValue = initialValue,
        };
        SemaphoreCreateInfo info = new() { SType = StructureType.SemaphoreCreateInfo, PNext = &timeline };
        CheckDeviceResult(vk.CreateSemaphore(device, &info, null, out VkSemaphore semaphore), "vkCreateSemaphore");
        return semaphore;
    }

    private SemaphoreRecord RequireSemaphore(NativeGpuSemaphore semaphore, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(semaphore, parameterName);
        if (semaphore is not SemaphoreRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The semaphore belongs to another device or backend.", parameterName);
        }
        ObjectDisposedException.ThrowIf(record.Disposed, semaphore);
        return record;
    }

    private sealed class SemaphoreRecord(VulkanBackend owner, VkSemaphore semaphore) : NativeGpuSemaphore
    {
        public VulkanBackend Owner { get; } = owner;
        public VkSemaphore Semaphore { get; } = semaphore;
        public bool Disposed { get; private set; }

        public override bool IsComplete(ulong value)
        {
            VerifyAvailable();
            Owner.CheckDeviceResult(Owner.vk.GetSemaphoreCounterValue(Owner.device, Semaphore, out ulong current), "vkGetSemaphoreCounterValue");
            return current >= value;
        }

        public override void WaitCpu(ulong value)
        {
            VerifyAvailable();
            VkSemaphore handle = Semaphore;
            SemaphoreWaitInfo wait = new()
            {
                SType = StructureType.SemaphoreWaitInfo, SemaphoreCount = 1, PSemaphores = &handle, PValues = &value,
            };
            Owner.CheckDeviceResult(Owner.vk.WaitSemaphores(Owner.device, &wait, ulong.MaxValue), "vkWaitSemaphores");
        }

        public override void SignalCpu(ulong value)
        {
            VerifyAvailable();
            SemaphoreSignalInfo signal = new() { SType = StructureType.SemaphoreSignalInfo, Semaphore = Semaphore, Value = value };
            Owner.CheckDeviceResult(Owner.vk.SignalSemaphore(Owner.device, &signal), "vkSignalSemaphore");
        }

        private void VerifyAvailable()
        {
            Owner.VerifyAvailable();
            ObjectDisposedException.ThrowIf(Disposed, this);
        }

        public override void Dispose()
        {
            if (Disposed) { return; }
            Owner.VerifyNotDisposed();
            Owner.vk.DestroySemaphore(Owner.device, Semaphore, null);
            Disposed = true;
        }
    }
}
