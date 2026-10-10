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
        IGraphicsCommandBuffer[] snapshot = desc.CommandBuffers.ToArray();
        SemaphoreWaitDesc[] waits = desc.WaitSemaphores.ToArray();
        IGraphicsSemaphore[] signals = desc.SignalSemaphores.ToArray();
        if (snapshot.Length == 0 && waits.Length == 0 && signals.Length == 0)
        {
            throw new ArgumentException("A submission must contain commands, waits or signals.");
        }

        SemaphoreValidation.Submission(waits, signals);
        VulkanSemaphore[] waiting = waits.Select(wait => OwnSemaphore(wait.Semaphore)).ToArray();
        VulkanSemaphore[] signaling = signals.Select(OwnSemaphore).ToArray();
        foreach (VulkanSemaphore semaphore in waiting)
        {
            semaphore.State.ValidateWait();
        }

        foreach (VulkanSemaphore semaphore in signaling)
        {
            semaphore.State.ValidateSignal();
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

        SurfaceFrameLifetime[] frames = commands.SelectMany(command => command.SurfaceFrames).Distinct().ToArray();
        foreach (SurfaceFrameLifetime frame in frames)
        {
            frame.ValidateRecording();
            TextureState? finalState = commands.Select(command => command.GetSurfaceFinalState(frame)).Where(state => state.HasValue).LastOrDefault();
            if (finalState != TextureState.Present)
            {
                throw new InvalidOperationException("Commands using an acquired image must end in explicit Present state.");
            }
        }

        ShaderDataTransferState.ValidateSubmission(commands.Select(c => c.ShaderDataTransfers));

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Result result = owner.Api.CreateFence(owner.NativeDevice, &fenceInfo, null, out Fence fence);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan CreateFence failed: {result}.");
        }

        Semaphore[] waitHandles = waiting.Select(semaphore => semaphore.Native).ToArray();
        Semaphore[] signalHandles = signaling.Select(semaphore => semaphore.Native).ToArray();
        PipelineStageFlags[] stages = waits.Select(wait => NativeStages(wait.Stages)).ToArray();
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

        var submission = new VulkanSubmission(owner, commands, fence);
        owner.RetainSubmission();
        foreach (SurfaceFrameLifetime frame in frames)
        {
            frame.MarkSubmitted(submission);
        }

        foreach (VulkanSemaphore semaphore in waiting)
        {
            semaphore.State.MarkWait(() => submission.Status != SubmissionStatus.Pending);
        }

        foreach (VulkanSemaphore semaphore in signaling)
        {
            semaphore.State.MarkSignal(() => submission.Status != SubmissionStatus.Pending);
        }

        return submission;
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

        semaphore.State.ValidateAlive();
        return semaphore;
    }
}
