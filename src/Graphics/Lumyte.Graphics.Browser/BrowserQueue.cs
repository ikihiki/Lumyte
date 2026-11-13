using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserQueue(BrowserDevice owner) : IGraphicsQueue
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
                if (wait.Semaphore is not BrowserSemaphore semaphore || !ReferenceEquals(semaphore.Owner, owner))
                {
                    throw new ArgumentException("Validation: semaphore belongs to another device.");
                }
            }

            foreach (IGraphicsSemaphore signal in desc.SignalSemaphores)
            {
                if (signal is not BrowserSemaphore semaphore || !ReferenceEquals(semaphore.Owner, owner))
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

        var commands = new BrowserCommandBuffer[commandBuffers.Count];
        for (int i = 0; i < commandBuffers.Count; i++)
        {
            if (commandBuffers[i] is not BrowserCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner))
            {
                throw new ArgumentException("Command buffer belongs to another device.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
        }

        using JSObject list = BrowserInterop.CreateCommandList();
        foreach (BrowserCommandBuffer command in commands)
        {
            BrowserInterop.AddCommand(list, command.Native);
        }

        JSObject handle = BrowserInterop.SubmitCommands(owner.Handle, list);
        if (validationState != null)
        {
            owner.Validation!.CommitSubmission(validationState);
        }

        foreach (BrowserCommandBuffer command in commands)
        {
            command.MarkSubmitted();
        }

        var submission = new BrowserSubmission(owner, commands, handle);
        owner.Validation?.SubmittedSemaphores(submission, desc);
        return submission;
    }
}
