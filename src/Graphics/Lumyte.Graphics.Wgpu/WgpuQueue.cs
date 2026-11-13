using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuQueue(WgpuDevice owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => Submit(new QueueSubmitDesc { CommandBuffers = commandBuffers });

    public IGraphicsSubmission Submit(QueueSubmitDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.CommandBuffers);
        ArgumentNullException.ThrowIfNull(desc.WaitSemaphores);
        ArgumentNullException.ThrowIfNull(desc.SignalSemaphores);
        if (owner.Validation != null)
        {
            foreach (SemaphoreWaitDesc wait in desc.WaitSemaphores)
            {
                if (wait.Semaphore is not WgpuSemaphore semaphore || !ReferenceEquals(semaphore.Owner, owner))
                {
                    throw new ArgumentException("Validation: semaphore belongs to another device.");
                }
            }

            foreach (IGraphicsSemaphore signal in desc.SignalSemaphores)
            {
                if (signal is not WgpuSemaphore semaphore || !ReferenceEquals(semaphore.Owner, owner))
                {
                    throw new ArgumentException("Validation: semaphore belongs to another device.");
                }
            }
        }

        owner.Validation?.ValidateSemaphores(desc);
        GraphicsValidation.SubmissionState? validationState = owner.Validation?.PrepareSubmission(desc);
        IReadOnlyList<IGraphicsCommandBuffer> commandBuffers = desc.CommandBuffers;
        if (commandBuffers.Count == 0 && desc.WaitSemaphores.Count == 0 && desc.SignalSemaphores.Count == 0)
        {
            throw new ArgumentException("A submission must contain commands, waits or signals.");
        }

        var commands = new WgpuCommandBuffer[commandBuffers.Count];
        nint[] handles = new nint[commandBuffers.Count];
        for (int i = 0; i < commandBuffers.Count; i++)
        {
            if (commandBuffers[i] is not WgpuCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner))
            {
                throw new ArgumentException("Command buffer belongs to another device.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
            handles[i] = (nint)buffer.Native;
        }

        fixed (nint* data = handles)
        {
            WGPU.wgpuQueueSubmit(owner.NativeDevice.Queue.Handle, (nuint)handles.Length, (WGPUCommandBufferImpl**)data);
        }

        if (validationState != null)
        {
            owner.Validation!.CommitSubmission(validationState);
        }

        foreach (WgpuCommandBuffer buffer in commands)
        {
            buffer.MarkSubmitted();
        }

        var submission = new WgpuSubmission(owner, commands);
        owner.Validation?.SubmittedSemaphores(submission, desc);
        return submission;
    }
}
