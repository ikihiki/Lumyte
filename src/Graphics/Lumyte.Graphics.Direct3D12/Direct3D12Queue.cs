using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Queue(Direct3D12Device owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => Submit(new QueueSubmitDesc { CommandBuffers = commandBuffers });

    public unsafe IGraphicsSubmission Submit(QueueSubmitDesc desc)
    {
        owner.RequireReady();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.CommandBuffers);
        ArgumentNullException.ThrowIfNull(desc.WaitSemaphores);
        ArgumentNullException.ThrowIfNull(desc.SignalSemaphores);
        var semaphores = new HashSet<Direct3D12Semaphore>();
        Direct3D12Semaphore Own(IGraphicsSemaphore value)
        {
            if (value is not Direct3D12Semaphore s || !ReferenceEquals(s.Owner, owner) || !semaphores.Add(s))
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
        var unique = new HashSet<Direct3D12CommandBuffer>();
        for (int i = 0; i < commands.Length; i++)
        {
            if (desc.CommandBuffers[i] is not Direct3D12CommandBuffer command || !ReferenceEquals(command.Owner, owner) || command.State != CommandBufferState.Executable || !unique.Add(command))
            {
                throw new ArgumentException("Each command must be unique, executable and belong to this device.", nameof(desc));
            }

            commands[i] = command;
            handles[i] = command.Native;
        }

        owner.Check(NativeMethods.FenceCreate(owner.Native, out nint fence));
        var submission = new Direct3D12Submission(owner, commands, fence);
        try
        {
            foreach (Direct3D12Semaphore wait in waits)
            {
                wait.Wait();
            }

            if (handles.Length != 0)
            {
                fixed (nint* data = handles)
                {
                    owner.Check(NativeMethods.CommandsExecute(owner.Native, data, (uint)handles.Length));
                }
            }

            foreach (Direct3D12CommandBuffer command in commands)
            {
                command.MarkSubmitted();
            }

            foreach (Direct3D12Semaphore signal in signals)
            {
                signal.Signal();
            }

            owner.Check(NativeMethods.QueueSignal(owner.Native, fence));
            return submission;
        }
        catch
        {
            foreach (Direct3D12CommandBuffer command in commands)
            {
                if (command.State == CommandBufferState.Submitted)
                {
                    command.Complete(false);
                }
            }

            submission.Dispose();
            throw;
        }
    }
}
