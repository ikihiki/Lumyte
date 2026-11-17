using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationQueue(ValidationDevice device, IGraphicsQueue raw) : IGraphicsQueue
{
    private readonly List<(object Operation, ValidationCommandBuffer[] Commands, IGraphicsSubmission? FailedRaw)> _unknown = [];

    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => Submit(new QueueSubmitDesc { CommandBuffers = commandBuffers });

    public IGraphicsSubmission Submit(QueueSubmitDesc desc)
    {
        device.EnsureActive();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.CommandBuffers);
        ArgumentNullException.ThrowIfNull(desc.WaitSemaphores);
        ArgumentNullException.ThrowIfNull(desc.SignalSemaphores);
        QueueSubmitDesc logical = desc with { CommandBuffers = desc.CommandBuffers.ToArray(), WaitSemaphores = desc.WaitSemaphores.Select(wait => wait with { Stages = ValidationStages.Normalize(device, wait.Stages) }).ToArray(), SignalSemaphores = desc.SignalSemaphores.ToArray() };
        ValidationCommandBuffer[] commands = logical.CommandBuffers.Select(command =>
        {
            device.Unwrap(command);
            return (ValidationCommandBuffer)command;
        }).ToArray();
        QueueSubmitDesc native = logical with
        {
            CommandBuffers = logical.CommandBuffers.Select(device.Unwrap).ToArray(),
            WaitSemaphores = logical.WaitSemaphores.Select(wait => wait with { Semaphore = device.Unwrap(wait.Semaphore) }).ToArray(),
            SignalSemaphores = logical.SignalSemaphores.Select(device.Unwrap).ToArray(),
        };
        if (commands.Length == 0 && logical.WaitSemaphores.Count == 0 && logical.SignalSemaphores.Count == 0)
        {
            throw new ArgumentException("Empty submission.", nameof(desc));
        }

        foreach (ValidationCommandBuffer? command in commands)
        {
            command.PrepareSubmit();
        }

        device.Tracker.ValidateSemaphores(logical);
        GraphicsValidation.SubmissionState state = device.Tracker.PrepareSubmission(logical);
        IGraphicsSubmission issued;
        try
        {
            issued = raw.Submit(native);
        }
        catch
        {
            RetainUnknown(logical, commands, null);
            throw;
        }

        ValidationSubmission submission;
        IGraphicsSubmission? failedRaw = null;
        try
        {
            submission = device.OwnCreated(issued, nativeSubmission => new ValidationSubmission(device, nativeSubmission, commands), failed => failedRaw = failed);
        }
        catch
        {
            // Native success cannot be undone by failure to construct its receipt.
            RetainUnknown(logical, commands, failedRaw);
            throw;
        }

        device.Tracker.CommitSubmission(state);
        foreach (ValidationCommandBuffer? command in commands)
        {
            command.OnSubmitted();
        }

        device.Tracker.SubmittedSemaphores(submission, logical);
        return submission;
    }

    internal void RetireUnknownAfterDeviceLoss()
    {
        while (_unknown.Count != 0)
        {
            (object Operation, ValidationCommandBuffer[] Commands, IGraphicsSubmission? FailedRaw) attempt = _unknown[0];
            attempt.FailedRaw?.Dispose();
            foreach (ValidationCommandBuffer command in attempt.Commands)
            {
                command.OnCompleted(false);
            }

            device.Tracker.Complete(attempt.Operation, false);
            _unknown.RemoveAt(0);
        }
    }

    private void RetainUnknown(QueueSubmitDesc logical, ValidationCommandBuffer[] commands, IGraphicsSubmission? failedRaw)
    {
        object operation = new();
        device.Tracker.MarkPartialSubmissionFailure();
        foreach (ValidationCommandBuffer command in commands)
        {
            command.MarkSubmitted();
            device.Tracker.Use(operation, command);
        }

        foreach (SemaphoreWaitDesc wait in logical.WaitSemaphores)
        {
            device.Tracker.Use(operation, wait.Semaphore);
        }

        foreach (IGraphicsSemaphore signal in logical.SignalSemaphores)
        {
            device.Tracker.Use(operation, signal);
        }

        _unknown.Add((operation, commands, failedRaw));
    }
}
