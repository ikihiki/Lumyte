using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Queue(Direct3D12Device owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => Submit(new QueueSubmitDesc { CommandBuffers = commandBuffers });

    public IGraphicsSubmission Submit(QueueSubmitDesc desc)
    {
        owner.RequireReady();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.CommandBuffers);
        ArgumentNullException.ThrowIfNull(desc.WaitSemaphores);
        ArgumentNullException.ThrowIfNull(desc.SignalSemaphores);
        owner.Validation?.ValidateSemaphores(desc);
        GraphicsValidation.SubmissionState? validationState = owner.Validation?.PrepareSubmission(desc);
        HashSet<Direct3D12Semaphore>? semaphores = owner.Validation == null ? null : [];
        Direct3D12Semaphore Own(IGraphicsSemaphore value)
        {
            if (value is not Direct3D12Semaphore s || !ReferenceEquals(s.Owner, owner) || (semaphores != null && !semaphores.Add(s)))
            {
                throw new ArgumentException("Each semaphore must be unique and belong to this device.");
            }

            _ = s.Native;
            return s;
        }

        Direct3D12Semaphore[] waits = desc.WaitSemaphores.Select(w =>
        {
            ArgumentNullException.ThrowIfNull(w);
            if (w.Stages == PipelineStage.None || (w.Stages & ~PipelineStage.AllCommands) != 0)
            {
                throw new ArgumentException("Semaphore waits require nonempty GPU stages.");
            }

            CommandValidation.Scope(new(w.Stages, ResourceAccess.None));
            return Own(w.Semaphore);
        }).ToArray();
        Direct3D12Semaphore[] signals = desc.SignalSemaphores.Select(Own).ToArray();
        foreach (Direct3D12Semaphore wait in waits)
        {
            wait.ValidateWait();
        }

        foreach (Direct3D12Semaphore signal in signals)
        {
            signal.ValidateSignal();
        }

        if (desc.CommandBuffers.Count == 0 && waits.Length == 0 && signals.Length == 0)
        {
            throw new ArgumentException("At least one command buffer is required.", nameof(desc));
        }

        var commands = new Direct3D12CommandBuffer[desc.CommandBuffers.Count];
        nint[] handles = new nint[commands.Length];
        HashSet<Direct3D12CommandBuffer>? unique = owner.Validation == null ? null : [];
        for (int i = 0; i < commands.Length; i++)
        {
            if (desc.CommandBuffers[i] is not Direct3D12CommandBuffer command || !ReferenceEquals(command.Owner, owner) || (unique != null && !unique.Add(command)))
            {
                throw new ArgumentException("Each command must be unique and belong to this device.", nameof(desc));
            }

            if (command.State != CommandBufferState.Executable)
            {
                throw new InvalidOperationException("Finish each command before submitting it exactly once.");
            }

            command.ValidateSubmit();
            commands[i] = command;
            handles[i] = command.Native;
        }

        owner.QueueDriver.CreateFence(owner, out nint fence);
        var submission = new Direct3D12Submission(owner, commands, fence, completionIssued: false, waits: waits, signals: signals);
        try
        {
            foreach (Direct3D12Semaphore wait in waits)
            {
                wait.Wait();
                submission.MarkNativeUse();
                owner.Validation?.SubmittedWait(submission, wait);
            }

            if (handles.Length != 0)
            {
                owner.QueueDriver.Execute(owner, handles);
                submission.MarkCommandsIssued();
            }

            if (validationState != null)
            {
                owner.Validation!.CommitSubmission(validationState);
            }

            foreach (Direct3D12CommandBuffer command in commands)
            {
                command.MarkSubmitted();
            }

            foreach (Direct3D12Semaphore signal in signals)
            {
                signal.Signal();
                submission.MarkNativeUse();
                owner.Validation?.SubmittedSignal(submission, signal);
            }

            submission.EnsureCompletionSignal();
            return submission;
        }
        catch
        {
            if (submission.HasNativeUse)
            {
                owner.Validation?.MarkPartialSubmissionFailure();
                owner.RetainFailedSubmission(submission);
                submission.TrySignalRecoveryCompletion();
            }
            else if (!submission.TryAbandonUnissued())
            {
                owner.RetainFailedSubmission(submission);
            }

            // Never replace the native operation's original error with a pending
            // fence disposal rejection or a secondary recovery signal failure.
            throw;
        }
    }
}
