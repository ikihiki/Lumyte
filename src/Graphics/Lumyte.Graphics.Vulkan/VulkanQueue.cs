using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using VkCommandBuffer = Silk.NET.Vulkan.CommandBuffer;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanQueue(VulkanDevice owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers)
    {
        owner.ValidateAlive();
        ArgumentNullException.ThrowIfNull(commandBuffers);
        IGraphicsCommandBuffer[] snapshot = commandBuffers.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("Empty submission.");
        }

        var commands = new VulkanCommandBuffer[snapshot.Length];
        var handles = new VkCommandBuffer[snapshot.Length];
        var seen = new HashSet<VulkanCommandBuffer>();
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i] is not VulkanCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner) || !seen.Add(buffer))
            {
                throw new ArgumentException("Invalid device or duplicate command buffer.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
            handles[i] = buffer.Native;
        }

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Result result = owner.Api.CreateFence(owner.NativeDevice, &fenceInfo, null, out Fence fence);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan CreateFence failed: {result}.");
        }

        fixed (VkCommandBuffer* data = handles)
        {
            var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = (uint)handles.Length, PCommandBuffers = data };
            result = owner.Api.QueueSubmit(owner.NativeQueue, 1, &submit, fence);
        }

        if (result != Result.Success)
        {
            if (result == Result.ErrorDeviceLost)
            {
                foreach (VulkanCommandBuffer command in commands)
                {
                    command.Complete(false);
                }
            }

            owner.Api.DestroyFence(owner.NativeDevice, fence, null);
            throw new InvalidOperationException($"Vulkan QueueSubmit failed: {result}.");
        }

        foreach (VulkanCommandBuffer command in commands)
        {
            command.MarkSubmitted();
        }

        var submission = new VulkanSubmission(owner, commands, fence);
        owner.RetainSubmission();
        return submission;
    }
}
