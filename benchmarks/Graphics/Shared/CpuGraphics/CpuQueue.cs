using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

// Submit performs immediate SIMULATED CPU consumption, never GPU work.
internal sealed class CpuQueue(CpuGraphicsDevice owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => Submit(new QueueSubmitDesc
    {
        CommandBuffers = commandBuffers,
    });

    public IGraphicsSubmission Submit(QueueSubmitDesc desc)
    {
        owner.Check();
        if (desc.WaitSemaphores.Count != 0 || desc.SignalSemaphores.Count != 0)
        {
            throw new NotSupportedException("GPU semaphores are not modeled.");
        }

        if (desc.CommandBuffers.Count == 0)
        {
            throw new ArgumentException("Submission requires commands.");
        }

        var unique = new HashSet<IGraphicsCommandBuffer>();
        foreach (IGraphicsCommandBuffer c in desc.CommandBuffers)
        {
            owner.Own(c);
            if (c.State != CommandBufferState.Executable || !unique.Add(c))
            {
                throw new InvalidOperationException("Submission requires unique executable commands.");
            }
        }

        foreach (CpuCommands c in desc.CommandBuffers)
        {
            c.Submit();
        }

        owner.SubmitCount++;
        return new CpuSubmission(owner);
    }
}
