using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class QueueNative : IGraphicsQueue
{
    internal int SubmitCount { get; private set; }

    internal QueueSubmitDesc? Last { get; private set; }

    internal Exception? Failure { get; set; }

    internal QueueSubmission Submission { get; } = new();

    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => Submit(new QueueSubmitDesc { CommandBuffers = commandBuffers });

    public IGraphicsSubmission Submit(QueueSubmitDesc desc)
    {
        SubmitCount++;
        Last = desc;
        if (Failure is { } failure)
        {
            throw failure;
        }

        return Submission;
    }
}
