using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using VkCommandBuffer = Silk.NET.Vulkan.CommandBuffer;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanQueue(VulkanDevice owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => SubmitCore(commandBuffers, null);

    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers, IGraphicsSurfaceFrame frame)
    {
        if (frame is not VulkanSurfaceFrame acquired || !ReferenceEquals(acquired.Owner, owner))
        {
            throw new ArgumentException("Presentation frame belongs to another device.", nameof(frame));
        }

        acquired.Lifetime.ValidateRecording();
        return SubmitCore(commandBuffers, acquired);
    }

    private IGraphicsSubmission SubmitCore(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers, VulkanSurfaceFrame? frame)
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

        TextureState? finalSurfaceState = null;
        foreach (VulkanCommandBuffer command in commands)
        {
            finalSurfaceState = command.ValidateSurfaceSubmission(frame?.Lifetime) ?? finalSurfaceState;
        }

        if (frame != null && finalSurfaceState != TextureState.Present)
        {
            throw new InvalidOperationException("A frame submission must use its image and end in explicit Present state.");
        }

        ShaderDataTransferState.ValidateSubmission(commands.Select(c => c.ShaderDataTransfers));

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Result result = owner.Api.CreateFence(owner.NativeDevice, &fenceInfo, null, out Fence fence);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan CreateFence failed: {result}.");
        }

        fixed (VkCommandBuffer* data = handles)
        {
            var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = (uint)handles.Length, PCommandBuffers = data };
            Semaphore acquire = frame?.AcquireSemaphore ?? default;
            Semaphore rendered = frame?.RenderSemaphore ?? default;
            PipelineStageFlags waitStage = PipelineStageFlags.AllCommandsBit;
            if (frame != null)
            {
                submit.WaitSemaphoreCount = 1;
                submit.PWaitSemaphores = &acquire;
                submit.PWaitDstStageMask = &waitStage;
                submit.SignalSemaphoreCount = 1;
                submit.PSignalSemaphores = &rendered;
            }

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
        frame?.Lifetime.MarkSubmitted(submission);
        return submission;
    }
}
