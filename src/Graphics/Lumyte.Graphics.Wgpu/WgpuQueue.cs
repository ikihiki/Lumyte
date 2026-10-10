using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuQueue(WgpuDevice owner) : IGraphicsQueue
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
        WgpuSemaphore[] waiting = waits.Select(wait => OwnSemaphore(wait.Semaphore)).ToArray();
        WgpuSemaphore[] signaling = signals.Select(OwnSemaphore).ToArray();
        foreach (WgpuSemaphore semaphore in waiting)
        {
            semaphore.State.ValidateWait();
        }

        foreach (WgpuSemaphore semaphore in signaling)
        {
            semaphore.State.ValidateSignal();
        }

        var commands = new WgpuCommandBuffer[snapshot.Length];
        nint[] handles = new nint[snapshot.Length];
        HashSet<WgpuCommandBuffer> seen = [];
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i] is not WgpuCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner) || !seen.Add(buffer))
            {
                throw new ArgumentException("Invalid device or duplicate command buffer.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
            handles[i] = (nint)buffer.Native;
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

        fixed (nint* data = handles)
        {
            WGPU.wgpuQueueSubmit(owner.NativeDevice.Queue.Handle, (nuint)handles.Length, (WGPUCommandBufferImpl**)data);
        }

        foreach (WgpuCommandBuffer buffer in commands)
        {
            buffer.MarkSubmitted();
        }

        var submission = new WgpuSubmission(owner, commands);
        owner.RetainSubmission();
        foreach (SurfaceFrameLifetime frame in frames)
        {
            frame.MarkSubmitted(submission);
        }

        foreach (WgpuSemaphore semaphore in waiting)
        {
            semaphore.State.MarkWait(() => submission.Status != SubmissionStatus.Pending);
        }

        foreach (WgpuSemaphore semaphore in signaling)
        {
            semaphore.State.MarkSignal(() => submission.Status != SubmissionStatus.Pending);
        }

        return submission;
    }

    private WgpuSemaphore OwnSemaphore(IGraphicsSemaphore value)
    {
        if (value is not WgpuSemaphore semaphore || !ReferenceEquals(semaphore.Owner, owner))
        {
            throw new ArgumentException("Semaphore belongs to another device.", nameof(value));
        }

        semaphore.State.ValidateAlive();
        return semaphore;
    }
}
