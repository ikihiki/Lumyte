using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserQueue(BrowserDevice owner) : IGraphicsQueue
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
        BrowserSemaphore[] waiting = waits.Select(wait => OwnSemaphore(wait.Semaphore)).ToArray();
        BrowserSemaphore[] signaling = signals.Select(OwnSemaphore).ToArray();
        foreach (BrowserSemaphore semaphore in waiting)
        {
            semaphore.State.ValidateWait();
        }

        foreach (BrowserSemaphore semaphore in signaling)
        {
            semaphore.State.ValidateSignal();
        }

        var commands = new BrowserCommandBuffer[snapshot.Length];
        var seen = new HashSet<BrowserCommandBuffer>();
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i] is not BrowserCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner) || !seen.Add(buffer))
            {
                throw new ArgumentException("Invalid device or duplicate command buffer.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
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

        using JSObject list = BrowserInterop.CreateCommandList();
        foreach (BrowserCommandBuffer command in commands)
        {
            BrowserInterop.AddCommand(list, command.Native);
        }

        JSObject handle = BrowserInterop.SubmitCommands(owner.Handle, list);
        foreach (BrowserCommandBuffer command in commands)
        {
            command.MarkSubmitted();
        }

        var submission = new BrowserSubmission(owner, commands, handle);
        owner.RetainSubmission();
        foreach (SurfaceFrameLifetime frame in frames)
        {
            frame.MarkSubmitted(submission);
        }

        foreach (BrowserSemaphore semaphore in waiting)
        {
            semaphore.State.MarkWait(() => submission.Status != SubmissionStatus.Pending);
        }

        foreach (BrowserSemaphore semaphore in signaling)
        {
            semaphore.State.MarkSignal(() => submission.Status != SubmissionStatus.Pending);
        }

        return submission;
    }

    private BrowserSemaphore OwnSemaphore(IGraphicsSemaphore value)
    {
        if (value is not BrowserSemaphore semaphore || !ReferenceEquals(semaphore.Owner, owner))
        {
            throw new ArgumentException("Semaphore belongs to another device.", nameof(value));
        }

        semaphore.State.ValidateAlive();
        return semaphore;
    }
}
