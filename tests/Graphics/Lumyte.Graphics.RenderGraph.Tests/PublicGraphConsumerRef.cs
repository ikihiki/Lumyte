using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerRef<T>(ulong count) : IGpuRef<T>
{
    public ulong Count => count;

    public IGpuRef<T> GetElement(ulong index) => index < count ? new PublicGraphConsumerRef<T>(1) : throw new ArgumentOutOfRangeException(nameof(index));
}
