using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class RawQueue : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => throw new NotSupportedException();

    public IGraphicsSubmission Submit(QueueSubmitDesc desc) => throw new NotSupportedException();
}
