using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class PresentationSemaphore : IGraphicsSemaphore
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
