using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using VkCommandBuffer = Silk.NET.Vulkan.CommandBuffer;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanQueue(VulkanDevice owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => Submit(new QueueSubmitDesc { CommandBuffers = commandBuffers });

    public IGraphicsSubmission Submit(QueueSubmitDesc desc)
    {
        owner.ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.CommandBuffers);
        ArgumentNullException.ThrowIfNull(desc.WaitSemaphores);
        ArgumentNullException.ThrowIfNull(desc.SignalSemaphores);
        IReadOnlyList<IGraphicsCommandBuffer> commandBuffers = desc.CommandBuffers;
        if (commandBuffers.Count == 0 && desc.WaitSemaphores.Count == 0 && desc.SignalSemaphores.Count == 0)
        {
            throw new ArgumentException("A submission must contain commands, waits or signals.");
        }

        var commands = new VulkanCommandBuffer[commandBuffers.Count];
        var handles = new VkCommandBuffer[commandBuffers.Count];
        for (int i = 0; i < commandBuffers.Count; i++)
        {
            if (commandBuffers[i] is not VulkanCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner))
            {
                throw new ArgumentException("Command buffer belongs to another device.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
            handles[i] = buffer.Native;
        }

        var waitHandles = new Semaphore[desc.WaitSemaphores.Count];
        var stages = new PipelineStageFlags[waitHandles.Length];
        for (int i = 0; i < waitHandles.Length; i++)
        {
            SemaphoreWaitDesc wait = desc.WaitSemaphores[i];
            waitHandles[i] = OwnSemaphore(wait.Semaphore).Native;
            stages[i] = NativeStages(wait.Stages);
        }

        var signalHandles = new Semaphore[desc.SignalSemaphores.Count];
        for (int i = 0; i < signalHandles.Length; i++)
        {
            signalHandles[i] = OwnSemaphore(desc.SignalSemaphores[i]).Native;
        }

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Result result = owner.Api.CreateFence(owner.NativeDevice, &fenceInfo, null, out Fence fence);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan CreateFence failed: {result}.");
        }

        fixed (VkCommandBuffer* data = handles)
        {
            fixed (Semaphore* waitData = waitHandles, signalData = signalHandles)
            {
                fixed (PipelineStageFlags* stageData = stages)
                {
                    var submit = new SubmitInfo
                    {
                        SType = StructureType.SubmitInfo,
                        CommandBufferCount = (uint)handles.Length,
                        PCommandBuffers = data,
                        WaitSemaphoreCount = (uint)waitHandles.Length,
                        PWaitSemaphores = waitData,
                        PWaitDstStageMask = stageData,
                        SignalSemaphoreCount = (uint)signalHandles.Length,
                        PSignalSemaphores = signalData,
                    };
                    result = owner.Api.QueueSubmit(owner.NativeQueue, 1, &submit, fence);
                }
            }
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

        return new VulkanSubmission(owner, commands, fence);
    }

    private static PipelineStageFlags NativeStages(PipelineStage stages)
    {
        if (stages == PipelineStage.AllCommands)
        {
            return PipelineStageFlags.AllCommandsBit;
        }

        PipelineStageFlags result = 0;
        if ((stages & PipelineStage.Copy) != 0)
        {
            result |= PipelineStageFlags.TransferBit;
        }

        if ((stages & PipelineStage.DrawIndirect) != 0)
        {
            result |= PipelineStageFlags.DrawIndirectBit;
        }

        if ((stages & PipelineStage.IndexInput) != 0)
        {
            result |= PipelineStageFlags.VertexInputBit;
        }

        if ((stages & PipelineStage.VertexShader) != 0)
        {
            result |= PipelineStageFlags.VertexShaderBit;
        }

        if ((stages & PipelineStage.FragmentShader) != 0)
        {
            result |= PipelineStageFlags.FragmentShaderBit;
        }

        if ((stages & PipelineStage.ComputeShader) != 0)
        {
            result |= PipelineStageFlags.ComputeShaderBit;
        }

        if ((stages & PipelineStage.ColorOutput) != 0)
        {
            result |= PipelineStageFlags.ColorAttachmentOutputBit;
        }

        if ((stages & PipelineStage.DepthStencil) != 0)
        {
            result |= PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
        }

        return result;
    }

    private VulkanSemaphore OwnSemaphore(IGraphicsSemaphore value)
    {
        if (value is not VulkanSemaphore semaphore || !ReferenceEquals(semaphore.Owner, owner))
        {
            throw new ArgumentException("Semaphore belongs to another device.", nameof(value));
        }

        return semaphore;
    }
}
